using System;
using System.IO;
using CredentialManagement;

namespace VoiceRecorder
{
    public static class CredentialsProvider
    {
        private static string GetAppSettingsPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "appsettings.txt");
        }

        private static void EnsureAppSettingsLoaded()
        {
            string path = GetAppSettingsPath();
            if (File.Exists(path))
            {
                DotNetEnv.Env.Load(path);
            }
        }

        private static string? ReadFromCredentialManager(string targetName)
        {
            try
            {
                var cred = new Credential { Target = targetName, Type = CredentialType.Generic };
                return cred.Load() ? cred.Password : null;
            }
            catch
            {
                return null;
            }
        }

        // 4つの機密キー（Credential Managerのみ）
        public static string GetAzureSpeechKey()
        {
            return ReadFromCredentialManager("AZURE_SPEECH_KEY")
                   ?? throw new InvalidOperationException("AZURE_SPEECH_KEY がCredential Managerに登録されていません");
        }

        public static string GetAzureOpenAIApiKey()
        {
            return ReadFromCredentialManager("AZURE_OPENAI_API_KEY")
                   ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY がCredential Managerに登録されていません");
        }

        public static string GetGoogleSpreadsheetId()
        {
            return ReadFromCredentialManager("GOOGLE_SPREADSHEET_ID")
                   ?? throw new InvalidOperationException("GOOGLE_SPREADSHEET_ID がCredential Managerに登録されていません");
        }

        public static string GetAzureOpenAIEndpoint()
        {
            return ReadFromCredentialManager("AZURE_OPENAI_ENDPOINT")
                   ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT がCredential Managerに登録されていません");
        }

        public static string GetAzureFunctionsKey()
        {
            return ReadFromCredentialManager("AZURE_FUNCTIONS_KEY")
                   ?? throw new InvalidOperationException("AZURE_FUNCTIONS_KEY がCredential Managerに登録されていません");
        }

        // 固定値（appsettings.txtのみ参照。なければ既定値）
        public static string GetSetting(string key, string defaultValue)
        {
            EnsureAppSettingsLoaded();
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? defaultValue : v!;
        }

        // 必須設定（フォールバック既定値なし）
        private static string GetRequiredSetting(string key)
        {
            EnsureAppSettingsLoaded();
            var v = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(v))
            {
                throw new InvalidOperationException($"{key} が appsettings.txt に設定されていません");
            }
            return v!;
        }

        public static string GetGoogleSheetName() => GetSetting("GOOGLE_SHEET_NAME", "Home");
        public static string GetAzureOpenAIDeploymentName() => GetRequiredSetting("AZURE_OPENAI_DEPLOYMENT_NAME");
        public static bool GetAzureOpenAIRagSkipMode() => bool.Parse(GetSetting("AZURE_OPENAI_RAG_SKIP_MODE", "false"));
        public static bool GetEnableRagSummaryLog() => bool.Parse(GetSetting("ENABLE_RAG_SUMMARY_LOG", "false"));
        public static string GetPcName() => GetSetting("PC_NAME", "診察室①");
    }
}
