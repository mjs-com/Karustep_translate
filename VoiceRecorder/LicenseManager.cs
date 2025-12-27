using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.Windows;
using System.Diagnostics;

namespace VoiceRecorder
{
    public class LicenseManager
    {
        private const string API_BASE_URL = "https://karustep-license-api.onrender.com";
        private const string HWID_FILE_PATH = "hwid.txt";
        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        // HWIDを生成（CPU ID + マザーボードシリアル + ディスクシリアルの組み合わせ）
        public static string GenerateHardwareId()
        {
            StringBuilder sb = new StringBuilder();
            StringBuilder logBuilder = new StringBuilder();
            logBuilder.AppendLine("HWID生成開始:");

            try
            {
                // CPU情報を取得
                string cpuId = "不明";
                try
                {
                    using (ManagementClass mc = new ManagementClass("Win32_Processor"))
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            cpuId = mo["ProcessorId"]?.ToString() ?? "null";
                            sb.Append(cpuId);
                            logBuilder.AppendLine($"  CPU ID: {cpuId}");
                            break; // 最初のCPUのみ使用
                        }
                    }
                }
                catch (Exception ex)
                {
                    logBuilder.AppendLine($"  CPU情報取得エラー: {ex.Message}");
                }

                // マザーボード情報を取得
                string mbSerial = "不明";
                try
                {
                    using (ManagementClass mc = new ManagementClass("Win32_BaseBoard"))
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            mbSerial = mo["SerialNumber"]?.ToString() ?? "null";
                            sb.Append(mbSerial);
                            logBuilder.AppendLine($"  マザーボードシリアル: {mbSerial}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logBuilder.AppendLine($"  マザーボード情報取得エラー: {ex.Message}");
                }

                // ディスク情報を取得
                string diskSerial = "不明";
                try
                {
                    using (ManagementClass mc = new ManagementClass("Win32_DiskDrive"))
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            diskSerial = mo["SerialNumber"]?.ToString() ?? "null";
                            sb.Append(diskSerial);
                            logBuilder.AppendLine($"  ディスクシリアル: {diskSerial}");
                            break; // 最初のディスクのみ使用
                        }
                    }
                }
                catch (Exception ex)
                {
                    logBuilder.AppendLine($"  ディスク情報取得エラー: {ex.Message}");
                }
                
                // 生の結合文字列はログに残さない
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HWID生成エラー: {ex.Message}");
                LogToFile($"HWID生成エラー: {ex.Message}");
                logBuilder.AppendLine($"HWID生成エラー: {ex.Message}");
                
                // エラー時はマシン名とユーザー名を使用（フォールバック）
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;
                sb.Append(machineName);
                sb.Append(userName);
                logBuilder.AppendLine($"  フォールバック: マシン名={machineName}, ユーザー名={userName}");
            }

            // 文字列をハッシュ化してHWIDを生成
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                
                // ハッシュを16進数文字列に変換（最初の16文字のみ使用）
                StringBuilder hashBuilder = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    hashBuilder.Append(hashBytes[i].ToString("x2"));
                }
                
                string rawHash = hashBuilder.ToString();
                string masked = rawHash.Length >= 8 ? rawHash.Substring(0, 6) + "******" : rawHash;
                logBuilder.AppendLine($"  ハッシュ値(先頭のみ): {masked}");
                
                // 4文字ごとにハイフンを挿入して読みやすくする
                string hwid = $"{rawHash.Substring(0, 4)}-{rawHash.Substring(4, 4)}-{rawHash.Substring(8, 4)}-{rawHash.Substring(12, 4)}";
                logBuilder.AppendLine($"  生成されたHWID: {hwid}");
                
                
#if DEBUG
                Debug.WriteLine(logBuilder.ToString());
#endif
                LogToFile(logBuilder.ToString());
                
                return hwid;
            }
        }

        // HWIDをファイルに保存
        public static void SaveHardwareId(string hwid)
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Karustep");
                
                
#if DEBUG
                Debug.WriteLine($"HWID保存: パス={appDataPath}");
#endif
                LogToFile($"HWID保存: パス={appDataPath}");
                
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                    Debug.WriteLine($"HWID保存: ディレクトリを作成しました: {appDataPath}");
                    LogToFile($"HWID保存: ディレクトリを作成しました: {appDataPath}");
                }
                
                string hwidFilePath = Path.Combine(appDataPath, HWID_FILE_PATH);
                File.WriteAllText(hwidFilePath, hwid);
                
                
#if DEBUG
                Debug.WriteLine($"HWID保存成功: -> {hwidFilePath}");
