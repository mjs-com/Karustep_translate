using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using System.Text.RegularExpressions;

namespace VoiceRecorder
{
    /// <summary>
    /// 1人の患者（1つの診察セッション）の状態を管理するクラス
    /// Phase 1: シングルセッション対応（タブ機能はPhase 2で追加）
    /// </summary>
    public class RecordingSession : INotifyPropertyChanged, IDisposable
    {
        // --- 基本プロパティ ---
        // パフォーマンスモード (0: Realtime, 1: Balanced, 2: LowLoad, 3: UltraLowLoad)
        public static int CurrentPerformanceMode { get; set; } = 0;

        private string _patientName = "(未設定)";
        public string PatientName
        {
            get => _patientName;
            set { _patientName = value; OnPropertyChanged(); }
        }

        private StringBuilder _accumulatedTranscript = new StringBuilder(); // 表示用キャッシュ
        public string AccumulatedTranscript => _accumulatedTranscript.ToString();

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            private set { _isRecording = value; OnPropertyChanged(); }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            private set { _isPaused = value; OnPropertyChanged(); }
        }

        private bool _isStopped;
        public bool IsStopped
        {
            get => _isStopped;
            private set { _isStopped = value; OnPropertyChanged(); }
        }

        // --- 事前情報関連（v30.0追加） ---
        private string _preInfoText = "";
        public string PreInfoText
        {
            get => _preInfoText;
            set 
            { 
                _preInfoText = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(HasPreInfo)); 
            }
        }

        /// <summary>
        /// 事前情報が入力済みかどうか
        /// </summary>
        public bool HasPreInfo => !string.IsNullOrWhiteSpace(_preInfoText);

        /// <summary>
        /// 事前情報ファイルのパス
        /// </summary>
        public string PreInfoFilePath { get; private set; } = "";

        // --- 録音開始時刻（タブ表示用） ---
        private DateTime? _recordingStartTime;
        public DateTime? RecordingStartTime
        {
            get => _recordingStartTime;
            private set { _recordingStartTime = value; OnPropertyChanged(); }
        }

        // --- ファイル管理 ---
        public string SessionId { get; private set; }
        public string OutputDirectory { get; private set; } = ""; // Initialize()で設定される
        public string CurrentTextFilePath { get; private set; } = ""; // 追記対象のファイルパス
        public string SummaryFilePath { get; private set; } = ""; // 要約結果ファイルのパス
        public List<string> SessionRecordingFiles { get; } = new List<string>();

        // --- 内部コンポーネント ---
        public SoundRecorder? Recorder { get; private set; }

        // --- チャンク処理の追跡（対策1, 3） ---
        private readonly List<Task> _pendingChunkTasks = new List<Task>();
        private readonly object _chunkTasksLock = new object();
        private volatile bool _isStopping = false; // 停止処理中フラグ（他スレッドからの可視性を担保）
        
        // --- ファイル書き込み用のロック（cannot access a closed fileエラー対策） ---
        private readonly object _fileWriteLock = new object();

        // --- イベント ---
        /// <summary>
        /// 音声チャンクが生成されたら発火（MainWindowで購読してAPIへ送信）
        /// </summary>
        public event EventHandler<byte[]>? ChunkReady;

        /// <summary>
        /// 長時間無音が検出されたら発火（自動停止用）
        /// </summary>
        public event EventHandler? SilenceDetected;

        /// <summary>
        /// テキストが更新されたら発火（UI更新用）
        /// </summary>
        public event EventHandler? TranscriptUpdated;

        // --- コンストラクタ ---
        public RecordingSession()
        {
            SessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        // --- セッションIDの再生成（録音停止後の新規録音用） ---
        public void ResetSessionId()
        {
            SessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            CurrentTextFilePath = ""; // テキストファイルパスもリセット
            SummaryFilePath = ""; // 要約ファイルパスもリセット
            _accumulatedTranscript.Clear(); // 文字起こしもクリア
            RecordingStartTime = null; // 録音開始時刻もリセット
            SessionRecordingFiles.Clear(); // 【追加】過去の録音ファイルリストをクリアして肥大化を防ぐ
            OnPropertyChanged(nameof(AccumulatedTranscript));
            IsStopped = false; // 停止状態もリセット
            
            // v30.0: 事前情報もリセット
            PreInfoText = "";
            PreInfoFilePath = "";
        }

        // --- 初期化 ---
        public void Initialize(string outputDir)
        {
            OutputDirectory = outputDir;
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }
        }

        // --- 録音開始 ---
        // 同期版（互換性のため残すが、UIスレッドからの呼び出しは非推奨）
        public void StartRecording()
        {
            StartRecordingAsync().GetAwaiter().GetResult();
        }

        // 非同期版（UIフリーズ防止のためこちらを使用する）
        public async Task StartRecordingAsync()
        {
            if (IsRecording)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ StartRecordingAsync: 既に録音中のため、録音開始をスキップします");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ StartRecordingAsync: 録音開始処理を開始します (SessionId: {SessionId}, IsStopped: {IsStopped})");

            try
            {
                // チャンク処理の追跡をリセット
                lock (_chunkTasksLock)
                {
                    _pendingChunkTasks.Clear();
                    _isStopping = false;
                }

                // 【追加】古いRecorderのイベント購読を解除（メモリリーク防止）
                if (Recorder != null)
                {
                    Recorder.ChunkReady -= Recorder_ChunkReady;
                    Recorder.SilenceDetected -= Recorder_SilenceDetected;
                    Recorder.Dispose();
                    Recorder = null;
                }

                // 音声ファイル作成
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string wavPath = Path.Combine(OutputDirectory, $"recording_{SessionId}_{timestamp}.wav");
                SessionRecordingFiles.Add(wavPath);

                // テキストファイルパスの確定（初回のみ）
                if (string.IsNullOrEmpty(CurrentTextFilePath))
                {
                    string fileName = Path.GetFileNameWithoutExtension(wavPath) + ".txt";
                    CurrentTextFilePath = Path.Combine(OutputDirectory, fileName);
                    // 空ファイルを作成しておく（存在確認のため）
                    if (!File.Exists(CurrentTextFilePath))
                    {
                        File.WriteAllText(CurrentTextFilePath, "");
                    }
                }

                // SoundRecorder作成とイベント購読
                Recorder = new SoundRecorder(wavPath);
                // Phase 1: MainWindow.CurrentRecorder は MainWindow 側で設定（外部からは設定不可）

                // ChunkReadyイベントのラップ
                Recorder.ChunkReady += Recorder_ChunkReady;

                // SilenceDetectedイベントの転送
                Recorder.SilenceDetected += Recorder_SilenceDetected;

                // 録音開始（非同期で実行し、デバイスロック待ちによるUIフリーズを防ぐ）
                System.Diagnostics.Debug.WriteLine($"🎤 StartRecordingAsync: Recorder.StartRecording() を呼び出します (ファイル: {wavPath})");
                await Task.Run(() => Recorder.StartRecording());
                System.Diagnostics.Debug.WriteLine($"✅ StartRecordingAsync: Recorder.StartRecording() が完了しました");
                
                RecordingStartTime = DateTime.Now; // 録音開始時刻を記録
                IsRecording = true;
                IsPaused = false;
                System.Diagnostics.Debug.WriteLine($"✅ StartRecordingAsync: 録音開始完了 (IsRecording: {IsRecording}, SessionId: {SessionId})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"録音開始エラー: {ex.Message}");
                throw;
            }
        }

        // 【追加】イベントハンドラメソッド（解除しやすくするためにラムダ式からメソッドへ変更）
        private void Recorder_ChunkReady(object? sender, SoundRecorder.ChunkReadyEventArgs args)
        {
            // 停止処理中は新しいチャンクを処理しない
            if (_isStopping) return;
            ChunkReady?.Invoke(this, args.AudioData);
        }

        private void Recorder_SilenceDetected(object? sender, EventArgs e)
        {
            SilenceDetected?.Invoke(this, e);
        }

        // --- 一時停止 ---
        public void PauseRecording()
        {
            if (!IsRecording || IsPaused) return;

            try
            {
                // 残っている音声チャンクがあれば送信する
                if (Recorder != null)
                {
                    var remainingData = Recorder.GetRemainingChunk();
                    if (remainingData != null && remainingData.Length > 0)
                    {
                        ChunkReady?.Invoke(this, remainingData);
                    }
                    Recorder.StopRecording();
                }

                IsPaused = true;
                // 一時停止時は録音状態は維持（IsRecording = true のまま）
                
                // 一時停止のタイミングで患者名を推定・更新（前の名前があっても上書き）
                ExtractPatientNameFromText();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"一時停止エラー: {ex.Message}");
                throw;
            }
        }

        // --- 録音再開 ---
        public void ResumeRecording()
        {
            if (!IsPaused) return;

            try
            {
                // 【追加】古いRecorderのイベント購読を解除（メモリリーク防止）
                if (Recorder != null)
                {
                    Recorder.ChunkReady -= Recorder_ChunkReady;
                    Recorder.SilenceDetected -= Recorder_SilenceDetected;
                    Recorder.Dispose();
                    Recorder = null;
                }

                // 新しい録音ファイルを作成（同じセッションIDを使用）
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string wavPath = Path.Combine(OutputDirectory, $"recording_{SessionId}_{timestamp}.wav");
                SessionRecordingFiles.Add(wavPath);

                // SoundRecorder作成とイベント購読
                Recorder = new SoundRecorder(wavPath);
                // Phase 1: MainWindow.CurrentRecorder は MainWindow 側で設定（外部からは設定不可）

                Recorder.ChunkReady += Recorder_ChunkReady;
                Recorder.SilenceDetected += Recorder_SilenceDetected;

                Recorder.StartRecording();
                IsPaused = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"録音再開エラー: {ex.Message}");
                throw;
            }
        }

        // --- 録音停止 ---
        // 【修正】デバイス停止はGetFinalChunkAndStopDevice()で行うため、ここではフラグ更新とクリーンアップのみ
        public async Task StopRecordingAsync()
        {
            if (!IsRecording) return;

            // 1. 【変更】フラグを最優先で更新（UIを即座に解放するため）
            IsRecording = false; 
            IsPaused = false;
            IsStopped = true;

            // 停止処理中フラグを立てる（新しいチャンクの処理を防ぐ）
            lock (_chunkTasksLock)
            {
                _isStopping = true;
            }

            // 2. Recorderのクリーンアップ（デバイス停止はGetFinalChunkAndStopDevice()で既に行われている）
            await Task.Run(() =>
            {
                try
                {
                    if (Recorder != null)
                    {
                        // Disposeは必ず実行（リソースリーク防止）
                        try
                        {
                            Recorder.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Recorder.Disposeエラー: {ex.Message}");
                        }
                        Recorder = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"録音停止エラー: {ex.Message}");
                }
            });
        }
        
        // --- 最後のチャンクを取得して返す（MainWindow用） ---
        public byte[]? GetFinalChunkAndStopDevice()
        {
            byte[]? lastChunk = null;
            
            try
            {
                if (Recorder != null)
                {
                    // 1. まずデバイスを停止（NAudioが残りバッファをフラッシュする）
                    try
                    {
                        Recorder.StopRecording();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"デバイス停止エラー: {ex.Message}");
                    }
                    
                    // 2. 停止後に残りチャンクを取得（フラッシュ後のデータを含む）
                    lastChunk = Recorder.GetRemainingChunk();
                    System.Diagnostics.Debug.WriteLine($"📦 GetFinalChunkAndStopDevice: {(lastChunk?.Length ?? 0)} bytes取得");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetFinalChunkAndStopDeviceエラー: {ex.Message}");
            }
            
            return lastChunk;
        }

        // --- 要約ファイルパスの設定（MainWindowから呼ばれる） ---
        public void SetSummaryFilePath(string summaryFilePath)
        {
            SummaryFilePath = summaryFilePath;
        }

        // --- 事前情報の保存（v30.0追加） ---
        /// <summary>
        /// 事前情報を保存し、患者名を設定する
        /// </summary>
        /// <param name="title">患者名（タブに表示される）</param>
        /// <param name="preInfoText">事前情報テキスト</param>
        public void SavePreInfo(string title, string preInfoText)
        {
            // 患者名を設定（タブに表示される）
            PatientName = title;
            PreInfoText = preInfoText;

            // 事前情報ファイルを保存
            if (!string.IsNullOrEmpty(OutputDirectory))
            {
                PreInfoFilePath = Path.Combine(OutputDirectory, $"preinfo_{SessionId}.txt");
                try
                {
                    File.WriteAllText(PreInfoFilePath, preInfoText);
                    System.Diagnostics.Debug.WriteLine($"✅ 事前情報を保存しました: {PreInfoFilePath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 事前情報の保存に失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 要約用に事前情報と文字起こしを結合したテキストを取得する
        /// </summary>
        /// <returns>結合されたテキスト</returns>
        public string GetCombinedTextForSummary()
        {
            var sb = new StringBuilder();

            // 事前情報がある場合は追加
            if (HasPreInfo)
            {
                sb.AppendLine("【事前情報】");
                // ファイルがあればファイルから、なければメモリから
                if (!string.IsNullOrEmpty(PreInfoFilePath) && File.Exists(PreInfoFilePath))
                {
                    sb.AppendLine(File.ReadAllText(PreInfoFilePath));
                }
                else
                {
                    sb.AppendLine(PreInfoText);
                }
                sb.AppendLine();
            }

            // 文字起こしがある場合は追加
            if (!string.IsNullOrEmpty(CurrentTextFilePath) && File.Exists(CurrentTextFilePath))
            {
                sb.AppendLine("【診察内容（文字起こし）】");
                sb.AppendLine(File.ReadAllText(CurrentTextFilePath));
            }
            else if (!string.IsNullOrEmpty(AccumulatedTranscript))
            {
                // ファイルがなければメモリから
                sb.AppendLine("【診察内容（文字起こし）】");
                sb.AppendLine(AccumulatedTranscript);
            }

            return sb.ToString();
        }

        // --- すべてのチャンク処理の完了を待機（対策1, 3） ---
        public async Task WaitForAllChunksAsync()
        {
            Task[] tasks;
            lock (_chunkTasksLock)
            {
                // 【修正】完了済みのタスクを一括でお掃除（ここでやるのが一番効率的）
                _pendingChunkTasks.RemoveAll(t => t.IsCompleted);
                tasks = _pendingChunkTasks.ToArray();
            }

            if (tasks.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"進行中のチャンク処理を待機中: {tasks.Length}件");
                try
                {
                    // 【修正】タイムアウト設定を延長（低負荷モードでは最後のチャンクが最大5分になるため）
                    // タイムアウトは警告のみで、実際の待機は継続する（要約処理を開始しない）
                    var allTasksTask = Task.WhenAll(tasks);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(360)); // 6分（低負荷モードの5分 + 余裕）
                    var completedTask = await Task.WhenAny(allTasksTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ チャンク処理の待機がタイムアウトしました（6分）。ただし、完了まで待機を継続します。");
                        // タイムアウトしても、実際の完了を待つ（要約処理を開始しない）
                        await allTasksTask;
                        System.Diagnostics.Debug.WriteLine("すべてのチャンク処理が完了しました（タイムアウト後も待機）");
                    }
                    else
                    {
                        // タイムアウト前に完了した場合、結果を待機（エラーがあればここで例外が発生）
                        await allTasksTask;
                        System.Diagnostics.Debug.WriteLine("すべてのチャンク処理が完了しました");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"チャンク処理待機中のエラー: {ex.Message}");
                    // エラーが発生しても処理は継続（一部のチャンクが失敗しても全体を止めない）
                }
            }
        }

        // --- チャンク処理の登録（MainWindowから呼ばれる） ---
        public void RegisterChunkTask(Task task)
        {
            if (task == null) return;
            lock (_chunkTasksLock)
            {
                if (!_isStopping)
                {
                    _pendingChunkTasks.Add(task);
                }
            }
        }

        // --- 文字起こし結果の追加（重要：ファイル追記） ---
        public void AppendTranscript(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. メモリ（表示用）に追加
            _accumulatedTranscript.AppendLine(text);
            OnPropertyChanged(nameof(AccumulatedTranscript));

            // 2. ファイル（正本）に追記（ロックで保護して同時書き込みを防止）
            if (!string.IsNullOrEmpty(CurrentTextFilePath))
            {
                lock (_fileWriteLock)
                {
                    try
                    {
                        File.AppendAllText(CurrentTextFilePath, text + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ファイル追記エラー: {ex.Message}");
                    }
                }
            }

            // 3. イベント発火（UI更新用）
            TranscriptUpdated?.Invoke(this, EventArgs.Empty);
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- IDisposable ---
        public void Dispose()
        {
            Recorder?.Dispose();
            Recorder = null;
            // Phase 1: MainWindow.CurrentRecorder は MainWindow 側でクリア（外部からは設定不可）
        }

        // --- 患者名抽出ロジック ---
        // 除外ワードリスト
        private static readonly HashSet<string> _nameExclusionList = new HashSet<string>
        {
            "奥", "旦那", "主人", "母", "父", "祖父", "祖母", "兄", "姉",
            "お母", "お父", "お祖父", "お祖母", "お兄", "お姉",
            "おば", "おじ", "おばあ", "おじい",
            "先生", "看護師", "薬剤師", "技師", "事務", "スタッフ", "担当",
            "みな", "皆", "患者", "隣の方", "誰々", "お客", "お客様",
            "お疲れ", "ご苦労", "はい", "いいえ", "そう", "あー", "えー"
        };

        private void ExtractPatientNameFromText()
        {
            string text = AccumulatedTranscript;
            if (string.IsNullOrEmpty(text)) return;

            // 冒頭500文字に限定
            if (text.Length > 500)
            {
                text = text.Substring(0, 500);
            }

            // 名前パターン: 2~10文字の漢字/ひらがな/カタカナ + 敬称
            // "鈴木さん"、"鈴木花子さん"
            // [一-龠ぁ-んァ-ヶ] はJIS第1/第2水準漢字、ひらがな、カタカナをカバー
            // 々 などの記号も名前に含まれることがあるため追加
            string pattern = @"([一-龠ぁ-んァ-ヶ々]{2,10})[ 　]*(さん|さま|様|くん|君|ちゃん|チャン)";
            var matches = Regex.Matches(text, pattern);

            if (matches.Count == 0) return;

            string? bestName = null;
            
            foreach (Match match in matches)
            {
                string namePart = match.Groups[1].Value;
                
                // 除外チェック
                bool isExcluded = false;
                foreach (var exclude in _nameExclusionList)
                {
                    // 完全一致または一部に除外ワードが含まれるか（文脈によるが、"奥さん"などは除外）
                    // namePart自体が除外ワードを含むかチェック
                    if (namePart.Contains(exclude))
                    {
                        isExcluded = true;
                        break;
                    }
                }
                if (isExcluded) continue;

                // 最初の候補が見つかった場合
                if (bestName == null)
                {
                    bestName = namePart;
                }
                else
                {
                    // 既に候補がある場合、現在の候補がより長いフルネームかどうかチェック
                    // 例：bestName="鈴木", namePart="鈴木花子" -> "鈴木花子"を採用
                    if (namePart.Length > bestName.Length && namePart.Contains(bestName))
                    {
                        bestName = namePart;
                    }
                    // 逆の場合（フルネームの後に名字が来た場合）は更新しない
                }
            }

            if (bestName != null)
            {
                // 患者名を更新（上書き）
                // Dispatcher経由で更新する必要はない（OnPropertyChangedでバインディング更新される）
                PatientName = bestName;
            }
        }
    }
}
