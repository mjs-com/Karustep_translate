using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VoiceRecorder
{
    public partial class DictionaryEditorWindow : Window
    {
        private string _dictionaryPath;
        private string _originalContent = string.Empty;

        public DictionaryEditorWindow()
        {
            InitializeComponent();
            
            // 辞書ファイルのパスを設定
            _dictionaryPath = Path.Combine(AppContext.BaseDirectory, "dictionary.txt");
            
            LoadDictionary();
        }

        private void LoadDictionary()
        {
            try
            {
                if (File.Exists(_dictionaryPath))
                {
                    _originalContent = File.ReadAllText(_dictionaryPath);
                    DictionaryTextBox.Text = _originalContent;
                    Debug.WriteLine($"辞書ファイル読み込み成功: {_dictionaryPath}");
                }
                else
                {
                    // ファイルが存在しない場合は空の内容で開始
                    _originalContent = string.Empty;
                    DictionaryTextBox.Text = "# 補正辞書\n# 形式: 読み→変換後\n# 例:\n# しょうかいじょう→紹介状\n\n";
                    Debug.WriteLine($"辞書ファイルが存在しないため、新規作成モードで開始: {_dictionaryPath}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"辞書ファイルの読み込みに失敗しました:\n{ex.Message}", 
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"辞書ファイル読み込みエラー: {ex.Message}");
                this.Close();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string newContent = DictionaryTextBox.Text;
                
                // 内容が変更されていない場合は何もしない
                if (newContent == _originalContent)
                {
                    MessageBox.Show("変更がないため、保存をスキップしました。", 
                        "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    return;
                }

                try
                {
                    string binFilePath = _dictionaryPath; // 実行ファイルと同じ場所のパス
                    Debug.WriteLine($"binファイル保存パス: {binFilePath}");

                    // ① bin 側（＝実行ファイルと同じフォルダ）は常に保存
                    File.WriteAllText(binFilePath, newContent);
                    Debug.WriteLine($"binファイル保存先: {binFilePath}");
                    bool binSaved = File.Exists(binFilePath) && File.ReadAllText(binFilePath) == newContent;

#if DEBUG
                    // ② ソースディレクトリ探索と書き込みは DEBUG ビルドのときだけ
                    string? projectSourceDir = FindProjectSourceDirectory(AppContext.BaseDirectory, "VoiceRecorder");
                    if (projectSourceDir != null)
                    {
                        string sourcePath = Path.Combine(projectSourceDir, "dictionary.txt");
                        try
                        {
                            File.WriteAllText(sourcePath, newContent);
                            Debug.WriteLine($"ソースファイル保存先: {sourcePath}");
                            bool sourceSaved = File.Exists(sourcePath);
                            if (!sourceSaved)
                            {
                                Debug.WriteLine($"警告: ソースファイルへの保存に失敗しました: {sourcePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"警告: ソースファイルへの保存中にエラーが発生しました: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("警告: プロジェクトソースディレクトリが見つからなかったため、ソースファイルへの保存をスキップします。");
                    }
#endif

                    // ③ 最終的な成功判定 (bin側が保存できていればOKとする)
                    if (binSaved)
                    {
                        MessageBox.Show("補正辞書を保存しました。\n次回の音声認識から新しい辞書が適用されます。",
                            "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.DialogResult = true;
                    }
                    else
                    {
                        MessageBox.Show($"ファイルの保存に失敗しました: {binFilePath}", 
                            "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存中にエラーが発生しました:\n{ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存処理全体でエラーが発生しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // プロジェクトのソースディレクトリを探すヘルパー関数
        private string? FindProjectSourceDirectory(string startPath, string projectFolderName)
        {
            DirectoryInfo? currentDir = new DirectoryInfo(startPath);
            int maxDepth = 10; // 無限ループを防ぐための最大探索深度
            int currentDepth = 0;

            while (currentDir != null && currentDepth < maxDepth)
            {
                Debug.WriteLine($"探索中: {currentDir.FullName}");
                // プロジェクトフォルダ (例: VoiceRecorder) が存在するか確認
                DirectoryInfo projectDir = new DirectoryInfo(Path.Combine(currentDir.FullName, projectFolderName));
                if (projectDir.Exists)
                {
                    Debug.WriteLine($"プロジェクトソースディレクトリ発見: {projectDir.FullName}");
                    return projectDir.FullName;
                }

                currentDir = currentDir.Parent;
                currentDepth++;
            }

            Debug.WriteLine("プロジェクトソースディレクトリが見つかりませんでした。");
            return null;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 変更があるかチェック
            if (DictionaryTextBox.Text != _originalContent)
            {
                var result = MessageBox.Show("変更が保存されていません。本当にキャンセルしますか？", 
                    "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.No)
                {
                    return; // キャンセルを中止
                }
            }
            
            this.DialogResult = false;
        }
    }
} 