using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRecorder
{
    /// <summary>
    /// Google Sheets のサービスアカウントJSONを安全に取得・キャッシュするプロバイダ。
    /// - 取得元: Azure Functions (Key Vault参照でJSONを返すエンドポイント)
    /// - メモリキャッシュ: 最大7日間（アプリ稼働中は常駐）
    /// - 永続キャッシュ: %LOCALAPPDATA%\Karustep\Secrets\google-sheets-key.enc（DPAPIで暗号化）
    /// </summary>
    public static class SecretsProvider
    {
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly SemaphoreSlim fetchGate = new SemaphoreSlim(1, 1);

        // メモリキャッシュ（最大7日）
        private static string? cachedCredentialsJson;
        private static DateTime cachedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan cacheTtl = TimeSpan.FromDays(7);

        // 永続キャッシュ（DPAPI, CurrentUser）
        private static string GetSecretsDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "Karustep", "Secrets");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetSecretsFilePath() => Path.Combine(GetSecretsDir(), "google-sheets-key.enc");

        /// <summary>
        /// 設定変更後に強制的にキャッシュを無効化するためのメソッド。
        /// deletePersisted=true の場合はディスク上のDPAPIキャッシュも削除する。
        /// </summary>
        public static void ClearCachedCredentials(bool deletePersisted = false)
        {
            cachedCredentialsJson = null;
            cachedAtUtc = DateTime.MinValue;
            if (deletePersisted)
            {
                try
                {
                    string path = GetSecretsFilePath();
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { /* 失敗は致命ではないため無視 */ }
            }
        }

        // 即時取得を行わない方針に戻すため、ワンショットリフレッシュは撤去

        public static async Task<string> GetGoogleSheetsKeyJsonAsync()
        {
            // 有効なメモリキャッシュがあれば返す
            if (!string.IsNullOrEmpty(cachedCredentialsJson) && DateTime.UtcNow - cachedAtUtc < cacheTtl)
            {
                return cachedCredentialsJson!;
            }

            await fetchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // ダブルチェック
                if (!string.IsNullOrEmpty(cachedCredentialsJson) && DateTime.UtcNow - cachedAtUtc < cacheTtl)
                {
                    return cachedCredentialsJson!;
                }

                // 1) 永続キャッシュの読み込み
                if (TryLoadFromDisk(out var jsonFromDisk))
                {
                    cachedCredentialsJson = jsonFromDisk;
                    cachedAtUtc = DateTime.UtcNow;
                    return cachedCredentialsJson!;
                }

                // 2) Azure Functions から取得
                string url = BuildFunctionUrl();

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                var resp = await httpClient.GetAsync(url, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                string body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                var doc = JsonSerializer.Deserialize<JsonElement>(body);
                if (!doc.TryGetProperty("credentials", out var credProp))
                {
                    throw new InvalidOperationException("関数の応答にcredentialsフィールドがありません。");
                }
                string credentialsJson = credProp.GetString() ?? throw new InvalidOperationException("credentialsが空です。");

                // メモリキャッシュ + 永続キャッシュへ保存
                cachedCredentialsJson = credentialsJson;
                cachedAtUtc = DateTime.UtcNow;
                SaveToDisk(credentialsJson);
                return credentialsJson;
            }
            finally
            {
                fetchGate.Release();
            }
        }

        private static string BuildFunctionUrl()
        {
            // 運営側で管理する固定のAzure Functionsエンドポイント
            const string baseUrl = "https://func-karustep-trial-ggcphgaecpcbajft.japanwest-01.azurewebsites.net/api/GetGoogleSheetsKey";
            string functionKey = CredentialsProvider.GetAzureFunctionsKey();
            
            return $"{baseUrl}?code={Uri.EscapeDataString(functionKey)}";
        }

        private static bool TryLoadFromDisk(out string json)
        {
            json = string.Empty;
            try
            {
                string path = GetSecretsFilePath();
                if (!File.Exists(path)) return false;

                string payload = File.ReadAllText(path, Encoding.UTF8);
                var wrapper = JsonSerializer.Deserialize<SecretFilePayload>(payload);
                if (wrapper == null || string.IsNullOrEmpty(wrapper.Cipher)) return false;

                // TTL確認
                if (DateTime.UtcNow - new DateTime(wrapper.CachedAtUtcTicks, DateTimeKind.Utc) > cacheTtl)
                {
                    return false;
                }

                byte[] cipher = Convert.FromBase64String(wrapper.Cipher);
                byte[] plainBytes = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                json = Encoding.UTF8.GetString(plainBytes);
                return !string.IsNullOrWhiteSpace(json);
            }
            catch
            {
                return false;
            }
        }

        private static void SaveToDisk(string json)
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                var wrapper = new SecretFilePayload
                {
                    CachedAtUtcTicks = DateTime.UtcNow.Ticks,
                    Cipher = Convert.ToBase64String(cipher)
                };
                string payload = JsonSerializer.Serialize(wrapper);
                File.WriteAllText(GetSecretsFilePath(), payload, Encoding.UTF8);
            }
            catch
            {
                // 永続化失敗は致命ではないため無視
            }
        }

        private class SecretFilePayload
        {
            public long CachedAtUtcTicks { get; set; }
            public string Cipher { get; set; } = string.Empty;
        }
    }
}