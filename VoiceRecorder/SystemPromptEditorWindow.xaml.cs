using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic; // Added for List

namespace VoiceRecorder
{
    public class PromptFileInfo
    {
        public required string FilePath { get; set; }
        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
    }
    
    public partial class SystemPromptEditorWindow : Window
    {
        private string? _currentPromptPath;
        private string _fullContent = string.Empty;

        public SystemPromptEditorWindow(string? initialPromptPath = null)
        {
            InitializeComponent();
            
            string baseDirectory = AppContext.BaseDirectory;
            string? defaultPromptPath = null; // 数字付きの最初のプロンプトを後で決定
            string selectedPromptFilePath = Path.Combine(baseDirectory, "selected_prompt.txt");

            // 1. 引数で指定されたパスがある場合はそれを使用 (絶対パスを期待)
            if (!string.IsNullOrEmpty(initialPromptPath) && File.Exists(initialPromptPath))
            {
                _currentPromptPath = initialPromptPath;
                Debug.WriteLine($"引数で指定された有効なパスを使用: {_currentPromptPath}");
            }
            // 2. なければselected_prompt.txtから前回の選択を読み込む
            else if (File.Exists(selectedPromptFilePath))
            {
                try
                {
                    string selectedPromptName = File.ReadAllText(selectedPromptFilePath).Trim(); // 拡張子なし
                    string potentialPath = Path.Combine(baseDirectory, $"{selectedPromptName}.txt");
                    
                    Debug.WriteLine($"selected_prompt.txtから読み込み: {selectedPromptName}");
                    Debug.WriteLine($"構築したパス: {potentialPath}");
                    
                    if (File.Exists(potentialPath))
                    {
                        _currentPromptPath = potentialPath; // 絶対パス
                        Debug.WriteLine($"有効なパスを設定: {_currentPromptPath}");
                    }
                    else
                    {
                        _currentPromptPath = defaultPromptPath;
                        Debug.WriteLine($"ファイルが存在しないためデフォルトを使用: {_currentPromptPath}");
                    }
                }
                catch (Exception ex)
                {
                    _currentPromptPath = defaultPromptPath;
                    Debug.WriteLine($"エラーのためデフォルトを使用: {ex.Message}");
                }
            }
            // 3. どちらもなければデフォルト
            else
            {
                // 実行フォルダから先頭が数字の*.txtを探して最初のものを使用
                var firstPrompt = Directory.GetFiles(baseDirectory, "*.txt")
                    .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"^[0-9]{2}\.[\s\u3000]*"))
                    .OrderBy(f => Path.GetFileName(f))
                    .FirstOrDefault();
                _currentPromptPath = firstPrompt ?? string.Empty;
                Debug.WriteLine($"デフォルトを使用: {_currentPromptPath}");
            }
            
            // 念のため、最終的なパスが存在するか確認
            // 存在しない場合は未選択のまま（UIで選ばせる）

            // ウィンドウタイトルにファイル名を表示（未設定時は未選択）
            this.Title = $"システムプロンプト編集 - {Path.GetFileNameWithoutExtension(_currentPromptPath ?? string.Empty)}";
            


