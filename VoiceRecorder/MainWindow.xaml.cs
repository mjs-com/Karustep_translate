using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using DrawingIcon = System.Drawing.Icon;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using WinForms = System.Windows.Forms;
using DotNetEnv; // 追加
using Microsoft.VisualBasic;


namespace VoiceRecorder
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // 停止処理の競合を防ぐためのフラグ（対策1）
        private bool _isStopping = false;

        // 【v28.4】統一ガードタイム機構（チャタリング対策）
        // 入力経路（ボタン/ホットキー）に関わらず、一定時間内の連続操作を確実にブロックする
        private DateTime _lastRecordStateChangeAt = DateTime.MinValue;
        private const double GUARD_TIME_SECONDS = 1.5;
        
        private bool IsWithinGuardTime()
        {
            return (DateTime.UtcNow - _lastRecordStateChangeAt).TotalSeconds < GUARD_TIME_SECONDS;
        }

        public static SoundRecorder? CurrentRecorder { get; private set; }
        // CurrentSelectedPrompt は絶対パスで保持する
        public static string CurrentSelectedPrompt { get; private set; } = string.Empty;
        // Windows API constants for hotkey registration
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_WIN = 0x0008; // Windowsキーのモディファイアを追加
        private const int MOD_ALT = 0x0001; // 追加
        private const int VK_COMMA = 0xBC;
        private const int VK_PERIOD = 0xBE;
        private const int HOTKEY_ID = 9000;
        private const int PAUSE_HOTKEY_ID = 9001;
        private const int COPY_HOTKEY_ID = 9002;

        // P/Invoke declarations for Windows API functions
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 自動貼り付け機能用のWindows API
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        // 追加のWindows API
        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPoint(IntPtr hWndParent, POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT Point, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr RealChildWindowFromPoint(IntPtr hWndParent, POINT ptParentClientCoords);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // 貼り付け用のWindows API
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // SendInput用の構造体
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // SendInput用の定数
        private const uint INPUT_KEYBOARD = 1;
        private const uint INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        // キーボード仮想キーコード
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;

        // ChildWindowFromPointEx用のフラグ
        private const uint CWP_ALL = 0x0000;
        private const uint CWP_SKIPINVISIBLE = 0x0001;
        private const uint CWP_SKIPDISABLED = 0x0002;
        private const uint CWP_SKIPTRANSPARENT = 0x0004;

        // GetWindow用の定数
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // Windows Messages
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_SETFOCUS = 0x0007;

        // mouse_event用の定数
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
 
        // For flashing taskbar icon
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
 
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }
 
        // Flash flags
        private const uint FLASHW_STOP = 0;       // Stop flashing
        private const uint FLASHW_ALL = 3;        // Flash both window and taskbar button
        private const uint FLASHW_CAPTION = 1;    // Flash the window caption
        private const uint FLASHW_TRAY = 2;       // Flash the taskbar button
        private const uint FLASHW_TIMER = 4;      // Flash continuously using timer
        private const uint FLASHW_TIMERNOFG = 12; // Flash continuously until window comes to foreground
 
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private string outputDirectory = "C:\\temp";
        
        // Phase 2: 複数セッション管理
        private ObservableCollection<RecordingSession> _sessions = new ObservableCollection<RecordingSession>();
        public ObservableCollection<RecordingSession> Sessions
        {
            get => _sessions;
            private set
            {
                _sessions = value;
                OnPropertyChanged();
            }
        }

        // Phase 1: RecordingSession に置き換え（Phase 2: 選択されたセッションを保持）
        private RecordingSession? _currentSession;
        public RecordingSession? CurrentSession
        {
            get => _currentSession;
            private set
            {
                if (_currentSession != value)
                {
                    // 古いセッションのイベント購読を解除
                    if (_currentSession != null)
                    {
                        _currentSession.PropertyChanged -= CurrentSession_PropertyChanged;
                        _currentSession.ChunkReady -= CurrentSession_ChunkReady;
                        _currentSession.SilenceDetected -= CurrentSession_SilenceDetected;
                        _currentSession.TranscriptUpdated -= CurrentSession_TranscriptUpdated;
                    }

                    _currentSession = value;

                    // 新しいセッションのイベント購読
                    if (_currentSession != null)
                    {
                        _currentSession.PropertyChanged += CurrentSession_PropertyChanged;
                        _currentSession.ChunkReady += CurrentSession_ChunkReady;
                        _currentSession.SilenceDetected += CurrentSession_SilenceDetected;
                        _currentSession.TranscriptUpdated += CurrentSession_TranscriptUpdated;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRecording));
                    OnPropertyChanged(nameof(IsPaused));
                    UpdateUIForCurrentSession();
                }
            }
        }

        // CurrentSession のプロパティ変更を監視
        private void CurrentSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordingSession.IsRecording) || 
                e.PropertyName == nameof(RecordingSession.IsPaused))
            {
                this.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(IsRecording));
                    OnPropertyChanged(nameof(IsPaused));
                    UpdateButtonAppearance();
                });
            }
        }
        
        // ライセンス認証関連
        private bool _isLicensed = false;
        private string _hardwareId = "";
        // 【追加】セマフォの実体。初期値は余裕を持たせる。
        private static System.Threading.SemaphoreSlim _uploadSemaphore = new System.Threading.SemaphoreSlim(10);

        // 【追加】モードごとの並列数を返すヘルパー
        private int GetMaxConcurrencyForCurrentMode()
        {
            return RecordingSession.CurrentPerformanceMode switch
            {
                0 => 10,  // Realtime: ガンガン送る (デフォルト10)
                1 => 5,   // Balanced: 標準的
                2 => 2,   // LowLoad: 5分に1回なので並列不要、確実に送る
                3 => 2,   // UltraLowLoad: 同上
                _ => 3
            };
        }

        private DispatcherTimer? timer;
        private DispatcherTimer? blinkTimer;
        private DispatcherTimer? pauseTimer;
        private DispatcherTimer? flashRefreshTimer; // 2秒おきにフォアグラウンドかどうか確認するタイマー
        private int elapsedSeconds = 0;
        private int pausedSeconds = 0;
        private const int MAX_RECORDING_SECONDS = 300; // 5分 = 300秒
        private const int MAX_PAUSE_SECONDS = 3600; // 1時間 = 3600秒
        
        // ホットキーデバウンス用（コピーホットキーのみ、録音ホットキーは統一ガードタイムを使用）
        private DateTime _lastCopyHotkeyAt = DateTime.MinValue;
        private readonly System.Threading.SemaphoreSlim _recordToggleGate = new(1, 1);
        private readonly System.Threading.SemaphoreSlim _copyFunctionGate = new(1, 1);
        
        // Phase 1: CurrentSession から取得
        public bool IsRecording => CurrentSession?.IsRecording ?? false;
        
        public bool IsPaused => CurrentSession?.IsPaused ?? false;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 要約テキスト表示用プロパティ
        private string _summaryText = "録音を開始してください...";
        public string SummaryText
        {
            get => _summaryText;
            set
            {
                // 改行コードをWindows標準（\r\n）に統一（ダイナミクス等の古いアプリとの互換性確保）
                // 要約結果、録音中の文字起こし、すべてのテキスト表示で統一される
                string normalized = value?.Replace("\r\n", "\n")   // まずCRLFをLFに統一
                                          .Replace("\r", "\n")     // 古いMac形式も対応
                                          .Replace("\n", "\r\n")   // 最後にCRLF（Windows標準）に統一
                                          ?? "";
                
                if (_summaryText != normalized)
                {
                    _summaryText = normalized;
                    OnPropertyChanged();
                }
            }
        }

        // 録音情報表示用のプロパティ
        private string _currentDate = "-";
        private string _currentTime = "-";
        private string _currentPatientName = "-";

        public string CurrentDate
        {
            get => _currentDate;
            set
            {
                if (_currentDate != value)
                {
                    _currentDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set
            {
                if (_currentTime != value)
                {
                    _currentTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentPatientName
        {
            get => _currentPatientName;
            set
            {
                if (_currentPatientName != value)
                {
                    _currentPatientName = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                this.DataContext = this;
                
                // 【v28.4】アプリ起動時に古いログを削除
                DeleteOldLogFiles();

                // Phase 2: 最初のセッションを作成
                var initialSession = new RecordingSession();
                initialSession.Initialize(outputDirectory);
                Sessions.Add(initialSession);
                CurrentSession = initialSession;

                // Register for window loaded event to get the window handle
                this.Loaded += MainWindow_Loaded;
                this.Closing += MainWindow_Closing;

                // プロンプトファイルを読み込む
                LoadPromptFiles();
            }
            catch (Exception ex)
            {
                HandleUnhandledException("MainWindow Constructor", ex);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // appsettings.txtを読み込んで環境変数に設定
                string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.txt");
                if (File.Exists(appSettingsPath))
                {
                    Env.Load(appSettingsPath);
                    Debug.WriteLine($"appsettings.txtを読み込みました: {appSettingsPath}");
                }
                else
                {
                    Debug.WriteLine($"appsettings.txtが見つかりません: {appSettingsPath}");
                }

                // ライセンス認証チェック
                CheckLicense();
                
                // Get the window handle
                _windowHandle = new WindowInteropHelper(this).Handle;
                Debug.WriteLine($"ウィンドウハンドル取得: {_windowHandle}");
                _source = HwndSource.FromHwnd(_windowHandle);
                _source.AddHook(HwndHook);

                // HOTKEY_MODIFIER_KEYの設定を読み込む
                string hotkeyModifier = Environment.GetEnvironmentVariable("HOTKEY_MODIFIER_KEY") ?? "Alt";
                int copyModifierKey = MOD_CONTROL | MOD_SHIFT;
                if (hotkeyModifier.Equals("Win", StringComparison.OrdinalIgnoreCase))
                {
                    copyModifierKey |= MOD_WIN;
                    Debug.WriteLine("コピーホットキーの修飾キー: Win+Ctrl+Shift");
                }
                else
                {
                    copyModifierKey |= MOD_ALT;
                    Debug.WriteLine("コピーホットキーの修飾キー: Alt+Ctrl+Shift");
                }

                // Register the global hotkeys
                bool recordHotkeyRegistered = RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_COMMA);
                bool pauseHotkeyRegistered = RegisterHotKey(_windowHandle, PAUSE_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_PERIOD);
                bool copyHotkeyRegistered = RegisterHotKey(_windowHandle, COPY_HOTKEY_ID, copyModifierKey, VK_COMMA);

                if (!recordHotkeyRegistered)
                {
                    MessageBox.Show("グローバルホットキー (Ctrl+Shift+,) の登録に失敗しました。",
                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                if (!pauseHotkeyRegistered)
                {
                    MessageBox.Show("グローバルホットキー (Ctrl+Shift+.) の登録に失敗しました。",
                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                if (!copyHotkeyRegistered)
                {
                    MessageBox.Show("グローバルホットキー (Alt+Ctrl+Shift+,) の登録に失敗しました。",
                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 初期アイコンを設定
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch (Exception ex) {
                    Debug.WriteLine($"初期アイコン設定エラー: {ex.Message}");
                }

                // パフォーマンスモードの初期設定
                string perfMode = Environment.GetEnvironmentVariable("PERFORMANCE_MODE") ?? "Realtime";
                RecordingSession.CurrentPerformanceMode = perfMode switch
                {
                    "Realtime" => 0,
                    "Balanced" => 1,
                    "LowLoad" => 2,
                    "UltraLowLoad" => 3,
                    _ => 0
                };
                Debug.WriteLine($"パフォーマンスモード設定: {perfMode} ({RecordingSession.CurrentPerformanceMode})");

                // 選択中のプロンプトを復元
                LoadSelectedPrompt();

                // Phase 3: 最初のセッションを自動選択（起動時にアクティブにする）
                if (Sessions.Count > 0 && SessionListBox != null)
                {
                    SessionListBox.SelectedItem = CurrentSession ?? Sessions[0];
                    if (CurrentSession == null)
                    {
                        CurrentSession = Sessions[0];
                    }
                }
            }
            catch (Exception ex)
            {
                HandleUnhandledException("MainWindow_Loaded", ex);
            }
        }

        // 新しい例外処理メソッド
        private void HandleUnhandledException(string context, Exception ex)
        {
            string errorMessage = $"未処理の例外 ({context}): {ex.GetType().FullName} - {ex.Message}";
            Console.WriteLine($"❌ {errorMessage}");
            Console.WriteLine($"スタックトレース:\n{ex.StackTrace}");

            // ネストされた例外も表示
            var innerEx = ex.InnerException;
            while (innerEx != null)
            {
                 Console.WriteLine($"--- Inner Exception ---");
                 Console.WriteLine($"❌ {innerEx.GetType().FullName} - {innerEx.Message}");
                 Console.WriteLine($"スタックトレース:\n{innerEx.StackTrace}");
                 innerEx = innerEx.InnerException;
            }

            try
            {
                MessageBox.Show(
                    $"{errorMessage}\n\nスタックトレース:\n{ex.StackTrace}\n\nプログラムを終了します。",
                    "致命的なエラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // MessageBox が表示できない場合（UIスレッドの問題など）
                Console.WriteLine("!!! MessageBoxの表示に失敗しました !!!");
            }
            Environment.Exit(1);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Unregister the hotkey when the application is closing
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
                UnregisterHotKey(_windowHandle, PAUSE_HOTKEY_ID);
                UnregisterCopyHotkey(); // コピーホットキーの解除も追加
                EndOrangeGlow(); // Ensure flashing stops on close
            }
            
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Handle the WM_HOTKEY message
            if (msg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HOTKEY_ID)
                {
                    // グローバルホットキー (Ctrl+Shift+,) がトリガーされました（録音機能）
                    Debug.WriteLine("Global hotkey (Ctrl+Shift+,) triggered - recording function");
                    
                    // 【v28.4】統一ガードタイムによるチェック（チャタリング対策）
                    if (IsWithinGuardTime())
                    {
                        Debug.WriteLine("Guard time active - ignoring hotkey");
                        handled = true;
                        return IntPtr.Zero;
                    }
                    
                    // 再入防止（同時実行のガード）
                    this.Dispatcher.Invoke(async () =>
                    {
                        if (!await _recordToggleGate.WaitAsync(0))
                        {
                            Debug.WriteLine("Record toggle already in progress - skipping");
                            return;
                        }
                        try
                        {
                            RecordButton_Click(this, new RoutedEventArgs());
                        }
                        finally
                        {
                            _recordToggleGate.Release();
                        }
                    });
                    
                    handled = true;
                    return IntPtr.Zero;
                }
                else if (wParam.ToInt32() == PAUSE_HOTKEY_ID && IsRecording && !IsPaused)
                {
                    // Log that the pause hotkey was triggered
                    Debug.WriteLine("Global hotkey (Ctrl+Shift+.) triggered - pause function");
                    
                    // Invoke the PauseButton_Click method on the UI thread
                    this.Dispatcher.Invoke(() =>
                    {
                        // Trigger the pause button click
                        PauseButton_Click(this, new RoutedEventArgs());
                    });
                    
                    handled = true;
                    return IntPtr.Zero;
                }
                else if (wParam.ToInt32() == COPY_HOTKEY_ID) // コピーホットキーの処理
                {
                    // Log that the copy hotkey (Win+Ctrl+Shift+,) was triggered
                    Debug.WriteLine("Global hotkey (Win+Ctrl+Shift+,) triggered - copy function");
                    
                    // デバウンス処理（1000ms以内の連続発火を無視）
                    var now = DateTime.UtcNow;
                    if ((now - _lastCopyHotkeyAt).TotalMilliseconds < 1000)
                    {
                        Debug.WriteLine("Copy hotkey debounced - too soon");
                        handled = true;
                        return IntPtr.Zero;
                    }
                    _lastCopyHotkeyAt = now;
                    
                    // 再入防止（同時実行のガード）
                    this.Dispatcher.Invoke(async () =>
                    {
                        if (!await _copyFunctionGate.WaitAsync(0))
                        {
                            Debug.WriteLine("Copy function already in progress - skipping");
                            return;
                        }
                        try
                        {
                            ExecuteCopyFunction();
                        }
                        finally
                        {
                            _copyFunctionGate.Release();
                        }
                    });
                    
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            
            return IntPtr.Zero;
        }

        // コピー機能を実行するメソッド（重複コードを避けるため分離）
        private void ExecuteCopyFunction()
        {
            Debug.WriteLine("ExecuteCopyFunction called");
            
            // システムのアクセシビリティ機能との競合を回避するため、
            // 統一的な遅延を設定（キーボード・フットスイッチ共通）
            this.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(300); // 統一遅延
                
                // テキストをクリップボードにコピー
                if (!string.IsNullOrEmpty(SummaryTextBox.Text))
                {
                    try
                    {
                        // 改行コードをWindows標準（\r\n）に統一（ダイナミクス等の古いアプリとの互換性確保）
                        string textToClipboard = SummaryTextBox.Text
                            .Replace("\r\n", "\n")   // まずCRLFをLFに統一
                            .Replace("\r", "\n")     // 古いMac形式も対応
                            .Replace("\n", "\r\n");  // 最後にCRLF（Windows標準）に統一
                        Clipboard.SetText(textToClipboard);
                        
                        // 成功メッセージを表示
                        StatusText.Text = "✅ テキストをコピーしました";
                        StatusText.Foreground = Brushes.Green;
                        
                        // 録音中/一時停止中は赤/黄を維持。待機時のみ黒に戻す
                        if (!IsRecording && !IsPaused)
                        {
                            try {
                                string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                                using (DrawingIcon icon = new DrawingIcon(iconPath))
                                {
                                    this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                        icon.Handle,
                                        System.Windows.Int32Rect.Empty,
                                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                }
                            } catch (Exception ex) {
                                Debug.WriteLine($"アイコン設定エラー: {ex.Message}");
                            }
                        }
                        
                        // 最適な自動貼り付けを実行
                        await PerformOptimizedAutoPaste();
                        
                        // 1秒後に準備完了に戻す
                        var copyStatusTimer = new DispatcherTimer();
                        copyStatusTimer.Interval = TimeSpan.FromSeconds(1);
                        copyStatusTimer.Tick += (s, args) =>
                        {
                            copyStatusTimer.Stop();
                            StatusText.Text = "⭕ 準備完了";
                            StatusText.Foreground = Brushes.Gray;
                        };
                        copyStatusTimer.Start();
                    }
                    catch (Exception ex)
                    {
                        StatusText.Text = "⚠ コピーに失敗しました";
                        StatusText.Foreground = Brushes.Red;
                        Debug.WriteLine($"コピーエラー: {ex.Message}");
                        
                        // 2秒後に準備完了に戻す
                        var errorStatusTimer = new DispatcherTimer();
                        errorStatusTimer.Interval = TimeSpan.FromSeconds(2);
                        errorStatusTimer.Tick += (s, args) =>
                        {
                            errorStatusTimer.Stop();
                            StatusText.Text = "⭕ 準備完了";
                            StatusText.Foreground = Brushes.Gray;
                        };
                        errorStatusTimer.Start();
                    }
                }
                else
                {
                    StatusText.Text = "⚠ コピーするテキストがありません";
                    StatusText.Foreground = Brushes.Orange;
                    
                    // 2秒後に準備完了に戻す
                    var warningStatusTimer = new DispatcherTimer();
                    warningStatusTimer.Interval = TimeSpan.FromSeconds(2);
                    warningStatusTimer.Tick += (s, args) =>
                    {
                        warningStatusTimer.Stop();
                        StatusText.Text = "⭕ 準備完了";
                        StatusText.Foreground = Brushes.Gray;
                    };
                    warningStatusTimer.Start();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // 最適化された自動貼り付け機能
        private async Task PerformOptimizedAutoPaste()
        {
            try
            {
                Debug.WriteLine("PerformOptimizedAutoPaste開始");
                
                // 現在のマウスカーソル位置を取得
                POINT currentPoint;
                if (!GetCursorPos(out currentPoint))
                {
                    Debug.WriteLine("マウスカーソル位置の取得に失敗");
                    return;
                }

                Debug.WriteLine($"マウスカーソル位置: ({currentPoint.X}, {currentPoint.Y})");

                // カーソル位置でマウスクリックを実行（手動クリックと同じ挙動）
                await PerformMouseClickAtCurrentPosition(currentPoint);
                
                // 1秒待機
                Debug.WriteLine("1秒待機開始");
                await Task.Delay(1000);
                
                // テキストをペースト
                Debug.WriteLine("テキストペースト開始");
                bool pasteSuccess = await PerformPaste();
                
                if (pasteSuccess)
                {
                    Debug.WriteLine("自動貼り付け成功");
                }
                else
                {
                    Debug.WriteLine("自動貼り付け失敗");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PerformOptimizedAutoPaste中にエラー: {ex.Message}");
            }
        }
        
        // マウスクリックを実行
        private async Task PerformMouseClickAtCurrentPosition(POINT screenPoint)
        {
            try
            {
                Debug.WriteLine($"マウスクリック実行: ({screenPoint.X}, {screenPoint.Y})");
                
                // mouse_eventを使用してマウスクリックを実行（手動クリックと同じ方法）
                mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)screenPoint.X, (uint)screenPoint.Y, 0, UIntPtr.Zero);
                await Task.Delay(50); // クリック間の短い遅延
                mouse_event(MOUSEEVENTF_LEFTUP, (uint)screenPoint.X, (uint)screenPoint.Y, 0, UIntPtr.Zero);
                
                Debug.WriteLine("マウスクリック完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"マウスクリック実行中にエラー: {ex.Message}");
            }
        }
        
        // テキストペーストを実行（統一Ctrl+V方式 - Alt/Winキー状態無視）
        private async Task<bool> PerformPaste()
        {
            try
            {
                Debug.WriteLine("統一Ctrl+Vペースト実行開始");
                
                // SendInputを使用してより確実なキー送信を行う
                await SendCtrlVInput();
                
                Debug.WriteLine("統一Ctrl+Vペースト実行完了");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ペースト実行中にエラー: {ex.Message}");
                return false;
            }
        }

        // SendInputを使用したCtrl+V送信
        private async Task SendCtrlVInput()
        {
            Debug.WriteLine("Ctrl+V送信開始");
            
            try
            {
                // Ctrl+Vを送信
                Debug.WriteLine("Ctrl+V送信");
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);     // Ctrl DOWN
                await Task.Delay(10);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);           // V DOWN
                await Task.Delay(10);
                keybd_event(VK_V, 0, 0x0002, UIntPtr.Zero);      // V UP
                await Task.Delay(10);
                keybd_event(VK_CONTROL, 0, 0x0002, UIntPtr.Zero); // Ctrl UP
                
                await Task.Delay(50); // 処理完了を待機
                
                Debug.WriteLine("Ctrl+V送信完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ctrl+V送信エラー: {ex.Message}");
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            // 【v28.4】統一ガードタイムによるチェック（誤操作・連打・チャタリング防止）
            if (IsWithinGuardTime())
            {
                Debug.WriteLine($"⏳ ガードタイム内のため無視しました（残り{GUARD_TIME_SECONDS - (DateTime.UtcNow - _lastRecordStateChangeAt).TotalSeconds:F1}秒）");
                return;
            }

            if (IsPaused)
            {
                // 一時停止中の場合、録音を再開
                if (CurrentSession != null)
                {
                    CurrentSession.ResumeRecording();
                    _lastRecordStateChangeAt = DateTime.UtcNow; // 【v28.4】ガードタイム更新
                    UpdateUIForCurrentSession();
                }
                return;
            }

            // 【v28.4修正】停止処理中の新規録音をガードタイム付きで制限
            // チャタリングによる誤動作を防ぎつつ、連続診察のスムーズな開始機能を維持する
            if (_isStopping)
            {
                // ガードタイム内は完全にブロック（チャタリング対策）
                // ※上記のIsWithinGuardTime()でブロックされるため、ここに到達した場合はガードタイム経過後
                Debug.WriteLine("⚡ 停止処理中ですが、ガードタイム経過後のため新規録音を開始します");
                StartNewRecording();
                _lastRecordStateChangeAt = DateTime.UtcNow; // 【v28.4】ガードタイム更新
                return;
            }

            if (CurrentSession == null || !CurrentSession.IsRecording)
            {
                // 新規録音開始
                StartNewRecording();
                _lastRecordStateChangeAt = DateTime.UtcNow; // 【v28.4】ガードタイム更新
            }
            else
            {
                // 録音停止
                _lastRecordStateChangeAt = DateTime.UtcNow; // 【v28.4】ガードタイム更新（停止処理開始時）
                
                // UI上で即座にSTARTボタンに変更（処理完了を待たない）
                RecordButton.Content = "START";
                RecordButton.Style = (Style)FindResource("RecordButtonStyle");
                
                // 【修正】停止処理を待たずにバックグラウンドで実行（Fire-and-Forget）
                // RecordingSession.StopRecordingAsync内で即座にデバイス停止とIsRecording=falseが行われるため、
                // 次のStartNewRecordingはすぐに受け付け可能になる。
                _ = StopRecordingAsync();
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSession != null && CurrentSession.IsRecording && !CurrentSession.IsPaused)
            {
                CurrentSession.PauseRecording();
                UpdateUIForCurrentSession();
            }
        }

        // Phase 1: RecordingSession のイベントハンドラ（対策1, 2, 3）
        // 注意: async void はイベントハンドラで使用されるため、警告は無視
        private void CurrentSession_ChunkReady(object? sender, byte[] audioData)
        {
            // 対策2: 厳密なnullチェック
            if (sender is not RecordingSession session || session == null || audioData == null || audioData.Length == 0)
            {
                Debug.WriteLine("ChunkReady: 無効なパラメータ");
                return;
            }

            // 対策2: セッションが有効かチェック（録音中または一時停止中でない場合は処理しない）
            if (!session.IsRecording && !session.IsPaused)
            {
                Debug.WriteLine("ChunkReady: セッションが録音状態ではない");
                return;
            }

            // 対策1, 3: チャンク処理をタスクとして登録
            Task chunkTask = ProcessChunkAsync(session, audioData);
            session.RegisterChunkTask(chunkTask);
        }

        // チャンク処理を非同期で実行（対策1, 2）
        private async Task ProcessChunkAsync(RecordingSession session, byte[] audioData)
        {
            // 【追加】セマフォによる流量制限
            // モードに応じて並列数を制御したいところですが、動的にSemaphoreSlimを変更するのは複雑なため、
            // ここでは安全に固定値(10)で全体を制限しつつ、詰まりを防止します。
            // 必要であれば GetMaxConcurrencyForCurrentMode() を使って制御ロジックを組むことも可能です。
            await _uploadSemaphore.WaitAsync();

            try
            {
                // 対策2: 処理開始時にもセッションの有効性を再確認
                // 【修正】停止後も最後のチャンク処理が必要なため、IsRecordingチェックは削除
                if (session == null)
                {
                    Debug.WriteLine("ProcessChunkAsync: セッションが無効");
                    return;
                }

                string chunkText = await SpeechToText.StartFastTranscriptionWithRetry(audioData, $"chunk_{DateTime.Now.Ticks}.wav");
                
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    // デッドロック回避: InvokeAsyncを使用（同期待ちをしない）
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        // 対策2: 処理完了時にもセッションの有効性を再確認
                        if (session == null)
                        {
                            Debug.WriteLine("ProcessChunkAsync: 処理完了時にセッションがnull");
                            return;
                        }

                        // セッションがまだ録音中または一時停止中かチェック
                        // 【修正】録音停止後（IsStopped=true）でも処理を継続するため、異常状態のみログ出力
                        if (!session.IsRecording && !session.IsPaused && !session.IsStopped)
                        {
                            Debug.WriteLine("ProcessChunkAsync: 処理完了時にセッションが予期せぬ状態です");
                            // ただし、文字起こし結果は保存する
                        }

                        try
                        {
                            // RecordingSession にテキストを追加（ファイル追記も行われる）
                            session.AppendTranscript(chunkText);
                            
                            // アクティブなセッションならUI更新
                            if (session == CurrentSession && CurrentSession != null)
                            {
                                // UltraLowLoad(3)の場合は録音中のUI更新をスキップ
                                if (RecordingSession.CurrentPerformanceMode != 3)
                                {
                                    SummaryText = "🎤 録音中...\n\n" + session.AccumulatedTranscript;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"AppendTranscriptエラー: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"チャンク文字起こしエラー: {ex.Message}");
                
                // 【v28.4 修正4】ユーザー向け：エラーを文字起こしテキストファイルに記録
                if (session != null && !string.IsNullOrEmpty(session.CurrentTextFilePath))
                {
                    try
                    {
                        string errorLog = $"[エラー: 文字起こし失敗 - {ex.Message}]";
                        session.AppendTranscript(errorLog);
                    }
                    catch { /* ファイル書き込みエラーは無視 */ }
                }
                
                // 【v28.4 修正4】ユーザー向け：UIにもエラー状況を反映
                _ = this.Dispatcher.BeginInvoke(() =>
                {
                    if (CurrentSession == session)
                    {
                        StatusText.Text = "⚠️ 一部の文字起こしに失敗";
                    }
                });
                
                // 【v28.4 修正8】開発者向け：詳細エラーログ（ログファイルに記録）
                LogToFile($"[チャンク処理エラー] SessionId: {session?.SessionId ?? "unknown"}\n" +
                          $"例外: {ex.GetType().Name}\n" +
                          $"メッセージ: {ex.Message}\n" +
                          $"スタックトレース:\n{ex.StackTrace}");
            }
            finally
            {
                _uploadSemaphore.Release();
            }
        }

        private void CurrentSession_SilenceDetected(object? sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (sender == CurrentSession && IsRecording && !IsPaused)
                {
                    StatusText.Text = "🔇 3分間無音を検出 - 自動停止します";
                    RecordButton_Click(this, new RoutedEventArgs());
                }
            });
        }

        private void CurrentSession_TranscriptUpdated(object? sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (sender == CurrentSession && CurrentSession != null)
                {
                    // UltraLowLoad(3)の場合は録音中のUI更新をスキップ
                    if (RecordingSession.CurrentPerformanceMode != 3)
                    {
                        SummaryText = "🎤 録音中...\n\n" + CurrentSession.AccumulatedTranscript;
                    }
                }
            });
        }

        // Phase 1: CurrentSession のプロパティをUIに反映
        private void UpdateUIForCurrentSession()
        {
            if (CurrentSession == null) return;

            // タイマーとステータス表示の制御
            if (CurrentSession.IsRecording && !CurrentSession.IsPaused)
            {
                // 録音中
                StopPauseTimer();
                StartTimer();
                
                SummaryText = "🎤 録音中...\n\n" + CurrentSession.AccumulatedTranscript;
                StatusText.Text = "Listening...";
                StatusText.Foreground = Brushes.Gray;
                
                // アイコンを赤点滅（または赤）に
                BeginOrangeGlow();
            }
            else if (CurrentSession.IsPaused)
            {
                // 一時停止中
                StopTimer();
                StartPauseTimer();
                
                SummaryText = "⏸ 一時停止中...\n\n" + CurrentSession.AccumulatedTranscript;
                StatusText.Text = "⏸ 一時停止中";
                StatusText.Foreground = Brushes.Orange;
                
                // アイコンを黄色に
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_yellow.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch { }
            }
            else if (CurrentSession.IsStopped && !string.IsNullOrEmpty(CurrentSession.SummaryFilePath) && File.Exists(CurrentSession.SummaryFilePath))
            {
                // 処理完了
                StopTimer();
                StopPauseTimer();
                
                // 録音停止後は要約結果ファイルを読み込んで表示
                try
                {
                    string summaryContent = File.ReadAllText(CurrentSession.SummaryFilePath);
                    // 処理時間の行を除去（表示用）
                    string[] lines = summaryContent.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
                    var displayLines = lines.TakeWhile(line => !line.Contains("--- 処理時間 ---")).ToList();
                    SummaryText = string.Join(Environment.NewLine, displayLines);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"要約ファイル読み込みエラー: {ex.Message}");
                    SummaryText = CurrentSession.AccumulatedTranscript; // フォールバック
                }
                
                StatusText.Text = "✅ 処理完了";
                StatusText.Foreground = Brushes.Green;
                
                // アイコンを緑に
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_green.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch { }
            }
            else if (CurrentSession.IsStopped)
            {
                // 録音停止後、要約処理中
                StopTimer();
                StopPauseTimer();
                
                SummaryText = CurrentSession.AccumulatedTranscript;
                StatusText.Text = "🤖 要約中...";
                StatusText.Foreground = Brushes.Blue;
                
                // アイコンを黒に
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch { }
            }
            else
            {
                // 待機中または初期状態
                StopTimer();
                StopPauseTimer();
                
                SummaryText = CurrentSession.AccumulatedTranscript;
                StatusText.Text = "⭕ 準備完了";
                StatusText.Foreground = Brushes.Blue;
                
                // アイコンを黒に
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch { }
            }

            // 患者名の更新は行わない（要約結果の患者名はタブの名前を反映しない）
            // 要約結果に新しい患者名が表示されたときは、UpdatePatientNameメソッドで
            // CurrentSession.PatientNameが更新される（タブのテキストに反映される）

            // ボタン表示を更新
            UpdateButtonAppearance();
        }

        // Phase 2: タブ切り替え時の自動一時停止
        private void SessionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 1. 変更前のセッションを一時停止
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is RecordingSession oldSession)
            {
                if (oldSession.IsRecording && !oldSession.IsPaused)
                {
                    oldSession.PauseRecording();
                    Debug.WriteLine($"タブ切り替え: セッション '{oldSession.PatientName}' を自動一時停止しました");
                }
            }

            // 2. 新しいセッションに切り替え
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is RecordingSession newSession)
            {
                CurrentSession = newSession;
                // ListBox の選択状態を CurrentSession に同期
                if (SessionListBox.SelectedItem != CurrentSession)
                {
                    SessionListBox.SelectedItem = CurrentSession;
                }
                Debug.WriteLine($"タブ切り替え: セッション '{newSession.PatientName}' に切り替えました");
                
                // UIを更新（StatusTextも含む）
                UpdateUIForCurrentSession();
            }
        }

        // Phase 2: 新規患者セッションを追加
        private void AddNewSession()
        {
            var newSession = new RecordingSession();
            newSession.Initialize(outputDirectory);
            Sessions.Add(newSession);
            CurrentSession = newSession;
            // ListBox の選択状態を CurrentSession に同期
            SessionListBox.SelectedItem = CurrentSession;
            Debug.WriteLine($"新規セッション追加: '{newSession.PatientName}' (SessionId: {newSession.SessionId})");
        }

        // Phase 2: 新規患者ボタンのクリックハンドラ
        private void AddNewSessionButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewSession();
        }

        // 患者セッションパネル折りたたみ/展開のクリックハンドラ
        private bool _isSessionPanelExpanded = true;
        private GridLength _savedSessionPanelWidth = new GridLength(200);
        private GridLength _savedSessionSpacerWidth = new GridLength(20);

        private void SessionPanelToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isSessionPanelExpanded = !_isSessionPanelExpanded;

            if (_isSessionPanelExpanded)
            {
                // 展開：保存した幅に戻す
                SessionPanelColumn.Width = _savedSessionPanelWidth;
                SessionSpacerColumn.Width = _savedSessionSpacerWidth;
            }
            else
            {
                // 折りたたみ：幅を0にする
                SessionPanelColumn.Width = new GridLength(0);
                SessionSpacerColumn.Width = new GridLength(0);
            }

            // ボタンアイコンを更新（›=展開中→折りたたみ可能、‹=折りたたみ中→展開可能）
            if (SessionPanelToggleButton.Template.FindName("ToggleIcon", SessionPanelToggleButton) is TextBlock toggleIcon)
            {
                toggleIcon.Text = _isSessionPanelExpanded ? "›" : "‹";
            }

            // ツールチップを更新
            SessionPanelToggleButton.ToolTip = _isSessionPanelExpanded 
                ? "患者セッションパネルを折りたたむ" 
                : "患者セッションパネルを展開する";
        }

        // セッション削除ボタンのクリックハンドラ
        private async void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is RecordingSession sessionToDelete)
                {
                    // 削除前に確認（オプション：必要に応じてコメントアウト）
                    // 現在は確認なしで削除

                    // 削除対象が現在のセッションの場合、別のセッションに切り替え
                    if (sessionToDelete == CurrentSession)
                    {
                        // 録音中の場合は停止
                        if (sessionToDelete.IsRecording)
                        {
                            await sessionToDelete.StopRecordingAsync();
                        }

                        // 他のセッションがあれば最初のセッションに切り替え
                        if (Sessions.Count > 1)
                        {
                            var remainingSessions = Sessions.Where(s => s != sessionToDelete).ToList();
                            if (remainingSessions.Count > 0)
                            {
                                CurrentSession = remainingSessions[0];
                                SessionListBox.SelectedItem = CurrentSession;
                            }
                            else
                            {
                                CurrentSession = null;
                            }
                        }
                        else
                        {
                            CurrentSession = null;
                        }
                    }
                    else
                    {
                        // 削除対象が現在のセッションでない場合、録音中なら停止
                        if (sessionToDelete.IsRecording)
                        {
                            await sessionToDelete.StopRecordingAsync();
                        }
                    }

                    // セッションを削除
                    Sessions.Remove(sessionToDelete);
                    sessionToDelete.Dispose(); // リソースを解放

                    // セッションが0になった場合は新しいセッションを作成
                    if (Sessions.Count == 0)
                    {
                        var newSession = new RecordingSession();
                        newSession.Initialize(outputDirectory);
                        Sessions.Add(newSession);
                        CurrentSession = newSession;
                        SessionListBox.SelectedItem = CurrentSession;
                    }

                    Debug.WriteLine($"セッション削除: '{sessionToDelete.PatientName}' (SessionId: {sessionToDelete.SessionId})");
                }
            }
            catch (Exception ex)
            {
                HandleUnhandledException("DeleteSessionButton_Click", ex);
            }
        }

        // シングルクリックでタブをアクティブにする（最下層の処理）
        private void SessionListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ×ボタンのクリックは除外（削除処理に任せる）
            if (e.OriginalSource is Button)
            {
                return;
            }

            // TextBoxのクリックも除外（TextBox自身の処理に任せる）
            if (e.OriginalSource is TextBox || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            // クリックされたListBoxItemを取得
            var source = e.OriginalSource as DependencyObject;
            if (source == null)
            {
                return;
            }

            var item = FindAncestor<ListBoxItem>(source);
            if (item != null && item.DataContext is RecordingSession session)
            {
                // タブをアクティブにする（患者名編集は行わない）
                SessionListBox.SelectedItem = session;
                CurrentSession = session;
            }
        }

        // 患者名TextBoxのクリック時（最上層の処理）
        private void PatientNameTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // イベントを処理済みにする（親のListBoxItemのクリック処理を防ぐ）
            e.Handled = true;

            if (sender is TextBox textBox && textBox.DataContext is RecordingSession session)
            {
                // タブをアクティブにする
                SessionListBox.SelectedItem = session;
                CurrentSession = session;

                // TextBoxにフォーカスを当てる
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }), DispatcherPriority.Loaded);
            }
        }

        // 削除ボタンのクリック時（中層の処理、優先度が高い）
        private void DeleteButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // イベントを処理済みにする（親のListBoxItemのクリック処理を防ぐ）
            e.Handled = true;
            
            // 削除処理を直接呼び出す
            if (sender is Button button)
            {
                DeleteSessionButton_Click(button, e);
            }
        }

        // =====================================
        // v30.0: 事前情報入力機能
        // =====================================
        
        // 事前情報入力対象のセッション
        private RecordingSession? _preInfoTargetSession;
        
        // 最後に要約を表示したセッション（再生成用）
        private RecordingSession? _lastSummarizedSession;
        
        // メモ帳ボタンのPreviewMouseLeftButtonDown（クリック伝播を防ぐ）
        private void PreInfoButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // イベントを処理済みにする（親のListBoxItemのクリック処理を防ぐ）
            e.Handled = true;
            
            // クリック処理を直接呼び出す
            if (sender is Button button)
            {
                PreInfoButton_Click(button, e);
            }
        }
        
        // メモ帳ボタンクリック → 事前情報入力パネルを表示
        private void PreInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is RecordingSession session)
                {
                    _preInfoTargetSession = session;
                    
                    // 既存の事前情報があれば読み込み
                    PreInfoTitleTextBox.Text = session.PatientName != "(未設定)" ? session.PatientName : "";
                    PreInfoContentTextBox.Text = session.PreInfoText ?? "";
                    
                    // パネルを表示
                    PreInfoPanel.Visibility = Visibility.Visible;
                    
                    // 患者名入力欄にフォーカス
                    PreInfoTitleTextBox.Focus();
                    PreInfoTitleTextBox.SelectAll();
                    
                    Debug.WriteLine($"📝 事前情報入力パネルを表示: SessionId={session.SessionId}");
                }
            }
            catch (Exception ex)
            {
                HandleUnhandledException("PreInfoButton_Click", ex);
            }
        }
        
        // キャンセルボタン → パネルを閉じる
        private void PreInfoCancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PreInfoPanel.Visibility = Visibility.Collapsed;
                _preInfoTargetSession = null;
                
                // 入力内容をクリア
                PreInfoTitleTextBox.Text = "";
                PreInfoContentTextBox.Text = "";
                
                Debug.WriteLine("📝 事前情報入力をキャンセルしました");
            }
            catch (Exception ex)
            {
                HandleUnhandledException("PreInfoCancelButton_Click", ex);
            }
        }
        
        // 保存ボタン → 事前情報を保存してパネルを閉じる
        private void PreInfoSaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_preInfoTargetSession == null)
                {
                    MessageBox.Show("保存対象のセッションが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                string title = PreInfoTitleTextBox.Text.Trim();
                string content = PreInfoContentTextBox.Text.Trim();
                
                // 患者名は必須
                if (string.IsNullOrEmpty(title))
                {
                    MessageBox.Show("患者名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    PreInfoTitleTextBox.Focus();
                    return;
                }
                
                // セッションが既に録音停止済み（IsStopped）で、かつ録音ファイルがある場合は
                // 新しいセッションIDが必要かどうかを確認
                if (_preInfoTargetSession.IsStopped && _preInfoTargetSession.SessionRecordingFiles.Count > 0)
                {
                    // 既に録音が完了しているセッションに事前情報を追加しようとしている
                    // この場合は新しいセッションを作成する
                    var result = MessageBox.Show(
                        "このセッションは既に録音が完了しています。\n新しいセッションとして事前情報を登録しますか？",
                        "確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                    
                    // 新しいセッションを作成
                    var newSession = new RecordingSession();
                    newSession.Initialize(_preInfoTargetSession.OutputDirectory);
                    Sessions.Add(newSession);
                    
                    // ターゲットを新しいセッションに変更
                    _preInfoTargetSession = newSession;
                    CurrentSession = newSession;
                    SessionListBox.SelectedItem = newSession;
                }
                
                // 事前情報を保存（患者名も同時に設定される）
                _preInfoTargetSession.SavePreInfo(title, content);
                
                // パネルを閉じる
                PreInfoPanel.Visibility = Visibility.Collapsed;
                
                // ステータス更新
                StatusText.Text = $"✅ 事前情報を保存しました（{title}）";
                
                Debug.WriteLine($"📝 事前情報を保存: SessionId={_preInfoTargetSession.SessionId}, PatientName={title}");
                
                // 入力内容をクリア
                PreInfoTitleTextBox.Text = "";
                PreInfoContentTextBox.Text = "";
                _preInfoTargetSession = null;
            }
            catch (Exception ex)
            {
                HandleUnhandledException("PreInfoSaveButton_Click", ex);
            }
        }

        // =====================================
        // v30.0: 要約再生成機能
        // =====================================
        
        // 再生成メニュークリック
        private async void RegenerateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not MenuItem menuItem) return;
                string selectedPromptPath = (string)menuItem.Tag;
                string promptName = Path.GetFileNameWithoutExtension(selectedPromptPath);
                
                // 再生成対象のセッションを確認
                if (_lastSummarizedSession == null)
                {
                    MessageBox.Show(
                        "再生成対象の要約がありません。\n先に録音→要約を行ってください。",
                        "再生成エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                
                // 確認ポップアップ
                var result = MessageBox.Show(
                    $"「{promptName}」プロンプトを使って要約を再生成しますか？\n\n" +
                    $"対象: {_lastSummarizedSession.PatientName}\n" +
                    $"セッションID: {_lastSummarizedSession.SessionId}",
                    "要約の再生成",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes) return;
                
                // 再生成処理
                await RegenerateSummaryAsync(_lastSummarizedSession, selectedPromptPath);
            }
            catch (Exception ex)
            {
                HandleUnhandledException("RegenerateMenuItem_Click", ex);
            }
        }
        
        // 再生成処理
        private async Task RegenerateSummaryAsync(RecordingSession session, string systemPromptPath)
        {
            try
            {
                StatusText.Text = "🔄 再生成中...";
                SummaryText = "🔄 要約を再生成中...\n\nしばらくお待ちください。";
                
                // 結合テキストを取得（事前情報＋文字起こし）
                string combinedText = session.GetCombinedTextForSummary();
                
                if (string.IsNullOrWhiteSpace(combinedText))
                {
                    StatusText.Text = "⚠️ 再生成失敗";
                    SummaryText = "❌ 再生成に必要なテキストデータがありません。";
                    return;
                }
                
                Debug.WriteLine($"🔄 再生成開始: SessionId={session.SessionId}, Prompt={Path.GetFileName(systemPromptPath)}, Text={combinedText.Length}文字");
                
                // 時間計測
                var sw = System.Diagnostics.Stopwatch.StartNew();
                
                // 指定されたプロンプトで要約を再生成
                var (rawSummaryContent, _, _, _) = 
                    await SummarizeText.SummarizeFromCombinedTextAsync(combinedText, systemPromptPath);
                
                sw.Stop();
                
                // 結果を表示
                var (fact, assessment, todo) = ExtractSummaryContent(rawSummaryContent);
                string displayText = FormatSummaryForDisplay(fact, assessment, todo);
                
                SummaryText = displayText;
                StatusText.Text = $"✅ 再生成完了 ({sw.ElapsedMilliseconds}ms)";
                
                // 再生成した要約をファイルに保存
                string summaryPath = session.SummaryFilePath;
                if (!string.IsNullOrEmpty(summaryPath))
                {
                    try
                    {
                        string fileContent = displayText + Environment.NewLine + Environment.NewLine + 
                            "--- 再生成情報 ---" + Environment.NewLine +
                            $"使用プロンプト: {Path.GetFileNameWithoutExtension(systemPromptPath)}" + Environment.NewLine +
                            $"再生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                            $"処理時間: {sw.ElapsedMilliseconds} ms";
                        await File.WriteAllTextAsync(summaryPath, fileContent);
                        Debug.WriteLine($"💾 再生成結果を保存: {summaryPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"再生成結果の保存エラー: {ex.Message}");
                    }
                }
                
                // 患者名の更新
                UpdatePatientName(rawSummaryContent);
                
                LogToFile($"[再生成完了] SessionId: {session.SessionId}, Prompt: {Path.GetFileName(systemPromptPath)}, Time: {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                StatusText.Text = "⚠️ 再生成エラー";
                SummaryText = $"❌ 再生成中にエラーが発生しました:\n{ex.Message}";
                LogToFile($"[再生成エラー] SessionId: {session.SessionId}\n{ex.Message}\n{ex.StackTrace}");
            }
        }

        // 患者名TextBoxにフォーカスが当たった時
        private void PatientNameTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // テキストを全選択
                textBox.SelectAll();
            }
        }

        // 患者名TextBoxからフォーカスが外れた時
        private void PatientNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is RecordingSession session)
            {
                // 空の場合は"(未設定)"に戻す
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    session.PatientName = "(未設定)";
                }
            }
        }

        // ヘルパーメソッド：指定された型の親要素を探す
        private T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ヘルパーメソッド：指定された型の子要素を探す
        private T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                {
                    return (T)child;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        // ListBox上でのマウスホイールイベントを親のScrollViewerに転送
        private void SessionListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ListBox && !e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
                eventArg.RoutedEvent = UIElement.MouseWheelEvent;
                eventArg.Source = sender;
                var parent = ((Control)sender).Parent as UIElement;
                parent?.RaiseEvent(eventArg);
            }
        }

        private async void StartNewRecording()
        {
            try
            {
                StopBlinking();

                // Phase 2: CurrentSession が null の場合は最初のセッションを使用
                if (CurrentSession == null)
                {
                    if (Sessions.Count == 0)
                    {
                        var newSession = new RecordingSession();
                        newSession.Initialize(outputDirectory);
                        Sessions.Add(newSession);
                    }
                    CurrentSession = Sessions[0];
                }

                // Phase 2: 前回が録音停止（一時停止ではない）の場合は新しいセッションインスタンスを作成する
                // (バックグラウンド処理中の旧セッションとの競合を避けるため)
                Debug.WriteLine($"🔍 StartNewRecording: 現在のセッション状態 (IsStopped: {CurrentSession.IsStopped}, IsPaused: {CurrentSession.IsPaused}, IsRecording: {CurrentSession.IsRecording})");
                
                if (CurrentSession.IsStopped && !CurrentSession.IsPaused)
                {
                    // 新しいセッションを作成
                    var newSession = new RecordingSession();
                    newSession.Initialize(outputDirectory);

                    // 既存のセッションリスト内の位置を特定して置換
                    int index = Sessions.IndexOf(CurrentSession);
                    if (index >= 0)
                    {
                        Sessions[index] = newSession;
                    }
                    else
                    {
                        Sessions.Add(newSession);
                    }

                    CurrentSession = newSession;
                    SessionListBox.SelectedItem = CurrentSession;

                    Debug.WriteLine($"✅ 新規録音開始: 新しいセッションを作成しました (SessionId: {CurrentSession.SessionId})");
                }
                else
                {
                    Debug.WriteLine($"ℹ️ StartNewRecording: 既存のセッションを使用します (SessionId: {CurrentSession.SessionId})");
                }

                // 録音開始時に日時情報を設定
                UpdateRecordingInfo();

                // 【Phase 1 修正】UIインジケータ追加（MCI保存待ち中のUX改善）
                StatusText.Text = "🎤 録音準備中...";
                StatusText.Foreground = Brushes.Orange;

                // 録音開始（非同期版を使用）
                Debug.WriteLine($"🎤 StartNewRecording: StartRecordingAsync() を呼び出します");
                try
                {
                    await CurrentSession.StartRecordingAsync();
                    Debug.WriteLine($"✅ StartNewRecording: StartRecordingAsync() が完了しました");
                }
                catch (Exception ex)
                {
                    // 録音開始エラー時はUIを更新してエラーを表示
                    StatusText.Text = "⚠️ 録音開始エラー";
                    StatusText.Foreground = Brushes.Red;
                    Debug.WriteLine($"❌ StartNewRecording: 録音開始失敗: {ex.Message}");
                    throw; // エラーを上位に伝播
                }
                
                // Phase 1: CurrentRecorder を設定（RecordingSession からは設定不可のため）
                CurrentRecorder = CurrentSession.Recorder;

                // 録音ファイルが実際に作成されるまで待機（最大1秒）
                if (CurrentSession.SessionRecordingFiles.Count > 0)
                {
                    string firstFile = CurrentSession.SessionRecordingFiles[0];
                    Debug.WriteLine($"録音ファイル作成待機開始: {firstFile}");
                    int retryCount = 0;
                    bool fileCreated = false;
                    while (retryCount < 10 && !fileCreated)
                    {
                        await Task.Delay(100);
                        if (File.Exists(firstFile))
                        {
                            var fileInfo = new FileInfo(firstFile);
                            if (fileInfo.Length > 0)
                            {
                                fileCreated = true;
                                Debug.WriteLine($"録音ファイル作成確認完了: {fileInfo.Length} bytes");
                            }
                        }
                        retryCount++;
                    }

                    if (!fileCreated)
                    {
                        Debug.WriteLine("警告: 録音ファイルの作成確認に失敗しましたが、録音を継続します");
                    }
                }

                elapsedSeconds = 0;
                StartTimer(); // ファイル作成確認後にタイマー開始
                // 録音開始直後にバックグラウンドで各サービスをウォームアップ（DNS/TLS確立）
                _ = Task.Run(WarmUpServicesAsync);

                StatusText.Text = "Listening...";
                StatusText.Foreground = Brushes.Gray;
                
                // 録音開始時のテキスト更新
                SummaryText = "🎤 録音中...";
                
                // 録音中は赤いアイコンに変更（複数回試行）
                bool iconSetSuccessfully = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try {
                        string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_red.ico");
                        using (DrawingIcon icon = new DrawingIcon(iconPath))
                        {
                            this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        }
                        iconSetSuccessfully = true;
                        Debug.WriteLine($"赤いアイコンの設定に成功しました（試行回数: {attempt + 1}）");
                        break;
                    } catch (Exception ex) {
                        Debug.WriteLine($"アイコン設定エラー（試行{attempt + 1}/3）: {ex.Message}");
                        if (attempt < 2) // 最後の試行でない場合は少し待機
                        {
                            Task.Delay(50).Wait();
                        }
                    }
                }

                if (!iconSetSuccessfully)
                {
                    Debug.WriteLine("警告: 赤いアイコンの設定に失敗しましたが、録音中であることを示すため点滅を開始します");
                }

                // アイコン設定の成功/失敗に関わらず、録音中は必ず点滅を開始
                BeginOrangeGlow();
                Debug.WriteLine("タスクバーアイコンの疑似常時点灯を開始しました");
                
                // 【v28.4 修正10】セッション開始ログ
                LogToFile($"[セッション開始] SessionId: {CurrentSession.SessionId}");
            }
            catch (Exception ex)
            {
                // 【v28.4 修正10】セッション開始エラーログ
                LogToFile($"[セッション開始エラー] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                
                // 録音失敗時の状態をリセット
                if (CurrentSession != null)
                {
                    CurrentSession.Dispose();
                    CurrentSession = null;
                }
                
                // エラー表示を詳細に
                string errorMessage = "録音の開始に失敗しました。\n\n";
                errorMessage += "考えられる原因:\n";
                errorMessage += "• マイクが接続されていない\n";
                errorMessage += "• マイクの使用許可がされていない\n";
                errorMessage += "• 他のアプリケーションがマイクを使用中\n";
                errorMessage += "• 録音デバイスが無効化されている\n\n";
                errorMessage += $"詳細エラー: {ex.Message}";
                
                StatusText.Text = "❌ 録音開始失敗";
                StatusText.Foreground = Brushes.Red;
                SummaryText = "❌ 録音開始に失敗しました";
                
                MessageBox.Show(errorMessage, "録音エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PauseRecording()
        {
            try
            {
                if (CurrentSession == null) return;

                StopTimer();
                CurrentSession.PauseRecording();
                
                // 一時停止時は黄色いアイコンに変更
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_yellow.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch (Exception ex) {
                    Debug.WriteLine($"アイコン設定エラー: {ex.Message}");
                }
                // StatusText.Text = $"⏸ 一時停止中 (経過: {elapsedSeconds}秒)";
                
                // 一時停止時のテキスト更新
                SummaryText = "⏸ 一時停止中...";
                
                // 一時停止タイマー開始
                pausedSeconds = 0;
                // StartPauseTimer(); // UpdateUIForCurrentSession で制御するため削除
                
                // ボタン表示を更新
                RecordButton.Content = "ReStart";
                var border = RecordButton.Template.FindName("border", RecordButton) as Border;
                if (border != null)
                {
                    border.Background = (SolidColorBrush)FindResource("RestartButtonNormalBrush");
                }
                EndOrangeGlow(); // 点灯を停止
                
                UpdateUIForCurrentSession();
            }
            catch (Exception ex)
            {
                MessageBox.Show("一時停止中にエラーが発生しました。\n" + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResumeRecording()
        {
            try
            {
                if (CurrentSession == null) return;

                StopPauseTimer();
                
                // Phase 1: RecordingSession に委譲
                CurrentSession.ResumeRecording();
                
                // Phase 1: CurrentRecorder を設定（RecordingSession からは設定不可のため）
                CurrentRecorder = CurrentSession.Recorder;

                // 録音ファイルが実際に作成されるまで待機（最大1秒）
                if (CurrentSession.SessionRecordingFiles.Count > 0)
                {
                    string lastFile = CurrentSession.SessionRecordingFiles[CurrentSession.SessionRecordingFiles.Count - 1];
                    Debug.WriteLine($"録音再開: ファイル作成待機開始: {lastFile}");
                    int retryCount = 0;
                    bool fileCreated = false;
                    while (retryCount < 10 && !fileCreated)
                    {
                        await Task.Delay(100);
                        if (File.Exists(lastFile))
                        {
                            var fileInfo = new FileInfo(lastFile);
                            if (fileInfo.Length > 0)
                            {
                                fileCreated = true;
                                Debug.WriteLine($"録音再開: ファイル作成確認完了: {fileInfo.Length} bytes");
                            }
                        }
                        retryCount++;
                    }

                    if (!fileCreated)
                    {
                        Debug.WriteLine("警告: 録音再開時のファイル作成確認に失敗しましたが、録音を継続します");
                    }
                }

                // 録音再開時は秒数を0にリセット
                elapsedSeconds = 0;
                
                // タイマー開始とUI更新は UpdateUIForCurrentSession に任せる
                // StartTimer(); 
                // StatusText.Text = "Listening...";
                // StatusText.Foreground = Brushes.Gray;
                
                // 録音再開時のテキスト更新
                SummaryText = "🎤 録音中...";
                
                // 録音再開時は赤いアイコンに変更（複数回試行）
                bool iconSetSuccessfully = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try {
                        string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_red.ico");
                        using (DrawingIcon icon = new DrawingIcon(iconPath))
                        {
                            this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        }
                        iconSetSuccessfully = true;
                        Debug.WriteLine($"赤いアイコンの設定に成功しました（試行回数: {attempt + 1}）");
                        break;
                    } catch (Exception ex) {
                        Debug.WriteLine($"アイコン設定エラー（試行{attempt + 1}/3）: {ex.Message}");
                        if (attempt < 2) // 最後の試行でない場合は少し待機
                        {
                            Task.Delay(50).Wait();
                        }
                    }
                }

                if (!iconSetSuccessfully)
                {
                    Debug.WriteLine("警告: 赤いアイコンの設定に失敗しましたが、録音中であることを示すため点滅を開始します");
                }

                // アイコン設定の成功/失敗に関わらず、録音中は必ず点滅を開始
                BeginOrangeGlow();
                Debug.WriteLine("タスクバーアイコンの疑似常時点灯を開始しました");
                
                UpdateUIForCurrentSession();
            }
            catch (Exception ex)
            {
                // エラー表示を詳細に
                string errorMessage = "録音の再開に失敗しました。\n\n";
                errorMessage += "考えられる原因:\n";
                errorMessage += "• マイクが接続されていない\n";
                errorMessage += "• マイクの使用許可がされていない\n";
                errorMessage += "• 他のアプリケーションがマイクを使用中\n";
                errorMessage += "• 録音デバイスが無効化されている\n\n";
                errorMessage += $"詳細エラー: {ex.Message}";
                
                StatusText.Text = "❌ 録音再開失敗";
                StatusText.Foreground = Brushes.Red;
                SummaryText = "❌ 録音再開に失敗しました";
                
                MessageBox.Show(errorMessage, "録音再開エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 要約処理を非同期で行うメソッド
        private async Task ProcessSummaryAsync(RecordingSession session, string textFilePath, long recordingDurationSeconds)
        {
            // 【v28.4 修正10】要約開始ログ
            LogToFile($"[要約開始] SessionId: {session.SessionId}, ファイル: {Path.GetFileName(textFilePath)}");
            
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (CurrentSession == session)
                    {
                        StatusText.Text = "🤖 要約中...";
                    }
                });

                // 要約API呼び出し
                // 時間計測開始
                System.Diagnostics.Stopwatch summaryStopwatch = new System.Diagnostics.Stopwatch();
                summaryStopwatch.Start();

                // v30.0: 事前情報がある場合は結合テキストを使用
                string rawSummaryContent;
                long ragProcessingTimeMs;
                string ragQueryText;
                string ragContext;
                
                if (session.HasPreInfo)
                {
                    // 事前情報＋文字起こしの結合テキストを取得
                    string combinedText = session.GetCombinedTextForSummary();
                    Debug.WriteLine($"📝 事前情報あり: 結合テキストで要約 ({combinedText.Length} 文字)");
                    
                    // 結合テキストを一時ファイルに保存（デバッグ用）
                    try
                    {
                        string combinedPath = Path.ChangeExtension(textFilePath, ".combined.txt");
                        await File.WriteAllTextAsync(combinedPath, combinedText);
                        Debug.WriteLine($"📄 結合テキスト保存: {combinedPath}");
                    }
                    catch { /* デバッグ用なので失敗しても続行 */ }
                    
                    // 結合テキストを使用して要約
                    (rawSummaryContent, ragProcessingTimeMs, ragQueryText, ragContext) = 
                        await SummarizeText.SummarizeFromCombinedTextAsync(combinedText, CurrentSelectedPrompt);
                }
                else
                {
                    // 従来通りファイルパスから要約
                    Debug.WriteLine($"📝 事前情報なし: 従来のファイルベース要約");
                    (rawSummaryContent, ragProcessingTimeMs, ragQueryText, ragContext) = 
                        await SummarizeText.SummarizeAsync(textFilePath);
                }

                summaryStopwatch.Stop();
                long totalProcessingTimeMs = summaryStopwatch.ElapsedMilliseconds;

                // UI更新用データの準備
                var (fact, assessment, todo) = ExtractSummaryContent(rawSummaryContent);
                string displayText = FormatSummaryForDisplay(fact, assessment, todo);
                string fileContent = displayText + Environment.NewLine + Environment.NewLine + "--- 処理時間 ---" + Environment.NewLine +
                                     $"録音停止から要約完了まで: {totalProcessingTimeMs} ms";

                // UI更新（メインスレッドで実行）
                this.Dispatcher.Invoke(() =>
                {
                    // セッションがまだ有効（かつ表示中）ならUIを更新
                    if (CurrentSession == session)
                    {
                        SummaryText = displayText;
                        StatusText.Text = "✅ 処理完了";
                        UpdatePatientName(rawSummaryContent);
                        
                        // v30.0: 再生成用にセッション情報を保持
                        _lastSummarizedSession = session;
                        
                        // 完了アイコンの設定など
                        try {
                            string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_green.ico");
                            using (DrawingIcon icon = new DrawingIcon(iconPath))
                            {
                                this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    icon.Handle,
                                    System.Windows.Int32Rect.Empty,
                                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            }
                        } catch { }
                    }
                    // セッションのキャッシュも更新
                    session.AppendTranscript(""); // 変更通知用（内容は変わらないが更新イベントを発火）
                });

                // ファイル保存（バックグラウンド）
                try
                {
                    string summaryPath = Path.ChangeExtension(textFilePath, ".summary.txt");
                    await File.WriteAllTextAsync(summaryPath, fileContent);
                    Debug.WriteLine($"💾 要約データ保存: {summaryPath}");
                    
                    // セッションに要約ファイルパスを設定
                    session.SetSummaryFilePath(summaryPath);

                    // 音声ファイル保存設定のチェックと削除処理
                    string saveAudioStr = Environment.GetEnvironmentVariable("SAVE_AUDIO_FILE") ?? "true";
                    if (!bool.TryParse(saveAudioStr, out bool saveAudio) || !saveAudio)
                    {
                        // 音声ファイル削除処理
                        foreach (var file in session.SessionRecordingFiles)
                        {
                            if (File.Exists(file)) File.Delete(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"要約ファイル保存エラー: {ex.Message}");
                    LogToFile($"要約ファイル保存エラー: {ex.Message}");
                }

                // Google Sheetsへのエクスポート（バックグラウンド）
                try
                {
                    string day = DateTime.Now.ToString("yyyy/MM/dd");
                    string time = DateTime.Now.ToString("HH:mm");
                    var (_, whoField) = ExtractFromAndWho(rawSummaryContent);
                    await GoogleSheetsExporter.ExportAsync(day, time, whoField, rawSummaryContent);
                    Debug.WriteLine("Google Sheetsへのエクスポート完了");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Google Sheetsエクスポートエラー: {ex.Message}");
                }
                
                // 【v28.4 修正10】要約完了ログ
                LogToFile($"[要約完了] SessionId: {session.SessionId}");
            }
            catch (Exception ex)
            {
                // 【v28.4 修正10】要約エラーログ
                LogToFile($"[要約エラー] SessionId: {session.SessionId}\n" +
                          $"例外: {ex.GetType().Name}\n" +
                          $"メッセージ: {ex.Message}\n" +
                          $"スタックトレース:\n{ex.StackTrace}");
                
                this.Dispatcher.Invoke(() =>
                {
                    if (CurrentSession == session)
                    {
                        StatusText.Text = "⚠️ 処理エラー";
                        SummaryText = "❌ エラーが発生しました: " + ex.Message;
                    }
                });
                Debug.WriteLine($"要約処理エラー: {ex.Message}");
            }
            finally
            {
                // セッションのリソース解放
                session.Dispose();
            }
        }

        private async Task StopRecordingAsync()
        {
            // 対策1: 再入防止ガード
            if (_isStopping)
            {
                Debug.WriteLine("StopRecordingAsync: 既に停止処理中のため、重複実行をスキップします。");
                return;
            }

            // 対策2: ローカル変数への退避（スナップショット）
            var session = CurrentSession;
            if (session == null) return;

            _isStopping = true; // ガード開始

            // StopRecordingAsyncの開始時間を計測
            System.Diagnostics.Stopwatch stopToSummaryStopwatch = new System.Diagnostics.Stopwatch();
            stopToSummaryStopwatch.Start();
            _isStopping = true; // ガード開始

            // 録音停止と最終処理（ここは失敗しても続行する努力をする）
            try
            {
                StopTimer();
                StopPauseTimer();
                StatusText.Text = "📝 文字起こし中...";
                
                // 【追加】SummaryTextを即座に「処理中...」に変更（ユーザーに停止を明確に伝える）
                // 【修正】InvokeAsyncに変更してデッドロック回避
                await this.Dispatcher.InvokeAsync(() =>
                {
                    SummaryText = "⏹ 処理中...\n\n" + (session.AccumulatedTranscript ?? "");
                });

                // 【修正】★重要★ デバイス停止後に最後のチャンクを取得する
                // NAudioはStopRecording()時に残りバッファをフラッシュするため、
                // 停止後にGetRemainingChunk()を呼ぶことで、フラッシュ後のデータを含む最後のチャンクを取得できる
                Debug.WriteLine("🔴 デバイス停止＆最後のチャンク取得を開始します");
                byte[]? lastChunk = await Task.Run(() => session.GetFinalChunkAndStopDevice());
                Debug.WriteLine($"📦 最後のチャンク取得: {(lastChunk?.Length ?? 0)} bytes");

                // フラグ更新＆クリーンアップ（デバイス停止は既に行われている）
                // ★★★ これにより session.IsRecording = false, session.IsStopped = true が設定される ★★★
                Debug.WriteLine("🔴 フラグ更新＆クリーンアップを実行します");
                await session.StopRecordingAsync();
                Debug.WriteLine("🟢 フラグ更新＆クリーンアップ完了");
                
                // CurrentRecorder をクリア（デバイス停止後に実行）
                if (CurrentRecorder == session.Recorder)
                {
                    CurrentRecorder = null;
                }

                // 【修正】最後のチャンクを処理（デバイス停止後に取得済み）
                // これにより「即時停止」と「最後のチャンク文字起こし」を両立
                Task? lastChunkTask = null;
                if (lastChunk != null && lastChunk.Length > 0)
                {
                    try
                    {
                        Debug.WriteLine($"📝 最後のチャンク処理開始: {lastChunk.Length} bytes");
                        lastChunkTask = ProcessChunkAsync(session, lastChunk);
                        session.RegisterChunkTask(lastChunkTask);
                        // 注意: ここではawaitしない
                        // 代わりに下のawait lastChunkTaskで待機する
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"最後のチャンク文字起こしエラー: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("⚠️ 最後のチャンクが空です（バッファにデータがなかった）");
                }

                // 対策1, 3: すべてのチャンク処理が完了するまで待機（最後のチャンクも含む）
                Debug.WriteLine("進行中のチャンク処理の完了を待機中...");
                await session.WaitForAllChunksAsync(); // session変数を使用
                
                // 【修正】最後のチャンク処理を明示的に待つ（確実に完了を保証）
                if (lastChunkTask != null)
                {
                    Debug.WriteLine("最後のチャンク処理の完了を待機中...");
                    await lastChunkTask;
                    Debug.WriteLine("✅ 最後のチャンク処理が完了しました");
                }
                
                Debug.WriteLine("✅ すべてのチャンク処理が完了しました - 要約処理に進みます");
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"録音停止処理の一部でエラー: {ex.Message}");
                 // 録音停止に失敗していても、ファイルがあれば要約はできる可能性があるため続行
            }

            // UIのクリーンアップ（エラーが出ても無視して続行）
            try
            {
                // 録音停止時は黒いアイコンに戻す
                try {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                    using (DrawingIcon icon = new DrawingIcon(iconPath))
                    {
                        this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                } catch { }
                EndOrangeGlow(); // 点灯を停止
            }
            catch { }

            // 本丸：ファイル確認と要約処理（ここは個別のtry-catchで守る）
            try
            {
                // 録音ファイルが存在するか確認
                bool hasValidFiles = false;
                try {
                    // 【修正】AsParallelをやめて軽量なforeachに変更
                    if (session.SessionRecordingFiles.Count > 0)
                    {
                        foreach (var filePath in session.SessionRecordingFiles)
                        {
                            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                            {
                                hasValidFiles = true;
                                break; // 1つでも有効なファイルがあればOK
                            }
                        }
                    }
                } catch { } // コレクション競合などのエラーはファイルなし扱い

                if (hasValidFiles)
                {
                    StatusText.Text = $"⏹ STOP（合計{elapsedSeconds}秒）";
                    
                    // 【v28.4 修正10】セッション終了ログ（要約処理開始前に記録）
                    LogToFile($"[セッション終了] SessionId: {session.SessionId}");

                    // 統合処理を開始（バックグラウンドで実行）
                    // ここで待機(await)しないことで、次の録音をすぐに開始できるようにする
                    string textFilePath = session.CurrentTextFilePath;
                    if (!string.IsNullOrEmpty(textFilePath) && File.Exists(textFilePath))
                    {
                        // バックグラウンドタスクとして開始（_ = で警告抑制）
                        _ = ProcessSummaryAsync(session, textFilePath, elapsedSeconds);
                    }
                    else
                    {
                        StatusText.Text = "⚠ テキストファイルがありません";
                        // ファイルがなくてもセッションは破棄する（ProcessSummaryAsyncが呼ばれない場合）
                        session.Dispose();
                    }
                }
                else
                {
                    StatusText.Text = "⚠ 有効な録音ファイルがありません";
                    SummaryText = "⚠ 録音ファイルが見つかりません";
                    StopBlinking();
                    // セッション破棄
                    session.Dispose();
                }
            }
            catch (Exception ex)
            {
                // ここでエラーが出るのは要約開始処理そのものが失敗した場合
                StatusText.Text = "⚠ 停止エラー";
                SummaryText = "❌ 停止中にエラーが発生しました: " + ex.Message;
                StopBlinking();
                MessageBox.Show($"停止中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // セッション破棄
                try { session.Dispose(); } catch { }
            }
            finally
            {
                // CurrentSessionのクリアは行わない
                // ここでは _isStopping フラグのみ解除
                _isStopping = false;
                
                // 【追加】強制的に状態をリセットしUIを更新する
                // 録音状態フラグが残っている場合は強制的にオフにする
                if (session != null && session.IsRecording)
                {
                    // 内部フラグを強制的にオフにする（例外が発生した場合の安全策）
                    try
                    {
                        // RecordingSessionのIsRecordingプロパティはprivate setなので、
                        // 直接変更はできないが、StopRecordingAsyncが呼ばれていれば
                        // 既にIsRecordingはfalseになっているはず
                        // 念のため、UI更新で状態を反映させる
                    }
                    catch { }
                }

                // UI更新を必ず実行（エラーが発生してもボタンをSTARTに戻す）
                try
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        UpdateUIForCurrentSession();
                        UpdateButtonAppearance(); // ボタンを確実に更新
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UI更新エラー: {ex.Message}");
                    // UI更新に失敗してもアプリは継続
                }
            }
        }
        private void StartTimer()
        {
            if (timer == null)
            {
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, e) => {
                    elapsedSeconds++;
                    int currentFileSeconds = elapsedSeconds % MAX_RECORDING_SECONDS;
                    if (currentFileSeconds == 0 && elapsedSeconds > 0)
                    {
                        currentFileSeconds = MAX_RECORDING_SECONDS;
                    }
                    
                    // 秒数表示を復活
                    StatusText.Text = $"Listening... {elapsedSeconds}秒経過";

                    // Phase 1: CurrentSession から最新の録音ファイルを取得
                    try
                    {
                        var session = CurrentSession; // スナップショット（対策2）
                        if (session != null && session.SessionRecordingFiles.Count > 0)
                        {
                            string latestFile = session.SessionRecordingFiles[session.SessionRecordingFiles.Count - 1];
                            if (File.Exists(latestFile))
                            {
                                long sizeBytes = new FileInfo(latestFile).Length;
                                if (sizeBytes == 0)
                                {
                                    Debug.WriteLine($"警告: 録音ファイルのサイズが0バイトです: {latestFile}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"タイマー内でのファイル処理エラー: {ex.Message}");
                    }
                };
            }
            timer.Start();
        }

        private void StartPauseTimer()
        {
            if (pauseTimer == null)
            {
                pauseTimer = new DispatcherTimer();
                pauseTimer.Interval = TimeSpan.FromSeconds(1);
                pauseTimer.Tick += (s, e) => {
                    pausedSeconds++;
                    // 秒数表示は行わない
                    
                    if (pausedSeconds >= MAX_PAUSE_SECONDS)
                    {
                        StatusText.Text = "一時停止が長すぎます。自動停止します。";
                        RecordButton_Click(this, new RoutedEventArgs());
                    }
                };
            }
            pauseTimer.Start();
        }

        private void StopTimer()
        {
            timer?.Stop();
        }

        private void StopPauseTimer()
        {
            pauseTimer?.Stop();
        }

        // Phase 1: このメソッドは使用されていない（計画書によると、録音セッションごとに1つのファイル）
        // 削除予定だが、エラー回避のため一時的にコメントアウト
        /*
        private async void SwitchToNewRecordingFile()
        {
            // Phase 1: この機能は削除（録音セッションごとに1つのファイルを維持）
        }
        */

        // STT/LLMの事前ウォームアップ（ベストエフォート）。録音中に接続を温める
        private async Task WarmUpServicesAsync()
        {
            try
            {
                var stt = SpeechToText.WarmUpAsync();
                var llm = SummarizeText.WarmUpAsync();
                await Task.WhenAll(stt, llm);
            }
            catch { /* ウォームアップ失敗は致命でないため無視 */ }
        }

        private void StartBlinking()
        {
            if (blinkTimer == null)
            {
                blinkTimer = new DispatcherTimer();
                blinkTimer.Interval = TimeSpan.FromMilliseconds(500);
                blinkTimer.Tick += (s, e) => {
                    StatusText.Visibility = StatusText.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                };
            }
            blinkTimer.Start();
        }

        private void StopBlinking()
        {
            blinkTimer?.Stop();
            StatusText.Visibility = Visibility.Visible;
        }

        private (string fromField, string whoField) ExtractFromAndWho(string content)
        {
            // 複数のパターンを試行する（後方互換性を保つため）
            
            Debug.WriteLine("=== 患者名抽出デバッグ ===");
            Debug.WriteLine($"要約内容の最初の200文字: {content.Substring(0, Math.Min(200, content.Length))}");
            
            // パターン1: info[ - who: 小林さん ] 形式
            var whoInfoMatch = System.Text.RegularExpressions.Regex.Match(content, @"-\s*who:\s*([^\r\n\]]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // パターン2: who[小林さん] 形式（従来形式）
            var whoMatch = System.Text.RegularExpressions.Regex.Match(content, @"who\[([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // パターン3: from[値] 形式
            var fromMatch = System.Text.RegularExpressions.Regex.Match(content, @"from\[([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            string fromField = fromMatch.Success ? fromMatch.Groups[1].Value.Trim() : "";
            string whoField = "";
            
            if (whoInfoMatch.Success)
            {
                whoField = whoInfoMatch.Groups[1].Value.Trim();
                Debug.WriteLine($"パターン1で抽出: '{whoField}'");
            }
            else if (whoMatch.Success)
            {
                whoField = whoMatch.Groups[1].Value.Trim();
                Debug.WriteLine($"パターン2で抽出: '{whoField}'");
            }
            else
            {
                Debug.WriteLine("どのパターンでも患者名を抽出できませんでした");
            }
            
            Debug.WriteLine($"最終的な患者名抽出結果: '{whoField}' (パターン1: {whoInfoMatch.Success}, パターン2: {whoMatch.Success})");
            Debug.WriteLine("=== 患者名抽出デバッグ終了 ===");
            
            return (fromField, whoField);
        }

        /// <summary>
        /// 録音情報（日時、患者名）を表示に反映する
        /// </summary>
        private void UpdateRecordingInfo()
        {
            DateTime now = DateTime.Now;
            CurrentDate = now.ToString("yyyy/MM/dd");
            CurrentTime = now.ToString("HH:mm");
            // 録音開始時に患者名を「未設定」にリセット（前の患者名が残らないようにする）
            CurrentPatientName = "未設定";
            // セッションの患者名もリセット
            if (CurrentSession != null)
            {
                CurrentSession.PatientName = "未設定";
            }
        }

        /// <summary>
        /// 要約から患者名を抽出して表示を更新する
        /// </summary>
        private void UpdatePatientName(string summaryContent)
        {
            var (_, whoField) = ExtractFromAndWho(summaryContent);
            CurrentPatientName = string.IsNullOrEmpty(whoField) ? "未設定" : whoField;
            
            // セッションの患者名も更新（タブ表示用）
            // 要約結果の患者名に合わせて自動変更（編集したテキストの内容は保持せず上書き）
            if (CurrentSession != null)
            {
                CurrentSession.PatientName = CurrentPatientName;
            }
        }

        private void ViewSummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // .envファイルからGoogleスプレッドシートIDを取得
                string spreadsheetId = CredentialsProvider.GetGoogleSpreadsheetId();
                
                if (string.IsNullOrEmpty(spreadsheetId))
                {
                    MessageBox.Show("Googleスプレッドシートのリンクが設定されていません。\nCredential ManagerのGOOGLE_SPREADSHEET_IDを確認してください。", 
                        "設定エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // GoogleスプレッドシートのURLを構築
                string spreadsheetUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";
                
                // デフォルトブラウザでスプレッドシートを開く
                Process.Start(new ProcessStartInfo(spreadsheetUrl) { UseShellExecute = true });
                
                Debug.WriteLine($"Googleスプレッドシートを開きました: {spreadsheetUrl}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Googleスプレッドシートを開く際にエラーが発生しました:\n{ex.Message}", 
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"スプレッドシートオープンエラー: {ex.Message}");
            }
        }

        private void RecordButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // ホバー効果はスタイルで処理されるため、このメソッドは空にします
        }

        private void RecordButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // ホバー効果はスタイルで処理されるため、このメソッドは空にします
        }

        private void UpdateButtonAppearance()
        {
            if (IsRecording && !IsPaused)
            {
                // 録音中（STOP状態）
                RecordButton.Content = "STOP";
                RecordButton.Style = (Style)FindResource("StopButtonStyle");
            }
            else if (IsPaused)
            {
                // 一時停止中（ReStart状態）
                RecordButton.Content = "ReStart";
                RecordButton.Style = (Style)FindResource("RecordButtonStyle");
            }
            else
            {
                // 待機中（START状態）
                RecordButton.Content = "START";
                RecordButton.Style = (Style)FindResource("RecordButtonStyle");
            }
        }

        private void OpenTempFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // C:\tempフォルダをエクスプローラーで開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
                
                Debug.WriteLine($"エクスプローラーでフォルダを開きました: {outputDirectory}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダを開く際にエラーが発生しました:\n{ex.Message}", 
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"フォルダオープンエラー: {ex.Message}");
            }
        }

        // 【v28.4】エラーログフォルダを開くメニュー項目
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Karustep", "Logs");
                
                // フォルダが存在しない場合は作成
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                
                // エクスプローラーでフォルダを開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = logDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
                
                Debug.WriteLine($"エクスプローラーでログフォルダを開きました: {logDir}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログフォルダを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"ログフォルダオープンエラー: {ex.Message}");
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void EditDictionary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dictionaryEditor = new DictionaryEditorWindow();
                dictionaryEditor.Owner = this;
                dictionaryEditor.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"辞書編集画面の起動に失敗しました:\n{ex.Message}", 
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditSystemPrompt_Click(object sender, RoutedEventArgs e)
        {
            var editor = new SystemPromptEditorWindow();
            editor.ShowDialog();
        }

        private void EditApiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKeyWindow = new ApiKeySettingsWindow(this); // MainWindowのインスタンスを渡す
            apiKeyWindow.ShowDialog();
        }

        private void LoadPromptFiles()
        {
            try
            {
                string baseDirectory = AppContext.BaseDirectory;
                // 実行ディレクトリ直下の余計なtxtを拾わないように、まずは厳密に必要ファイルのみを列挙
                // ルール: 先頭が半角数字2桁+". "で始まる*.txt
                var allTxt = Directory.GetFiles(baseDirectory, "*.txt");
                var promptFiles = allTxt
                    .Where(f => {
                        string name = Path.GetFileNameWithoutExtension(f);
                        // 先頭が2桁数字+ピリオド。その後のスペースは任意
                        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[0-9]{2}\.[\s\u3000]*");
                    })
                    .Where(f => !Path.GetFileName(f).Equals("dictionary.txt", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals("selected_prompt.txt", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DepartmentMenu.Items.Clear();

                foreach (var file in promptFiles)
                {
                    var menuItem = new MenuItem
                    {
                        Header = Path.GetFileNameWithoutExtension(file),
                        Tag = file, // フルパスをTagに保存
                        IsCheckable = true
                    };
                    menuItem.Click += DepartmentMenuItem_Click;
                    DepartmentMenu.Items.Add(menuItem);
                }
                // 起動直後で CurrentSelectedPrompt が未設定の場合、最初の項目を自動選択
                if (string.IsNullOrEmpty(CurrentSelectedPrompt) && DepartmentMenu.Items.Count > 0)
                {
                    if (DepartmentMenu.Items[0] is MenuItem first)
                    {
                        CurrentSelectedPrompt = (string)first.Tag;
                    }
                }
                UpdatePromptMenu();
                
                // v30.0: 再生成メニューにも同じプロンプトを追加
                RegenerateMenu.Items.Clear();
                foreach (var file in promptFiles)
                {
                    var menuItem = new MenuItem
                    {
                        Header = Path.GetFileNameWithoutExtension(file),
                        Tag = file
                    };
                    menuItem.Click += RegenerateMenuItem_Click;
                    RegenerateMenu.Items.Add(menuItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロンプトファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DepartmentMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var clickedItem = (MenuItem)sender;
            CurrentSelectedPrompt = (string)clickedItem.Tag;
            
            UpdatePromptMenu();
            SaveSelectedPrompt();
        }

        private void UpdatePromptMenu()
        {
            foreach (MenuItem item in DepartmentMenu.Items)
            {
                item.IsChecked = (string)item.Tag == CurrentSelectedPrompt;
            }
            SelectedPromptText.Text = Path.GetFileNameWithoutExtension(CurrentSelectedPrompt);
        }

        private void SaveSelectedPrompt()
        {
            try
            {
                string savePath = Path.Combine(AppContext.BaseDirectory, "selected_prompt.txt");
                File.WriteAllText(savePath, CurrentSelectedPrompt);
            }
            catch (Exception ex)
            {
                LogToFile($"選択されたプロンプトの保存に失敗しました: {ex.Message}");
            }
        }

        private void LoadSelectedPrompt()
        {
            try
            {
                string savePath = Path.Combine(AppContext.BaseDirectory, "selected_prompt.txt");
                if (File.Exists(savePath))
                {
                    string savedPrompt = File.ReadAllText(savePath);
                    if (File.Exists(savedPrompt))
                    {
                        CurrentSelectedPrompt = savedPrompt;
                    }
                }
                UpdatePromptMenu();
            }
            catch (Exception ex)
            {
                LogToFile($"選択されたプロンプトの読み込みに失敗しました: {ex.Message}");
            }
        }

        // 【v28.4】ログをファイルに書き込む（LocalAppData配下、日付ごとのファイル分割）
        private void LogToFile(string message)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Karustep", "Logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                // 【v28.4】日付ごとのファイル分割（既存のLicenseManagerと同じ方式）
                string logFilePath = Path.Combine(logDir, $"app_error_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}\n");
            }
            catch
            {
                // ログファイルへの書き込み失敗は無視
            }
        }

        // 【v28.4 修正12】古いログファイルの自動削除（10日前以前）
        private void DeleteOldLogFiles()
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Karustep", "Logs");
                
                if (!Directory.Exists(logDir))
                    return;
                
                // 10日前の日付を計算
                DateTime cutoffDate = DateTime.Now.AddDays(-10);
                
                // ログフォルダ内の.logファイルを列挙
                string[] logFiles = Directory.GetFiles(logDir, "*.log");
                
                foreach (string filePath in logFiles)
                {
                    try
                    {
                        // ファイルの更新日時を使用（シンプルで確実）
                        DateTime fileDate = File.GetLastWriteTime(filePath);
                        
                        // 10日前以前のファイルを削除
                        if (fileDate < cutoffDate)
                        {
                            File.Delete(filePath);
                            Debug.WriteLine($"古いログファイルを削除: {Path.GetFileName(filePath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 個別ファイルの削除失敗は無視（他のファイルの削除を継続）
                        Debug.WriteLine($"ログファイル削除エラー: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // ログ削除処理の失敗は無視（アプリ起動を妨げない）
                Debug.WriteLine($"古いログファイル削除処理エラー: {ex.Message}");
            }
        }

        // ライセンス認証
        private async void CheckLicense()
        {
            try
            {
                _hardwareId = LicenseManager.GenerateHardwareId();
                HwidTextBlock.Text = _hardwareId;
                
                _isLicensed = await LicenseManager.VerifyLicenseAsync(_hardwareId);

                if (_isLicensed)
                {
                    // ライセンス認証成功時のポップアップ
                    MessageBox.Show("ライセンス認証に成功しました。\nアプリケーションを開始します。", 
                        "ライセンス認証成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    LicensePanel.Visibility = Visibility.Collapsed;
                    MainAppPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    // ライセンス認証失敗時は直接認証画面を表示（ポップアップなし）
                    LicensePanel.Visibility = Visibility.Visible;
                    MainAppPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ライセンス認証中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                LicensePanel.Visibility = Visibility.Visible;
                MainAppPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void CopyHwid_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(_hardwareId);
            MessageBox.Show("ハードウェアIDをコピーしました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SendEmail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // URLエンコーディングを適用
                string subject = Uri.EscapeDataString("カルステップの端末認証依頼");
                string bodyText = $@"カルステップ運営 御中

○○クリニックの○○です。
カルステップの端末認証登録をお願いします。
私のハードウェアIDは「{_hardwareId}」です。
よろしくお願い致します。";
                string body = Uri.EscapeDataString(bodyText);
                string mailto = $"mailto:mjsc0mpa2@gmail.com?subject={subject}&body={body}";
                
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"メールクライアントの起動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /* タスクバーアイコンをオレンジ色に点灯させるためのメソッド群 */
        private void BeginOrangeGlow()
        {
            if (flashRefreshTimer == null)
            {
                flashRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                flashRefreshTimer.Tick += (s, args) => {
                    // ウィンドウが非アクティブな場合のみ点灯
                    if (!this.IsActive)
                    {
                        FlashNow();
                    }
                };
            }
            flashRefreshTimer.Start();
            FlashNow(); // 即時実行
        }

        private void EndOrangeGlow()
        {
            flashRefreshTimer?.Stop();

            // いったん消灯
            var fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = _windowHandle,
                dwFlags = FLASHW_STOP
            };
            FlashWindowEx(ref fi);
        }

        /* 1回だけ FlashWindowEx を発行する関数 */
        private void FlashNow()
        {
            try
            {
                if (_windowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("無効なウィンドウハンドル: タスクバーアイコンを点灯できません");
                    return;
                }

                var fi = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = _windowHandle,
                    // タスクバーのみ ＋ タイマー駆動（uCount 無限）
                    dwFlags = FLASHW_TRAY | FLASHW_TIMER,
                    uCount = uint.MaxValue,
                    dwTimeout = 0
                };
                bool result = FlashWindowEx(ref fi);
                Debug.WriteLine($"FlashWindowEx 呼び出し結果: {result}, アクティブ状態: {this.IsActive}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タスクバーアイコン点灯エラー: {ex.Message}");
            }
        }

        private void CopyTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(SummaryTextBox.Text))
                {
                    // クリップボードにコピー
                    // 改行コードをWindows標準（\r\n）に統一（ダイナミクス等の古いアプリとの互換性確保）
                    string textToClipboard = SummaryTextBox.Text
                        .Replace("\r\n", "\n")   // まずCRLFをLFに統一
                        .Replace("\r", "\n")     // 古いMac形式も対応
                        .Replace("\n", "\r\n");  // 最後にCRLF（Windows標準）に統一
                    Clipboard.SetText(textToClipboard);
                    
                    // 成功メッセージを表示
                    StatusText.Text = "✅ テキストをコピーしました";
                    StatusText.Foreground = Brushes.Green;
                    
                    // 1秒後に準備完了に戻す
                    var copyStatusTimer = new DispatcherTimer();
                    copyStatusTimer.Interval = TimeSpan.FromSeconds(1);
                    copyStatusTimer.Tick += (s, args) =>
                    {
                        copyStatusTimer.Stop();
                        StatusText.Text = "⭕ 準備完了";
                        StatusText.Foreground = Brushes.Gray;
                    };
                    copyStatusTimer.Start();

                    // 録音中/一時停止中は赤/黄を維持。待機時のみ黒に戻す
                    if (!IsRecording && !IsPaused)
                    {
                        try {
                            string iconPath = Path.Combine(AppContext.BaseDirectory, "picture", "footswitch_black.ico");
                            using (DrawingIcon icon = new DrawingIcon(iconPath))
                            {
                                this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    icon.Handle,
                                    System.Windows.Int32Rect.Empty,
                                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            }
                        } catch (Exception ex) {
                            Debug.WriteLine($"アイコン設定エラー: {ex.Message}");
                        }
                    }
                }
                else
                {
                    StatusText.Text = "⚠ コピーするテキストがありません";
                    StatusText.Foreground = Brushes.Orange;
                    
                    // 2秒後に準備完了に戻す
                    var warningStatusTimer = new DispatcherTimer();
                    warningStatusTimer.Interval = TimeSpan.FromSeconds(2);
                    warningStatusTimer.Tick += (s, args) =>
                    {
                        warningStatusTimer.Stop();
                        StatusText.Text = "⭕ 準備完了";
                        StatusText.Foreground = Brushes.Gray;
                    };
                    warningStatusTimer.Start();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "⚠ コピーに失敗しました";
                StatusText.Foreground = Brushes.Red;
                Debug.WriteLine($"コピーエラー: {ex.Message}");
                
                // 2秒後に準備完了に戻す
                var errorStatusTimer = new DispatcherTimer();
                errorStatusTimer.Interval = TimeSpan.FromSeconds(2);
                errorStatusTimer.Tick += (s, args) =>
                {
                    errorStatusTimer.Stop();
                    StatusText.Text = "⭕ 準備完了";
                    StatusText.Foreground = Brushes.Gray;
                };
                errorStatusTimer.Start();
            }
        }

        /// <summary>
        /// 現在のマウスカーソル位置にあるコントロールに自動的にテキストを貼り付ける
        /// </summary>
        /// <returns>貼り付けが成功したかどうか</returns>
        private async Task<bool> AutoPasteToActiveControl()
        {
            try
            {
                // 現在のマウスカーソル位置を取得
                if (!GetCursorPos(out POINT cursorPos))
                {
                    return false;
                }

                // カーソル位置にあるトップレベルウィンドウを取得
                IntPtr topWindow = WindowFromPoint(cursorPos);
                if (topWindow == IntPtr.Zero)
                {
                    return false;
                }

                // スクリーン座標をクライアント座標に変換
                POINT clientPoint = cursorPos;
                if (!ScreenToClient(topWindow, ref clientPoint))
                {
                    return false;
                }

                // より精密なコントロール特定
                IntPtr targetControl = FindMostSpecificControlAtPoint(topWindow, clientPoint);
                if (targetControl == IntPtr.Zero)
                {
                    targetControl = topWindow;
                }

                // 段階的な強力なフォーカス設定（高速化）
                // 1. ウィンドウを復元（最小化されている場合）
                ShowWindow(topWindow, SW_RESTORE);
                await Task.Delay(50);

                // 2. ウィンドウを表示
                ShowWindow(topWindow, SW_SHOW);
                await Task.Delay(50);

                // 3. ウィンドウを最前面に移動
                BringWindowToTop(topWindow);
                await Task.Delay(50);

                // 4. SetWindowPosで強制的に最前面に
                SetWindowPos(topWindow, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                await Task.Delay(50);

                // 5. 最終的にSetForegroundWindow
                SetForegroundWindow(topWindow);
                await Task.Delay(100);

                // スレッドアタッチでフォーカス設定
                uint currentThreadId = GetCurrentThreadId();
                uint targetThreadId = GetWindowThreadProcessId(topWindow, out _);
                
                bool threadAttached = false;
                if (currentThreadId != targetThreadId)
                {
                    threadAttached = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                try
                {
                    // 実際のマウスクリックを再現してコントロールをアクティブにする
                    await PerformRealMouseClick(cursorPos, targetControl);
                    
                    // フォーカスが完全に設定されるまで統一的に待機
                    await Task.Delay(200);

                    // フォーカス状態を検証
                    if (!VerifyFocusState(topWindow, targetControl))
                    {
                        // フォーカス設定に失敗した場合、追加の強力なフォーカス設定を試行
                        await ForceWindowToForeground(topWindow);
                        await Task.Delay(400);
                    }

                    // 貼り付け前の最終待機（統一）
                    await Task.Delay(100);

                    // 複数の貼り付け方式を順次試行
                    return await TryMultiplePasteMethods();
                }
                finally
                {
                    // スレッドアタッチ解除
                    if (threadAttached)
                    {
                        AttachThreadInput(currentThreadId, targetThreadId, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動貼り付けエラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 指定した座標で最も具体的なコントロールを見つける
        /// </summary>
        private IntPtr FindMostSpecificControlAtPoint(IntPtr parentWindow, POINT clientPoint)
        {
            try
            {
                IntPtr bestMatch = IntPtr.Zero;
                int bestDepth = 0;

                // 複数の方法でコントロールを検索
                var candidates = new List<IntPtr>();

                // 方法1: ChildWindowFromPointEx（複数のフラグで試行）
                var control1 = ChildWindowFromPointEx(parentWindow, clientPoint, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
                if (control1 != IntPtr.Zero && control1 != parentWindow)
                    candidates.Add(control1);

                var control2 = ChildWindowFromPointEx(parentWindow, clientPoint, CWP_ALL);
                if (control2 != IntPtr.Zero && control2 != parentWindow)
                    candidates.Add(control2);

                // 方法2: RealChildWindowFromPoint
                var control3 = RealChildWindowFromPoint(parentWindow, clientPoint);
                if (control3 != IntPtr.Zero && control3 != parentWindow)
                    candidates.Add(control3);

                // 方法3: ChildWindowFromPoint
                var control4 = ChildWindowFromPoint(parentWindow, clientPoint);
                if (control4 != IntPtr.Zero && control4 != parentWindow)
                    candidates.Add(control4);

                // 各候補について、最も深い階層のコントロールを選択
                foreach (var candidate in candidates.Distinct())
                {
                    int depth = GetControlDepth(candidate);
                    if (IsTextInputControl(candidate) && depth > bestDepth)
                    {
                        bestMatch = candidate;
                        bestDepth = depth;
                    }
                }

                // テキスト入力コントロールが見つからない場合は、最も深い階層のコントロールを選択
                if (bestMatch == IntPtr.Zero)
                {
                    foreach (var candidate in candidates.Distinct())
                    {
                        int depth = GetControlDepth(candidate);
                        if (depth > bestDepth)
                        {
                            bestMatch = candidate;
                            bestDepth = depth;
                        }
                    }
                }

                return bestMatch;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// コントロールの階層の深さを取得
        /// </summary>
        private int GetControlDepth(IntPtr control)
        {
            int depth = 0;
            IntPtr parent = control;
            
            while (parent != IntPtr.Zero)
            {
                parent = GetParent(parent);
                if (parent != IntPtr.Zero)
                    depth++;
                
                // 無限ループ防止
                if (depth > 20)
                    break;
            }
            
            return depth;
        }

        /// <summary>
        /// テキスト入力可能なコントロールかどうかを判定
        /// </summary>
        private bool IsTextInputControl(IntPtr control)
        {
            try
            {
                StringBuilder className = new StringBuilder(256);
                GetClassName(control, className, className.Capacity);
                string classNameStr = className.ToString().ToLower();

                // 一般的なテキスト入力コントロールのクラス名
                return classNameStr.Contains("edit") ||
                       classNameStr.Contains("textbox") ||
                       classNameStr.Contains("input") ||
                       classNameStr.Contains("richedit") ||
                       classNameStr.Contains("combobox") ||
                       classNameStr.Contains("chrome_widgetwin") ||  // Chrome用
                       classNameStr.Contains("internetexplorer") ||  // IE用
                       classNameStr.Contains("mozilla");             // Firefox用
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>
        /// ターゲットコントロール用の正確なクライアント座標を計算
        /// </summary>
        private POINT CalculateControlClientPoint(IntPtr targetControl, POINT screenPoint)
        {
            try
            {
                POINT clientPoint = screenPoint;
                
                // スクリーン座標をターゲットコントロールのクライアント座標に変換
                if (ScreenToClient(targetControl, ref clientPoint))
                {
                    return clientPoint;
                }
                
                // 変換に失敗した場合は、相対座標を計算
                POINT controlScreenPos = new POINT { X = 0, Y = 0 };
                if (ClientToScreen(targetControl, ref controlScreenPos))
                {
                    return new POINT
                    {
                        X = screenPoint.X - controlScreenPos.X,
                        Y = screenPoint.Y - controlScreenPos.Y
                    };
                }
                
                // すべて失敗した場合は中央をクリック
                return new POINT { X = 5, Y = 5 };
            }
            catch
            {
                return new POINT { X = 5, Y = 5 };
            }
        }

        /// <summary>
        /// 実際のマウスクリックを再現してコントロールをアクティブにする
        /// </summary>
        private async Task PerformRealMouseClick(POINT screenPoint, IntPtr targetControl)
        {
            try
            {
                // 現在のマウス位置を保存
                GetCursorPos(out POINT originalPos);

                // ターゲット位置にマウスカーソルを移動
                SetCursorPos(screenPoint.X, screenPoint.Y);
                await Task.Delay(20);

                // SendInput APIを使った確実なマウスクリック
                if (!await PerformSendInputMouseClick(screenPoint))
                {
                    // フォールバック: mouse_event API
                    mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)screenPoint.X, (uint)screenPoint.Y, 0, UIntPtr.Zero);
                    await Task.Delay(30);
                    mouse_event(MOUSEEVENTF_LEFTUP, (uint)screenPoint.X, (uint)screenPoint.Y, 0, UIntPtr.Zero);
                }
                await Task.Delay(50);

                // 追加でウィンドウメッセージも送信（確実性を高めるため）
                POINT targetClientPoint = CalculateControlClientPoint(targetControl, screenPoint);
                IntPtr lParam = (IntPtr)((targetClientPoint.Y << 16) | (targetClientPoint.X & 0xFFFF));
                
                PostMessage(targetControl, WM_LBUTTONDOWN, IntPtr.Zero, lParam);
                await Task.Delay(5);
                PostMessage(targetControl, WM_LBUTTONUP, IntPtr.Zero, lParam);
                await Task.Delay(20);

                // フォーカスメッセージも送信
                PostMessage(targetControl, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

                // 元のマウス位置に戻す（オプション - ユーザーの操作を妨げないため）
                // SetCursorPos(originalPos.X, originalPos.Y);
            }
            catch
            {
                // エラーが発生した場合は従来の方法にフォールバック
                POINT targetClientPoint = CalculateControlClientPoint(targetControl, screenPoint);
                IntPtr lParam = (IntPtr)((targetClientPoint.Y << 16) | (targetClientPoint.X & 0xFFFF));
                SendMessage(targetControl, WM_LBUTTONDOWN, IntPtr.Zero, lParam);
                await Task.Delay(50);
                SendMessage(targetControl, WM_LBUTTONUP, IntPtr.Zero, lParam);
                SendMessage(targetControl, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            }
        }

        /// <summary>
        /// SendInput APIを使った確実なマウスクリック
        /// </summary>
        private async Task<bool> PerformSendInputMouseClick(POINT screenPoint)
        {
            try
            {
                INPUT[] inputs = new INPUT[2];

                // マウス左ボタン押下
                inputs[0] = new INPUT
                {
                    type = INPUT_MOUSE,
                    union = new INPUTUNION
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = MOUSEEVENTF_LEFTDOWN,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                // マウス左ボタン離す
                inputs[1] = new INPUT
                {
                    type = INPUT_MOUSE,
                    union = new INPUTUNION
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = MOUSEEVENTF_LEFTUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                uint result = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
                await Task.Delay(30);
                return result == 2;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// フォーカス状態を検証する
        /// </summary>
        private bool VerifyFocusState(IntPtr topWindow, IntPtr targetControl)
        {
            try
            {
                // フォアグラウンドウィンドウを確認
                IntPtr foregroundWindow = GetForegroundWindow();
                bool isForeground = (foregroundWindow == topWindow);

                // ウィンドウの状態を確認
                bool isEnabled = IsWindowEnabled(topWindow);
                bool isVisible = IsWindowVisible(topWindow);

                // コントロールの状態を確認
                bool controlEnabled = true;
                bool controlVisible = true;
                if (targetControl != topWindow)
                {
                    controlEnabled = IsWindowEnabled(targetControl);
                    controlVisible = IsWindowVisible(targetControl);
                }

                return isForeground && isEnabled && isVisible && controlEnabled && controlVisible;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// より強力なフォーカス設定を実行する
        /// </summary>
        private async Task ForceWindowToForeground(IntPtr hwnd)
        {
            try
            {
                // 1. ウィンドウを復元（最小化されている場合）
                ShowWindow(hwnd, SW_RESTORE);
                await Task.Delay(50);

                // 2. ウィンドウを表示
                ShowWindow(hwnd, SW_SHOW);
                await Task.Delay(50);

                // 3. ウィンドウを最前面に移動
                BringWindowToTop(hwnd);
                await Task.Delay(50);

                // 4. SetWindowPosで強制的に最前面に
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                await Task.Delay(50);

                // 5. 最終的にSetForegroundWindow
                SetForegroundWindow(hwnd);
                await Task.Delay(100);
            }
            catch
            {
                // エラーは無視して続行
            }
        }

        /// <summary>
        /// 複数の貼り付け方式を順次試行する
        /// </summary>
        private async Task<bool> TryMultiplePasteMethods()
        {
            // 方式1: SendInput API
            if (await TrySendInputPaste())
            {
                return true;
            }

            // 方式2: keybd_event API
            if (await TryKeybdEventPaste())
            {
                return true;
            }

            // 方式3: SendKeys
            if (await TrySendKeysPaste())
            {
                return true;
            }

            // 方式4: WM_PASTE メッセージ
            if (await TryWmPastePaste())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// SendInput APIを使用した貼り付け
        /// </summary>
        private async Task<bool> TrySendInputPaste()
        {
            try
            {
                INPUT[] inputs = new INPUT[4];

                // Ctrl押下
                inputs[0] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    union = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = 0 } }
                };

                // V押下
                inputs[1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    union = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = 0 } }
                };

                // V離す
                inputs[2] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    union = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } }
                };

                // Ctrl離す
                inputs[3] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    union = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } }
                };

                uint result = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
                await Task.Delay(50);
                return result == 4;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// keybd_event APIを使用した貼り付け
        /// </summary>
        private async Task<bool> TryKeybdEventPaste()
        {
            try
            {
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                await Task.Delay(5);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                await Task.Delay(5);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                await Task.Delay(50);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// SendKeysを使用した貼り付け
        /// </summary>
        private async Task<bool> TrySendKeysPaste()
        {
            try
            {
                WinForms.SendKeys.SendWait("^v");
                await Task.Delay(50);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// WM_PASTEメッセージを使用した貼り付け
        /// </summary>
        private async Task<bool> TryWmPastePaste()
        {
            try
            {
                // 現在のマウスカーソル位置を取得
                if (!GetCursorPos(out POINT cursorPos))
                {
                    return false;
                }

                // カーソル位置にあるウィンドウを取得
                IntPtr targetWindow = WindowFromPoint(cursorPos);
                if (targetWindow == IntPtr.Zero)
                {
                    return false;
                }

                // スクリーン座標をクライアント座標に変換
                POINT clientPoint = cursorPos;
                if (!ScreenToClient(targetWindow, ref clientPoint))
                {
                    return false;
                }

                // 子ウィンドウ（コントロール）を特定
                IntPtr targetControl = ChildWindowFromPointEx(targetWindow, clientPoint, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
                if (targetControl == IntPtr.Zero)
                {
                    targetControl = targetWindow;
                }

                // WM_PASTEメッセージを送信
                const uint WM_PASTE = 0x0302;
                SendMessage(targetControl, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                
                await Task.Delay(50);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private (string fact, string assessment, string todo) ExtractSummaryContent(string summaryContent)
        {
            var factMatch = System.Text.RegularExpressions.Regex.Match(summaryContent, @"fact\[([\s\S]+?)\]");
            var assessmentMatch = System.Text.RegularExpressions.Regex.Match(summaryContent, @"assessment\[([\s\S]+?)\]");
            var todoMatch = System.Text.RegularExpressions.Regex.Match(summaryContent, @"todo\[([\s\S]+?)\]");

            string fact = factMatch.Success ? factMatch.Groups[1].Value.Trim() : "情報なし";
            string assessment = assessmentMatch.Success ? assessmentMatch.Groups[1].Value.Trim() : "情報なし";
            string todo = todoMatch.Success ? todoMatch.Groups[1].Value.Trim() : "情報なし";

            return (fact, assessment, todo);
        }

        private string FormatSummaryForDisplay(string fact, string assessment, string todo)
        {
            return $"{fact}{Environment.NewLine}{Environment.NewLine}{assessment}{Environment.NewLine}{Environment.NewLine}{todo}";
        }

        // ウィンドウ操作用API
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // フォーカス診断・制御用API
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // ShowWindow用の定数
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        // SetWindowPos用の定数
        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        // キー状態監視用の追加定数とフィールド
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
        
        private const int VK_LWIN = 0x5B; // 左Windowsキーの仮想キーコード
        private const int VK_RWIN = 0x5C; // 右Windowsキーの仮想キーコード
        
        // Windowsキーが押されているかどうかを確認するメソッド
        private bool IsWinKeyPressed()
        {
            return (GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0;
        }

        // グローバルホットキーの修飾キーを保持するフィールド
        private int _currentCopyHotkeyModifier = 0;

        // コピーホットキーを登録するメソッド
        private bool RegisterCopyHotkey(int modifierKey)
        {
            if (_windowHandle == IntPtr.Zero)
            {
                Debug.WriteLine("ウィンドウハンドルがNULLのためコピーホットキー登録失敗");
                return false;
            }
            // まず既存のホットキーを解除
            UnregisterCopyHotkey();

            _currentCopyHotkeyModifier = modifierKey; // 現在のモディファイアを保持
            bool registered = RegisterHotKey(_windowHandle, COPY_HOTKEY_ID, modifierKey, VK_COMMA);
            Debug.WriteLine($"コピーホットキー登録: modifier={modifierKey}, success={registered}");
            return registered;
        }

        // コピーホットキーを解除するメソッド
        private void UnregisterCopyHotkey()
        {
            if (_windowHandle != IntPtr.Zero && _currentCopyHotkeyModifier != 0)
            {
                bool unregistered = UnregisterHotKey(_windowHandle, COPY_HOTKEY_ID);
                Debug.WriteLine($"コピーホットキー解除: modifier={_currentCopyHotkeyModifier}, success={unregistered}");
                _currentCopyHotkeyModifier = 0;
            }
        }

        // appsettings.txtの設定に基づいてコピーホットキーを更新するメソッド
        public void UpdateCopyHotkeySetting()
        {
            // appsettings.txtファイルを読み込む
            string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.txt");
            if (File.Exists(appSettingsPath))
            {
                DotNetEnv.Env.Load(appSettingsPath);
            }

            string hotkeyModifier = Environment.GetEnvironmentVariable("HOTKEY_MODIFIER_KEY") ?? "Alt";
            int newModifierKey = MOD_CONTROL | MOD_SHIFT;

            if (hotkeyModifier.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                newModifierKey |= MOD_WIN;
                Debug.WriteLine("HOTKEY_MODIFIER_KEY: Win (動的更新)");
            }
            else
            {
                newModifierKey |= MOD_ALT;
                Debug.WriteLine("HOTKEY_MODIFIER_KEY: Alt (動的更新)");
            }

            RegisterCopyHotkey(newModifierKey);
        }
    }
 
    public class SoundRecorder : IDisposable
    {
        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi)]
        private static extern int mciSendString(string lpstrCommand, StringBuilder lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        // デバイス操作の排他制御用ロック（クラス全体で共有）
        private static readonly object _deviceLock = new object();

        // 【Phase 1 修正】MCIエイリアスのユニーク化（セッション間の競合を回避）
        private readonly string _mciAlias;

        private readonly string filePath;
        private bool isRecording;
        private DateTime lastSoundTime;
        private DateTime? silenceStartTime = null; // 連続無音の開始時刻
        private const double SilenceThreshold = 0.04; // 無音と判定する閾値 (0.02 -> 0.04 に緩和)
        private const int SilenceTimeoutSeconds = 180;//無音の時のタイムアウト秒数
        private const double MinSilenceDurationSeconds = 0.3; // チャンク切り出しに必要な連続無音時間（秒） (1.0 -> 0.5 に短縮)
        private NAudio.Wave.WaveInEvent? waveIn;
        private NAudio.Wave.WaveFileWriter? waveFileWriter;
        private bool isUsingNAudio = false;
        private System.Diagnostics.Stopwatch debugTimer = new System.Diagnostics.Stopwatch();

        public event EventHandler? SilenceDetected;
        
        public class ChunkReadyEventArgs : EventArgs
        {
            public byte[] AudioData { get; }
            public ChunkReadyEventArgs(byte[] data) { AudioData = data; }
        }
        public event EventHandler<ChunkReadyEventArgs>? ChunkReady;

        public SoundRecorder(string filePath)
        {
            this.filePath = filePath;
            this.lastSoundTime = DateTime.Now;
            // 【Phase 1 修正】セッションごとにユニークなMCIエイリアスを生成（競合回避）
            this._mciAlias = $"capture_{Guid.NewGuid():N}";
            debugTimer.Start();
        }

        public void StartRecording()
        {
            if (isRecording)
                return;

            // デバイス操作を排他制御
            lock (_deviceLock)
            {
                try
                {
                    Debug.WriteLine($"録音開始試行: {filePath}");
                
                    // ファイルパスのディレクトリが存在することを確認
                    string? directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        Debug.WriteLine($"ディレクトリ作成: {directory}");
                    }

                    // 既存ファイルが存在する場合は削除
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Debug.WriteLine($"既存ファイル削除: {filePath}");
                    }

                    // まずNAudioでの録音を試行
                    if (TryNAudioRecording())
                    {
                        Debug.WriteLine("NAudio録音開始成功");
                        isRecording = true;
                        lastSoundTime = DateTime.Now;
                        silenceStartTime = null; // 無音開始時刻をリセット
                        lock (_chunkBuffer)
                        {
                            _chunkBuffer.Clear(); // チャンクバッファをクリア
                        }
                        return;
                    }

                    // NAudioが失敗した場合、MCI APIを使用
                    Debug.WriteLine("NAudio録音失敗、MCI APIを試行");
                    StringBuilder errorString = new StringBuilder(128);
                    int result;

                    // 【Phase 1 修正】ユニークなエイリアスを使用（セッション間の競合を回避）
                    Debug.WriteLine($"MCI: デバイスオープン試行 (alias: {_mciAlias})");
                    result = mciSendString($"open new type waveaudio alias {_mciAlias}", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI open failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        throw new Exception($"録音デバイスのオープンに失敗しました (コード: {result}): {mciError}");
                    }

                    Debug.WriteLine("MCI: フォーマット設定試行");
                    result = mciSendString($"set {_mciAlias} format tag pcm", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI set format failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        mciSendString($"close {_mciAlias}", new StringBuilder(), 0, IntPtr.Zero);
                        throw new Exception($"録音フォーマットの設定に失敗しました (コード: {result}): {mciError}");
                    }

                    result = mciSendString($"set {_mciAlias} bitspersample 16", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI set bitspersample failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        mciSendString($"close {_mciAlias}", new StringBuilder(), 0, IntPtr.Zero);
                        throw new Exception($"ビット深度の設定に失敗しました (コード: {result}): {mciError}");
                    }

                    result = mciSendString($"set {_mciAlias} samplespersec 16000", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI set samplespersec failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        mciSendString($"close {_mciAlias}", new StringBuilder(), 0, IntPtr.Zero);
                        throw new Exception($"サンプルレートの設定に失敗しました (コード: {result}): {mciError}");
                    }

                    result = mciSendString($"set {_mciAlias} channels 1", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI set channels failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        mciSendString($"close {_mciAlias}", new StringBuilder(), 0, IntPtr.Zero);
                        throw new Exception($"チャンネル数の設定に失敗しました (コード: {result}): {mciError}");
                    }

                    Debug.WriteLine("MCI: 録音開始試行");
                    result = mciSendString($"record {_mciAlias}", errorString, errorString.Capacity, IntPtr.Zero);
                    if (result != 0)
                    {
                        string mciError = GetMciErrorString(result);
                        Debug.WriteLine($"MCI record failed: {result}, Error: {errorString}, MCI Error: {mciError}");
                        mciSendString($"close {_mciAlias}", new StringBuilder(), 0, IntPtr.Zero);
                        throw new Exception($"録音の開始に失敗しました (コード: {result}): {mciError}");
                    }

                    isRecording = true;
                    lastSoundTime = DateTime.Now;
                    silenceStartTime = null; // 無音開始時刻をリセット
                    lock (_chunkBuffer)
                    {
                        _chunkBuffer.Clear(); // チャンクバッファをクリア
                    }
                    Debug.WriteLine($"MCI録音開始成功: {filePath}");
                    InitializeAudioMonitoring();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"録音開始エラー: {ex.Message}");
                    isRecording = false;
                    throw; // エラーを上位に伝播
                }
            }
        }

        private bool TryNAudioRecording()
        {
            try
            {
                waveFileWriter = new NAudio.Wave.WaveFileWriter(filePath, new NAudio.Wave.WaveFormat(16000, 16, 1));
                waveIn = new NAudio.Wave.WaveInEvent();
                waveIn.DeviceNumber = 0;
                waveIn.WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1);
                waveIn.DataAvailable += (s, e) => {
                    try
                    {
                        // 録音停止後は処理しない（isRecordingフラグでチェック）
                        if (!isRecording) return;
                        
                        // waveFileWriterがnullまたは既にDispose済みの場合は処理しない
                        if (waveFileWriter == null) return;
                        
                        waveFileWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        ProcessAudioChunk(e.Buffer, e.BytesRecorded);
                    }
                    catch (Exception ex)
                    {
                        // 既に閉じられたファイルへのアクセスエラーをキャッチ
                        if (ex.Message.Contains("closed") || ex.Message.Contains("disposed") || 
                            ex.Message.Contains("Cannot access a closed file"))
                        {
                            Debug.WriteLine($"DataAvailable: ファイルが既に閉じられています: {ex.Message}");
                        }
                        else
                        {
                            Debug.WriteLine($"DataAvailableエラー: {ex.Message}");
                        }
                    }
                };
                waveIn.RecordingStopped += (s, e) => {
                    // ここでのDisposeは行わない（StopRecordingメソッドで明示的に行うため）
                    // 二重Disposeを防ぐため、イベントハンドラ側では何もしない
                    // waveFileWriter?.Dispose();
                    // waveFileWriter = null;
                };
                waveIn.StartRecording();
                isUsingNAudio = true;
                Debug.WriteLine("NAudio録音開始成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAudio録音失敗: {ex.Message}");
                waveFileWriter?.Dispose();
                waveFileWriter = null;
                return false;
            }
        }

        private string GetMciErrorString(int errorCode)
        {
            var errorBuffer = new StringBuilder(128);
            mciGetErrorString(errorCode, errorBuffer, errorBuffer.Capacity);
            return errorBuffer.ToString();
        }

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

        private void InitializeAudioMonitoring()
        {
            try
            {
                waveIn = new NAudio.Wave.WaveInEvent();
                waveIn.DeviceNumber = 0;
                waveIn.WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1);
                waveIn.DataAvailable += (s, a) =>
                {
                    long elapsedMs = debugTimer.ElapsedMilliseconds;
                    if (elapsedMs < 1000) return;

                    bool isSilent = true;
                    for (int i = 0; i < a.BytesRecorded; i += 2)
                    {
                        short sample = (short)((a.Buffer[i + 1] << 8) | a.Buffer[i]);
                        double sample32 = sample / 32768.0;
                        if (Math.Abs(sample32) > SilenceThreshold)
                        {
                            isSilent = false;
                            break;
                        }
                    }

                    if (!isSilent)
                    {
                        lastSoundTime = DateTime.Now;
                    }
                    else
                    {
                        if ((DateTime.Now - lastSoundTime).TotalSeconds > SilenceTimeoutSeconds)
                        {
                            SilenceDetected?.Invoke(this, EventArgs.Empty);
                        }
                    }
                };
                waveIn.StartRecording();
                Debug.WriteLine("オーディオ監視開始");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーディオ監視の初期化エラー: {ex.Message}");
            }
        }

        public void UpdateSoundLevel(float level)
        {
            if (level > SilenceThreshold)
            {
                lastSoundTime = DateTime.Now;
            }
            else
            {
                if ((DateTime.Now - lastSoundTime).TotalSeconds > SilenceTimeoutSeconds)
                {
                    SilenceDetected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void StopRecording()
        {
            if (!isRecording)
                return;

            // デバイス操作を排他制御
            lock (_deviceLock)
            {
                try
                {
                    if (isUsingNAudio)
                    {
                        // NAudio録音の停止
                        waveIn?.StopRecording();
                        
                        // ファイルライターを確実にDispose（try-catchで保護）
                        if (waveFileWriter != null)
                        {
                            try
                            {
                                waveFileWriter.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"waveFileWriter Disposeエラー: {ex.Message}");
                                // Disposeエラーが発生しても処理は継続
                            }
                            finally
                            {
                                waveFileWriter = null;
                            }
                        }
                        isUsingNAudio = false;
                        Debug.WriteLine("NAudio録音停止");
                    }
                    else
                    {
                        // MCI録音の停止
                        StringBuilder errorString = new StringBuilder(128);
                        int result;

                        // 【Phase 1 修正】ユニークなエイリアスを使用
                        Debug.WriteLine($"MCI: 録音停止処理開始 (alias: {_mciAlias})");

                        // 録音停止
                        result = mciSendString($"stop {_mciAlias}", errorString, errorString.Capacity, IntPtr.Zero);
                        if (result != 0)
                        {
                            Debug.WriteLine($"MCI stop failed: {result}, Error: {errorString}");
                        }

                        // ファイル保存（処理時間を計測）
                        Debug.WriteLine($"🔴 MCI save開始: {DateTime.Now:HH:mm:ss.fff}");
                        result = mciSendString($"save {_mciAlias} \"{filePath}\"", errorString, errorString.Capacity, IntPtr.Zero);
                        Debug.WriteLine($"🟢 MCI save完了: {DateTime.Now:HH:mm:ss.fff}");
                        if (result != 0)
                        {
                            Debug.WriteLine($"MCI save failed: {result}, Error: {errorString}");
                            throw new Exception($"録音ファイルの保存に失敗しました: {errorString}");
                        }

                        // デバイス終了
                        result = mciSendString($"close {_mciAlias}", errorString, errorString.Capacity, IntPtr.Zero);
                        if (result != 0)
                        {
                            Debug.WriteLine($"MCI close failed: {result}, Error: {errorString}");
                        }
                        Debug.WriteLine("MCI録音停止");
                    }

                    isRecording = false;
                    waveIn?.StopRecording();

                    // ファイルサイズチェック
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        Debug.WriteLine($"録音ファイル保存完了: {filePath}, サイズ: {fileInfo.Length} bytes");
                        
                        if (fileInfo.Length == 0)
                        {
                            throw new Exception("録音ファイルのサイズが0バイトです。録音が正常に行われませんでした。");
                        }
                    }
                    else
                    {
                        throw new Exception("録音ファイルが作成されませんでした。");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"録音停止エラー: {ex.Message}");
                    isRecording = false;
                    throw; // エラーを上位に伝播
                }
            }
        }

        // バッファリング用変数
        private List<byte> _chunkBuffer = new List<byte>();
        private const int MIN_CHUNK_SECONDS = 5;
        private const int MAX_CHUNK_SECONDS = 30; 
        private const int SAMPLE_RATE = 16000;
        private const int BITS_PER_SAMPLE = 16;
        private const int CHANNELS = 1;
        private const int BYTES_PER_SECOND = SAMPLE_RATE * CHANNELS * (BITS_PER_SAMPLE / 8);
        
        private void ProcessAudioChunk(byte[] buffer, int bytesRecorded)
        {
            byte[] actualData = new byte[bytesRecorded];
            Array.Copy(buffer, actualData, bytesRecorded);
            
            lock (_chunkBuffer)
            {
                _chunkBuffer.AddRange(actualData);
                
                double accumulatedSeconds = (double)_chunkBuffer.Count / BYTES_PER_SECOND;
                
                // 無音検知（直近のデータが静かかどうか）
                bool isSilentNow = IsSilent(buffer, bytesRecorded);

                // 長時間無音検知用の更新
                if (!isSilentNow)
                {
                    lastSoundTime = DateTime.Now;
                    // 音声が検出されたら、連続無音の開始時刻をリセット
                    silenceStartTime = null;
                }
                else
                {
                    // 無音が検出された場合、連続無音の開始時刻を記録（まだ記録されていない場合）
                    if (silenceStartTime == null)
                    {
                        silenceStartTime = DateTime.Now;
                    }
                    
                    // 長時間無音検知（180秒以上）
                    if ((DateTime.Now - lastSoundTime).TotalSeconds > SilenceTimeoutSeconds)
                    {
                         SilenceDetected?.Invoke(this, EventArgs.Empty);
                         lastSoundTime = DateTime.Now; // Reset
                    }
                }
                
                // --- パフォーマンスモードに基づく切り出し設定 ---
                int currentMode = RecordingSession.CurrentPerformanceMode;
                int maxChunkSeconds = 30; // Default (Realtime)
                int minChunkSeconds = 5;  // Default (Realtime)

                switch (currentMode)
                {
                    case 1: // Balanced
                        maxChunkSeconds = 90;
                        minChunkSeconds = 60;
                        break;
                    case 2: // LowLoad
                    case 3: // UltraLowLoad
                        maxChunkSeconds = 300; // 5分
                        minChunkSeconds = 300; // 実質無効
                        break;
                }

                // 切り出し判定
                bool shouldFlush = false;
                
                if (accumulatedSeconds >= maxChunkSeconds)
                {
                    // 上限到達 -> 強制切り出し
                    shouldFlush = true;
                }
                else
                {
                    // 無音による切り出し（Realtime/Balancedのみ）
                    // LowLoad / UltraLowLoad では無音切り出しを行わない（時間固定）
                    if (currentMode <= 1 && accumulatedSeconds >= minChunkSeconds && isSilentNow && silenceStartTime != null)
                    {
                        // 一定時間以上経過していて、連続無音が一定時間以上続いている場合
                        double silenceDuration = (DateTime.Now - silenceStartTime.Value).TotalSeconds;
                        if (silenceDuration >= MinSilenceDurationSeconds)
                        {
                            shouldFlush = true;
                        }
                    }
                }
                
                if (shouldFlush)
                {
                    FlushChunk();
                    // チャンクを切り出したら、無音開始時刻をリセット
                    silenceStartTime = null;
                }
            }
        }
        
        private bool IsSilent(byte[] buffer, int length)
        {
            // Peak detection to match existing logic
            for (int i = 0; i < length; i += 2)
            {
                if (i + 1 >= length) break;
                short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                double sample32 = sample / 32768.0;
                if (Math.Abs(sample32) > SilenceThreshold)
                {
                    return false; 
                }
            }
            return true;
        }
        
        private void FlushChunk()
        {
            if (_chunkBuffer.Count == 0) return;
            
            // WAVヘッダーを付けてイベント発火
            byte[] rawData = _chunkBuffer.ToArray();
            byte[] wavData = AddWavHeader(rawData);
            
            ChunkReady?.Invoke(this, new ChunkReadyEventArgs(wavData));
            
            _chunkBuffer.Clear();
        }
        
        public byte[]? GetRemainingChunk()
        {
            lock (_chunkBuffer)
            {
                if (_chunkBuffer.Count > 0)
                {
                    byte[] rawData = _chunkBuffer.ToArray();
                    _chunkBuffer.Clear();
                    return AddWavHeader(rawData);
                }
            }
            return null;
        }
        
        private byte[] AddWavHeader(byte[] pcmData)
        {
            using (var ms = new MemoryStream())
            {
                // NAudioのWaveFileWriterを使ってヘッダー付きで書き込むのが確実
                using (var writer = new NAudio.Wave.WaveFileWriter(ms, new NAudio.Wave.WaveFormat(SAMPLE_RATE, BITS_PER_SAMPLE, CHANNELS)))
                {
                    writer.Write(pcmData, 0, pcmData.Length);
                }
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            try
            {
                StopRecording();
            }
            catch
            {
                // Dispose時のエラーは無視
            }
            waveIn?.Dispose();
            waveIn = null;
            waveFileWriter?.Dispose();
            waveFileWriter = null;
            debugTimer.Stop();
        }
    }
}