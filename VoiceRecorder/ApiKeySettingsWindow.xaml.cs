using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using DotNetEnv;
using CredentialManagement; // ★変更
using System.Windows.Input;
using System.Linq; // 追加
using System.ComponentModel; // 追加

namespace VoiceRecorder
{
    public partial class ApiKeySettingsWindow : Window, INotifyPropertyChanged
    {
        private readonly string _appSettingsFilePath;
        private readonly string _sourceAppSettingsFilePath;
        private string _sourceOriginalContent = string.Empty;
        private MainWindow _mainWindow; // MainWindowのインスタンスを保持するフィールドを追加
        private string _originalAzureFunctionsKey = string.Empty; // 変更検知用

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }



        public ApiKeySettingsWindow(MainWindow mainWindow) // コンストラクタにMainWindowのインスタンスを追加
        {
            InitializeComponent();
            this.DataContext = this; // DataContextを自身に設定
            _mainWindow = mainWindow; // インスタンスをフィールドに保存

            // プロジェクトのソースディレクトリのappsettings.txtファイルのパスを取得
            string? projectSourceDir = FindProjectSourceDirectory(AppContext.BaseDirectory, "VoiceRecorder");

            // 実行中のアプリケーションディレクトリのappsettings.txtファイルのパスを取得
            _appSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.txt");

            // プロジェクトのソースディレクトリのappsettings.txtファイルのパスを取得
            _sourceAppSettingsFilePath = projectSourceDir != null
                ? Path.Combine(projectSourceDir, "appsettings.txt")
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.txt");

            LoadSettings();
            LoadHotkeyModifier();
        }