            LoadPromptFiles();
            LoadSystemPrompt(_currentPromptPath);
        }

        private void LoadPromptFiles()
        {
            try
            {
                string baseDirectory = AppContext.BaseDirectory;
                
                // 全てのプロンプトファイルを取得 (絶対パス)
                Debug.WriteLine("=== SystemPromptEditorWindow: プロンプトファイル検索開始 ===");
                var allFiles = Directory.GetFiles(baseDirectory, "*.txt");
                Debug.WriteLine($"検出された全txtファイル数: {allFiles.Length}");
                foreach (var file in allFiles)
                {
                    Debug.WriteLine($"検出ファイル: {file}, ファイル名: {Path.GetFileName(file)}");
                }

                // 除外するファイル名を明示的に定義
                var excludeFiles = new[] { "dictionary.txt", "selected_prompt.txt" };
                Debug.WriteLine($"除外ファイル: {string.Join(", ", excludeFiles)}");

                // 先頭が半角数字2桁+". "で始まる*.txt をプロンプトとして扱う
                var promptFiles = allFiles
                    .Where(f => {
                        string fileName = Path.GetFileName(f);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(f);
                        bool isPrompt = System.Text.RegularExpressions.Regex.IsMatch(fileNameWithoutExt, @"^[0-9]{2}\.[\s\u3000]*");
                        Debug.WriteLine($"ファイル '{fileName}' はプロンプトか: {isPrompt}");
                        return isPrompt;
                    })
                    .OrderBy(f => Path.GetFileName(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(f => new PromptFileInfo { FilePath = f })
                    .ToList();
                
                // デバッグ情報
                Debug.WriteLine($"LoadPromptFiles - 現在のプロンプトパス: {_currentPromptPath}");
                Debug.WriteLine($"LoadPromptFiles - 検出されたプロンプトファイル数: {promptFiles.Count}");
                
                // ComboBoxにアイテムを設定
                PromptComboBox.ItemsSource = promptFiles;
                
                // 現在のパス(絶対パス)に一致するアイテムを選択
                Debug.WriteLine($"LoadPromptFiles - 検索する絶対パス: {_currentPromptPath}");
                
                var selectedItem = promptFiles.FirstOrDefault(f =>
                    _currentPromptPath != null && f.FilePath.Equals(_currentPromptPath, StringComparison.OrdinalIgnoreCase));
                
                if (selectedItem != null)
                {
                    Debug.WriteLine($"LoadPromptFiles - 一致するアイテムを発見: {selectedItem.FileNameWithoutExtension}");
                    PromptComboBox.SelectedItem = selectedItem;
                }
                else
                {
                    Debug.WriteLine($"LoadPromptFiles - 一致するプロンプトが見つかりません: {_currentPromptPath}");
                    // デフォルトの選択 (最初のアイテム)
                    if (promptFiles.Count > 0)
                    {
                        Debug.WriteLine("LoadPromptFiles - デフォルトの選択を使用");
                        PromptComboBox.SelectedIndex = 0;
                        // 選択が変更されたので、現在のパスも更新
                        if (PromptComboBox.SelectedItem is PromptFileInfo defaultSelection)
                        {
                            _currentPromptPath = defaultSelection.FilePath;
                            Debug.WriteLine($"LoadPromptFiles - デフォルト選択後のパス更新: {_currentPromptPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロンプトファイルの読み込みに失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadSystemPrompt(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            
            try
            {
                if (File.Exists(path))
                {
                    _currentPromptPath = path;
                    _fullContent = File.ReadAllText(path);
                    ParseSystemPrompt();
                }
                else
                {
                    // ファイルが無い場合はエラーにせず何もしない（ユーザーが別のプロンプトを選べるように）
                    Debug.WriteLine("プロンプトファイルが見つかりませんでしたが、起動は継続します。");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルの読み込みに失敗しました:\n{ex.Message}", 
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void ParseSystemPrompt()
        {
            // 入力フォーマット抽出
            var inputMatch = Regex.Match(_fullContent, @"### 入力フォーマット（自動抽出対象）:(.*?)---", 
                RegexOptions.Singleline);
            if (inputMatch.Success)
            {
                InputFormatTextBox.Text = inputMatch.Groups[1].Value.Trim();
            }

            // factセクション抽出
            var factMatch = Regex.Match(_fullContent, @"fact\[(.*?)\]", RegexOptions.Singleline);
            if (factMatch.Success)
            {
                FactTextBox.Text = factMatch.Groups[1].Value.Trim();
            }

            // assessmentセクション抽出
            var assessmentMatch = Regex.Match(_fullContent, @"assessment\[(.*?)\]", RegexOptions.Singleline);
            if (assessmentMatch.Success)
            {
                AssessmentTextBox.Text = assessmentMatch.Groups[1].Value.Trim();
            }

            // todoセクション抽出
            var todoMatch = Regex.Match(_fullContent, @"todo\[(.*?)\]", RegexOptions.Singleline);
            if (todoMatch.Success)
            {
                TodoTextBox.Text = todoMatch.Groups[1].Value.Trim();
            }
        }

        private void PromptComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PromptComboBox.SelectedItem is PromptFileInfo selectedFile)
            {
                LoadSystemPrompt(selectedFile.FilePath);
            }
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                // ヘッダー部分を保持
                var headerMatch = Regex.Match(_fullContent, @"(.*?)### 入力フォーマット", RegexOptions.Singleline);
                string header = headerMatch.Success ? headerMatch.Groups[1].Value : "";

                // フッター部分を保持
                var footerMatch = Regex.Match(_fullContent, @"todo\[.*?\](.*)", RegexOptions.Singleline);
                string footer = footerMatch.Success ? footerMatch.Groups[1].Value : "";

                // 新しい内容を組み立て
                string newContent = header +
                    "### 入力フォーマット（自動抽出対象）:\n" + InputFormatTextBox.Text.Trim() + "\n\n---\n\n" +
                    "### 輸出フォーマット:\n" +
                    "info[\n - 日時: {yyyy-mm-dd hh:mm:ss}\n - who: {患者氏名（不明な場合は「不明」と記載）}\n]\n\n" +
                    "fact[\n" + FactTextBox.Text.Trim() + "\n]\n\n" +
                    "assessment[\n" + AssessmentTextBox.Text.Trim() + "\n]\n\n" +
                    "todo[\n" + TodoTextBox.Text.Trim() + "\n]" + footer;

                try
                {
                    if (string.IsNullOrEmpty(_currentPromptPath))
                    {
                        MessageBox.Show("保存先のプロンプトが未選択です。先にプロンプトを選択してください。",
                            "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    string binFilePath = _currentPromptPath; // 実行時に読み込んだパス = exeと同じ場所のパス
                    string fileName = Path.GetFileName(binFilePath) ?? "";
                    Debug.WriteLine($"binファイル保存パス: {binFilePath}");

                    // ① bin 側（＝実行ファイルと同じフォルダ）は常に保存
                    File.WriteAllText(binFilePath, newContent);
                    Debug.WriteLine($"binファイル保存先: {binFilePath}");
                    bool binSaved = File.Exists(binFilePath); // bin側の保存確認

#if DEBUG
                    // ② ソースディレクトリ探索と書き込みは DEBUG ビルドのときだけ (任意)
                    string? projectSourceDir = FindProjectSourceDirectory(AppContext.BaseDirectory, "VoiceRecorder");
                    if (projectSourceDir != null)
                    {
                        string sourcePath = Path.Combine(projectSourceDir, fileName);
                        try
                        {
                            File.WriteAllText(sourcePath, newContent);
                            Debug.WriteLine($"ソースファイル保存先: {sourcePath}");
                            bool sourceSaved = File.Exists(sourcePath); // ソース側の保存確認 (DEBUG時のみ)
                            if (!sourceSaved)
                            {
                                Debug.WriteLine($"警告: ソースファイルへの保存に失敗しました: {sourcePath}");
                                // bin側が成功していればエラー扱いにしない
                            }
                        }
                        catch (Exception ex)
                        {
                             Debug.WriteLine($"警告: ソースファイルへの保存中にエラーが発生しました: {ex.Message}");
                             // ここでもエラー扱いにしない
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
                        MessageBox.Show("システムプロンプトを保存しました。",
                            "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.DialogResult = true;
                    }
                    else
                    {
                         // bin側の保存に失敗した場合のみエラーとする
                        MessageBox.Show($"ファイルの保存に失敗しました: {binFilePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error); // Typo fix: MessageBoxError -> MessageBoxImage.Error
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
                    // さらに、その中に .csproj ファイルや .sln ファイルがあるか確認するとより確実
                    // ここでは単純にフォルダ名で判断
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
            this.DialogResult = false;
        }


    }
}