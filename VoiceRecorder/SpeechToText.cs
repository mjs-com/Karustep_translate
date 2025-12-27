using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;

namespace VoiceRecorder
{
    public class SpeechToText
    {
        /* ---------- 共通設定 ---------- */

        private static readonly HttpClient httpClient;

        private const int MaxRetryAttempts    = 3;
        private const int InitialRetryDelayMs = 1_000;

        private static readonly SemaphoreSlim _transcriptionSemaphore = new SemaphoreSlim(5); // 同時実行数を5に制限

        static SpeechToText()
        {
            httpClient = new HttpClient(
                new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 10, // 同時接続数を最大10に設定
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5) // 接続の再利用期間を5分に設定
                })
            {
                Timeout = TimeSpan.FromMinutes(10)          // ① 音声長に合わせて十分長く
            };
            // プロキシ相性問題を回避するため100-continueを無効化
            httpClient.DefaultRequestHeaders.ExpectContinue = false;
        }

        /// <summary>
        /// 軽量リクエストでDNS/TLS/接続プールを温める（録音中に先行実行）
        /// </summary>
        public static async Task WarmUpAsync()
        {
            try
            {
                string key    = GetEnvVar("AZURE_SPEECH_KEY");
                string region = "japaneast";
                string url    = $"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions?api-version=2024-11-15";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Ocp-Apim-Subscription-Key", key);
                req.Headers.Add("Ocp-Apim-Subscription-Region", region);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var _ = await httpClient.SendAsync(req, cts.Token);
            }
            catch { /* ウォームアップはベストエフォート */ }
        }

        private static string GetEnvVar(string key)
        {
            return key switch
            {
                "AZURE_SPEECH_KEY" => CredentialsProvider.GetAzureSpeechKey(),
                _ => throw new Exception($"環境変数 '{key}' の取得は未対応です。")
            };
        }

        /* ---------- ここからメイン処理 ---------- */

        public static async Task<string> TranscribeMultipleAudioAsync(
            List<string> audioFilePaths, string phoneId, string outputBasePath)
        {
            if (audioFilePaths == null || audioFilePaths.Count == 0)
                throw new ArgumentException("音声ファイルのリストが空です。");

            var transcriptionTasks = audioFilePaths
                .Where(File.Exists)
                .Select(path => (Path: path,
                                 Task: Task.Run(async () =>
                                 {
                                     await _transcriptionSemaphore.WaitAsync();
                                     try
                                     {
                                         return await StartFastTranscriptionWithRetry(path);
                                     }
                                     finally
                                     {
                                         _transcriptionSemaphore.Release();
                                     }
                                 })))
                .ToList();

            foreach (var item in transcriptionTasks)
                Console.WriteLine($"🔹 キュー投入: {Path.GetFileName(item.Path)}");

            // 並列実行結果を取得
            var results = new List<(string Path, string Text)>();
            foreach (var (path, task) in transcriptionTasks)
            {
                try   { results.Add((path, await task)); }
                catch { results.Add((path, "[文字起こし失敗]")); }
            }

            // ファイル名の末尾タイムスタンプ順で並べ替え
            results = results.OrderBy(r =>
                Path.GetFileNameWithoutExtension(r.Path).Split('_').Last()).ToList();

            /* --- 保存用テキストを組み立て --- */
            var sb = new StringBuilder()
                .AppendLine($"電話ID: {phoneId}")
                .AppendLine($"日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"録音合計: {results.Count * 5} 分")
                .AppendLine("【文字起こし結果】");

            for (int i = 0; i < results.Count; i++)
            {
                sb.AppendLine($"--- {i + 1} 本目 ---")
                  .AppendLine(results[i].Text).AppendLine();
            }

            string outPath = $"{outputBasePath}.txt";
            await File.WriteAllTextAsync(outPath, sb.ToString());
            Console.WriteLine($"💾 保存: {outPath}");
            return outPath;
        }

        public static async Task<string> TranscribeAudioAsync(string audioFilePath, string phoneId)
        {
            if (!File.Exists(audioFilePath))
                throw new ArgumentException($"音声ファイルが見つかりません: {audioFilePath}");

            byte[] audioBytes = await File.ReadAllBytesAsync(audioFilePath);
            string text = await StartFastTranscriptionWithRetry(audioBytes, Path.GetFileName(audioFilePath));
            string outputPath = Path.ChangeExtension(audioFilePath, ".txt");

            var sb = new StringBuilder()
                .AppendLine($"電話ID: {phoneId}")
                .AppendLine($"日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine("【文字起こし結果】")
                .AppendLine(text);

            await File.WriteAllTextAsync(outputPath, sb.ToString());
            return outputPath;
        }

        /* ---------- Fast Transcription 本体 ---------- */

        private static async Task<string> StartFastTranscriptionWithRetry(string audioPath)
        {
            byte[] bytes = await File.ReadAllBytesAsync(audioPath);
            return await StartFastTranscriptionWithRetry(bytes, Path.GetFileName(audioPath));
        }

        public static async Task<string> StartFastTranscriptionWithRetry(byte[] audioBytes, string fileName)
        {
            int attempt = 0, delay = InitialRetryDelayMs;
            while (true)
            {
                attempt++;
                try   
                { 
                    string result = await StartFastTranscription(audioBytes, fileName);
                    return result ?? ""; // 空文字許容
                }
                catch (Exception ex) when (IsTransientError(ex) && attempt < MaxRetryAttempts)
                {
                    Console.WriteLine($"⚠️ 一時エラー({attempt}/{MaxRetryAttempts}): {ex.Message}");
                    // 【v28.4 修正11】リトライログ
                    LogApiError($"リトライ {attempt}/{MaxRetryAttempts}: {ex.GetType().Name} - {ex.Message}");
                    await Task.Delay(delay);
                    delay *= 2;
                }
                catch (Exception ex)
                {
                    // 【v28.4 修正11】最終失敗ログ（スタックトレース含む）
                    LogApiError($"API最終失敗: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
        }
        
        // 【v28.4】APIエラーログメソッド（日付ごとのファイル分割）
        private static void LogApiError(string message)
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
                // 日付ごとのファイル分割
                string logFilePath = Path.Combine(logDir, $"api_error_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}\n");
            }
            catch { /* ログ失敗は無視 */ }
        }

        private static async Task<string> StartFastTranscription(byte[] audioBytes, string fileName)
        {
            string key    = GetEnvVar("AZURE_SPEECH_KEY");
            // Console.WriteLine($"🔑 APIキーを取得しました: {key.Substring(0, Math.Min(5, key.Length))}...");
            string region = "japaneast";
            string url    = $"https://{region}.api.cognitive.microsoft.com" +
                            "/speechtotext/transcriptions:transcribe?api-version=2024-11-15";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Headers.Add("Ocp-Apim-Subscription-Region", region);

            /* ----- multipart/form-data ----- */
            // Console.WriteLine($"🔄 APIリクエスト送信中: {fileName}");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var mp = new MultipartFormDataContent();

            var file = new ByteArrayContent(audioBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            mp.Add(file, "audio", fileName);

            string defJson = "{\"locales\":[\"ja-JP\"],\"format\":\"Display\"}";
            mp.Add(new StringContent(defJson, Encoding.UTF8, "application/json"), "definition");
            req.Content = mp;

            using var res = await httpClient.SendAsync(req, cts.Token);
            string jsonStr = await res.Content.ReadAsStringAsync();
            // Console.WriteLine($"📥 APIレスポンス受信: ステータスコード {res.StatusCode}");

            if (!res.IsSuccessStatusCode)
                throw new Exception($"STT失敗 {res.StatusCode}: {jsonStr}");

            /* ----- 出力抽出 ----- */
            // Console.WriteLine($"🔍 JSONレスポンスの解析開始");
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (root.TryGetProperty("combinedPhrases", out var cp) && cp.GetArrayLength() > 0)
            {
                // Console.WriteLine($"✅ combinedPhrases を検出: {cp.GetArrayLength()} 件");
                var texts = cp.EnumerateArray().Select(x => x.GetProperty("text").GetString()).ToList();
                // Console.WriteLine($"📝 検出されたテキスト: {string.Join(" | ", texts)}");
                return string.Join(" ", texts);
            }

            if (root.TryGetProperty("phrases", out var p) && p.GetArrayLength() > 0)
            {
                // Console.WriteLine($"✅ phrases を検出: {p.GetArrayLength()} 件");
                var texts = p.EnumerateArray().Select(x => x.GetProperty("text").GetString()).ToList();
                // Console.WriteLine($"📝 検出されたテキスト: {string.Join(" | ", texts)}");
                return string.Join(" ", texts);
            }

            return "";
        }

        private static bool IsTransientError(Exception ex) =>
               ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429") ||
               ex is HttpRequestException httpEx &&
                     (httpEx.StatusCode == HttpStatusCode.TooManyRequests ||
                      httpEx.StatusCode == HttpStatusCode.ServiceUnavailable ||
                      httpEx.StatusCode == HttpStatusCode.GatewayTimeout);
    }
}