        private void LoadSettings()
        {
            try
            {
                // appsettings.txtファイルの読み込み
                if (File.Exists(_appSettingsFilePath))
                {
                    Env.Load(_appSettingsFilePath);
                }
                else if (File.Exists(_sourceAppSettingsFilePath))
                {
                    Env.Load(_sourceAppSettingsFilePath);
                }
                else
                {
                    MessageBox.Show($"appsettings.txtが見つかりません: {_appSettingsFilePath}\nまたは: {_sourceAppSettingsFilePath}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // Credential Managerから値を読み込む
                AzureSpeechKeyPasswordBox.Password = ReadCredential("AZURE_SPEECH_KEY");
                AzureOpenAIKeyPasswordBox.Password = ReadCredential("AZURE_OPENAI_API_KEY");
                AzureFunctionsKeyPasswordBox.Password = ReadCredential("AZURE_FUNCTIONS_KEY");
                _originalAzureFunctionsKey = AzureFunctionsKeyPasswordBox.Password; // 読み込み時の値を保持
                GoogleSpreadsheetIdTextBox.Text = ReadCredential("GOOGLE_SPREADSHEET_ID");
                AzureOpenAIEndpointTextBox.Text = ReadCredential("AZURE_OPENAI_ENDPOINT");

                // appsettings.txtから固定値を設定
                AzureOpenAIDeploymentNameTextBox.Text = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? string.Empty;
                GoogleSheetNameTextBox.Text = Environment.GetEnvironmentVariable("GOOGLE_SHEET_NAME") ?? string.Empty;
                PcNameTextBox.Text = Environment.GetEnvironmentVariable("PC_NAME") ?? string.Empty;
                
                string saveAudioStr = Environment.GetEnvironmentVariable("SAVE_AUDIO_FILE") ?? "true";
                if (bool.TryParse(saveAudioStr, out bool saveAudio))
                {
                    SaveAudioFileCheckBox.IsChecked = saveAudio;
                }
                else
                {
                    SaveAudioFileCheckBox.IsChecked = true;
                }

                // パフォーマンスモードの設定
                string perfMode = Environment.GetEnvironmentVariable("PERFORMANCE_MODE") ?? "Realtime";
                foreach (System.Windows.Controls.ComboBoxItem item in PerformanceModeComboBox.Items)
                {
                    if ((string)item.Tag == perfMode)
                    {
                        PerformanceModeComboBox.SelectedItem = item;
                        break;
                    }
                }

                // ソースディレクトリの内容も読み込む（存在する場合）
                if (File.Exists(_sourceAppSettingsFilePath))
                {
                    _sourceOriginalContent = File.ReadAllText(_sourceAppSettingsFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API設定の読み込みに失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string azureSpeechKey = AzureSpeechKeyPasswordBox.Password.Trim();
                string azureOpenAIKey = AzureOpenAIKeyPasswordBox.Password.Trim();
                string azureFunctionsKey = AzureFunctionsKeyPasswordBox.Password.Trim();
                string azureOpenAIEndpoint = AzureOpenAIEndpointTextBox.Text.Trim();
                string azureOpenAIDeploymentName = AzureOpenAIDeploymentNameTextBox.Text.Trim();
                string googleSpreadsheetId = GoogleSpreadsheetIdTextBox.Text.Trim();
                string googleSheetName = GoogleSheetNameTextBox.Text.Trim();
                string pcName = PcNameTextBox.Text.Trim();

                // 選択されたホットキーモディファイアを取得
                string hotkeyModifier = AltKeyRadioButton.IsChecked == true ? "Alt" : "Win";
                string saveAudioFile = SaveAudioFileCheckBox.IsChecked == true ? "true" : "false";
                
                string performanceMode = "Realtime"; // Default
                if (PerformanceModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
                {
                    performanceMode = (string)selectedItem.Tag;
                }

                if (string.IsNullOrEmpty(azureSpeechKey) ||
                    string.IsNullOrEmpty(azureOpenAIKey) ||
                    string.IsNullOrEmpty(azureFunctionsKey) ||
                    string.IsNullOrEmpty(azureOpenAIEndpoint) ||
                    string.IsNullOrEmpty(googleSpreadsheetId))
                {
                    MessageBox.Show("必須項目を入力してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Credential Managerに保存
                WriteCredential("AZURE_SPEECH_KEY", "VoiceRecorder", azureSpeechKey);
                WriteCredential("AZURE_OPENAI_API_KEY", "VoiceRecorder", azureOpenAIKey);
                WriteCredential("AZURE_FUNCTIONS_KEY", "VoiceRecorder", azureFunctionsKey);
                WriteCredential("GOOGLE_SPREADSHEET_ID", "VoiceRecorder", googleSpreadsheetId);
                WriteCredential("AZURE_OPENAI_ENDPOINT", "VoiceRecorder", azureOpenAIEndpoint);

                // appsettings.txtの内容を更新
                string appSettingsContent = string.Empty;
                if (File.Exists(_appSettingsFilePath))
                {
                    appSettingsContent = File.ReadAllText(_appSettingsFilePath);
                }

                appSettingsContent = UpdateEnvVariable(appSettingsContent, "AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAIDeploymentName);
                appSettingsContent = UpdateEnvVariable(appSettingsContent, "GOOGLE_SHEET_NAME", googleSheetName);
                appSettingsContent = UpdateEnvVariable(appSettingsContent, "PC_NAME", pcName);
                appSettingsContent = UpdateEnvVariable(appSettingsContent, "HOTKEY_MODIFIER_KEY", hotkeyModifier); // 新しい設定を追加
                appSettingsContent = UpdateEnvVariable(appSettingsContent, "SAVE_AUDIO_FILE", saveAudioFile);
                appSettingsContent = UpdateEnvVariable(appSettingsContent, "PERFORMANCE_MODE", performanceMode);

                File.WriteAllText(_appSettingsFilePath, appSettingsContent);

                if (File.Exists(_sourceAppSettingsFilePath))
                {
                    string sourceAppSettingsContent = _sourceOriginalContent;
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAIDeploymentName);
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "GOOGLE_SHEET_NAME", googleSheetName);
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "PC_NAME", pcName);
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "HOTKEY_MODIFIER_KEY", hotkeyModifier); // 新しい設定を追加
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "SAVE_AUDIO_FILE", saveAudioFile);
                    sourceAppSettingsContent = UpdateEnvVariable(sourceAppSettingsContent, "PERFORMANCE_MODE", performanceMode);
                    File.WriteAllText(_sourceAppSettingsFilePath, sourceAppSettingsContent);
                }
                
                // 環境変数を更新（即時反映のため）
                Environment.SetEnvironmentVariable("SAVE_AUDIO_FILE", saveAudioFile);
                Environment.SetEnvironmentVariable("PERFORMANCE_MODE", performanceMode);

                // RecordingSessionの静的プロパティに反映
                RecordingSession.CurrentPerformanceMode = performanceMode switch
                {
                    "Realtime" => 0,
                    "Balanced" => 1,
                    "LowLoad" => 2,
                    "UltraLowLoad" => 3,
                    _ => 0
                };

                MessageBox.Show("API設定を保存しました。",
                    "成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // Azure Functions Key が変更された場合のみ、再起動案内を表示
                if (!string.Equals(azureFunctionsKey, _originalAzureFunctionsKey, StringComparison.Ordinal))
                {
                    MessageBox.Show("アプリを再起動し、設定を反映させてください", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                    _originalAzureFunctionsKey = azureFunctionsKey; // 次回比較用に更新
                }

                // MainWindowのホットキー設定を更新
                _mainWindow.UpdateCopyHotkeySetting();

                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API設定の保存に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string UpdateEnvVariable(string content, string key, string value)
        {
            var pattern = $@"^{key}=.*$";
            var regex = new Regex(pattern, RegexOptions.Multiline);

            if (regex.IsMatch(content))
            {
                return regex.Replace(content, $"{key}={value}");
            }
            else
            {
                return content + Environment.NewLine + $"{key}={value}";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private string? FindProjectSourceDirectory(string startPath, string projectFolderName)
        {
            DirectoryInfo? currentDir = new DirectoryInfo(startPath);
            int maxDepth = 10; // 無限ループを防ぐための最大探索深度
            int currentDepth = 0;

            while (currentDir != null && currentDepth < maxDepth)
            {
                DirectoryInfo projectDir = new DirectoryInfo(Path.Combine(currentDir.FullName, projectFolderName));
                if (projectDir.Exists)
                {
                    return projectDir.FullName;
                }

                currentDir = currentDir.Parent;
                currentDepth++;
            }

            return null;
        }

        private string ReadCredential(string credentialName)
        {
            try
            {
                var credential = new Credential { Target = credentialName, Type = CredentialType.Generic };
                if (credential.Load())
                {
                    return credential.Password;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"資格情報の読み込みに失敗しました ({credentialName}):\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }

        private void WriteCredential(string credentialName, string userName, string password)
        {
            try
            {
                var credential = new Credential
                {
                    Target = credentialName,
                    Username = userName,
                    Password = password,
                    Type = CredentialType.Generic,
                    PersistanceType = PersistanceType.LocalComputer
                };
                credential.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"資格情報の保存に失敗しました ({credentialName}):\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveCredential(string credentialName)
        {
            try
            {
                var credential = new Credential { Target = credentialName, Type = CredentialType.Generic };
                credential.Delete();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"資格情報の削除に失敗しました ({credentialName}):\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadHotkeyModifier()
        {
            string hotkeyModifier = Environment.GetEnvironmentVariable("HOTKEY_MODIFIER_KEY") ?? "Alt";
            if (hotkeyModifier.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                WinKeyRadioButton.IsChecked = true;
            }
            else
            {
                AltKeyRadioButton.IsChecked = true;
            }
        }

        private void SaveHotkeyModifier()
        {
            string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.txt");
            var lines = File.Exists(appSettingsPath) ? File.ReadAllLines(appSettingsPath).ToList() : new List<string>();

            string settingKey = "HOTKEY_MODIFIER_KEY";
            string newSettingValue = WinKeyRadioButton.IsChecked == true ? "Win" : "Alt";
            string newSettingLine = $"{settingKey}={newSettingValue}";

            int existingIndex = lines.FindIndex(line => line.StartsWith($"{settingKey}="));
            if (existingIndex != -1)
            {
                lines[existingIndex] = newSettingLine;
            }
            else
            {
                lines.Add(newSettingLine);
            }
            File.WriteAllLines(appSettingsPath, lines);
            Environment.SetEnvironmentVariable(settingKey, newSettingValue, EnvironmentVariableTarget.Process);

            _mainWindow.UpdateCopyHotkeySetting(); // MainWindowのホットキー設定を更新
        }


    }
}