#endif
                LogToFile($"HWID保存成功: -> {hwidFilePath}");
                
                // 保存後に再読み込みして確認
                if (File.Exists(hwidFilePath))
                {
                    string savedHwid = File.ReadAllText(hwidFilePath).Trim();
#if DEBUG
                    Debug.WriteLine($"HWID保存確認: 一致={savedHwid == hwid}");
#endif
                    LogToFile($"HWID保存確認: 一致={savedHwid == hwid}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HWID保存エラー: {ex.Message}");
                LogToFile($"HWID保存エラー: {ex.Message}");
                
                // 明示的にメッセージボックスを表示
                MessageBox.Show($"HWIDの保存中にエラーが発生しました。\nHWID: {hwid}\nエラー: {ex.Message}",
                    "HWID保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 保存されたHWIDを読み込み
        public static string? LoadHardwareId()
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Karustep");
                string hwidFilePath = Path.Combine(appDataPath, HWID_FILE_PATH);
                
                
#if DEBUG
                Debug.WriteLine($"HWID読み込み: パス={hwidFilePath}");
#endif
                LogToFile($"HWID読み込み: パス={hwidFilePath}");
                
                if (File.Exists(hwidFilePath))
                {
                    string hwid = File.ReadAllText(hwidFilePath).Trim();
#if DEBUG
                    Debug.WriteLine($"HWID読み込み成功");
#endif
                    LogToFile($"HWID読み込み成功");
                    return hwid;
                }
                else
                {
                    Debug.WriteLine("HWID読み込み: ファイルが存在しません");
                    LogToFile("HWID読み込み: ファイルが存在しません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HWID読み込みエラー: {ex.Message}");
                LogToFile($"HWID読み込みエラー: {ex.Message}");
            }
            
            return null;
        }

        // ログファイルに書き込む
        // 【v28.4】ログフォルダを%LocalAppData%に統一
        private static void LogToFile(string message)
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
                
                string logFile = Path.Combine(logDir, $"license_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログファイル書き込みエラー: {ex.Message}");
            }
        }

        // APIを使用してライセンスを検証
        public static async Task<bool> VerifyLicenseAsync(string hwid)
        {
            string logPrefix = $"[HWID:{hwid}] ";
            try
            {
                if (!Uri.TryCreate(API_BASE_URL, UriKind.Absolute, out var apiBase) || apiBase.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("ライセンスAPIのエンドポイントが無効です（HTTPS必須）");

                string url = $"{API_BASE_URL}/verify?hwid={hwid}";
#if DEBUG
                Debug.WriteLine($"{logPrefix}ライセンス検証リクエスト: {url}");
#endif
                LogToFile($"{logPrefix}ライセンス検証リクエスト: {url}");
                
                // HTTPリクエストを送信
                HttpResponseMessage response = await client.GetAsync(url);
                
                
#if DEBUG
                Debug.WriteLine($"{logPrefix}ライセンス検証レスポンス: ステータスコード={response.StatusCode}");
#endif
                LogToFile($"{logPrefix}ライセンス検証レスポンス: ステータスコード={response.StatusCode}");
                
                // ステータスコードが200（OK）の場合は有効と判断
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    string content = await response.Content.ReadAsStringAsync();
#if DEBUG
                    Debug.WriteLine($"{logPrefix}ライセンス検証結果: 有効 (ステータスコード200)");
#endif
                    LogToFile($"{logPrefix}ライセンス検証結果: 有効 (ステータスコード200), レスポンス内容={content}");
                    return true;
                }
                
                // 内容が"1"の場合も有効と判断（念のため）
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    
                    if (content.Trim() == "1")
                    {
                        LogToFile($"{logPrefix}ライセンス検証結果: 有効 (レスポンス内容が'1')");
                        return true;
                    }
                }
                
                // 認証失敗時のレスポンス内容もログに記録
                string failContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"{logPrefix}ライセンス検証結果: 無効");
                LogToFile($"{logPrefix}ライセンス検証結果: 無効, ステータスコード={response.StatusCode}, レスポンス内容={failContent}");
                
                // メッセージボックス表示を削除 (HWIDは画面に表示されるため)
                
                return false;
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == default)
            {
                // タイムアウトの場合
                Debug.WriteLine($"{logPrefix}ライセンス検証エラー: タイムアウト");
                LogToFile($"{logPrefix}ライセンス検証エラー: タイムアウト (10秒以内に応答がありませんでした), 詳細={ex.Message}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                // ネットワークエラー（DNS解決失敗、接続拒否、SSL/TLSエラーなど）
                string errorType = "ネットワークエラー";
                string errorDetail = ex.Message;
                
                // エラーメッセージから原因を推測
                if (ex.Message.Contains("SSL") || ex.Message.Contains("TLS") || ex.Message.Contains("certificate") || ex.Message.Contains("証明書"))
                {
                    errorType = "SSL/TLS証明書エラー";
                }
                else if (ex.Message.Contains("DNS") || ex.Message.Contains("host") || ex.Message.Contains("名前解決"))
                {
                    errorType = "DNS解決エラー";
                }
                else if (ex.Message.Contains("connection") || ex.Message.Contains("接続"))
                {
                    errorType = "接続エラー";
                }
                else if (ex.Message.Contains("proxy") || ex.Message.Contains("プロキシ"))
                {
                    errorType = "プロキシエラー";
                }
                
                Debug.WriteLine($"{logPrefix}ライセンス検証エラー: {errorType} - {errorDetail}");
                LogToFile($"{logPrefix}ライセンス検証エラー: {errorType}, 詳細={errorDetail}, InnerException={ex.InnerException?.Message ?? "なし"}");
                return false;
            }
            catch (Exception ex)
            {
                // その他のエラー
                Debug.WriteLine($"{logPrefix}ライセンス検証エラー: {ex.GetType().Name} - {ex.Message}");
                LogToFile($"{logPrefix}ライセンス検証エラー: 種類={ex.GetType().Name}, 詳細={ex.Message}, スタックトレース={ex.StackTrace}");
                return false;
            }
        }
    }
}