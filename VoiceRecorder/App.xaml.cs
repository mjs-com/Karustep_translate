using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace VoiceRecorder;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static bool s_softwareFallbackApplied = false;

    // Windows API: コンソールウィンドウを割り当てる
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    // Windows API: コンソールウィンドウを解放する
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    // Windows API: 標準出力ハンドルを取得
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    // Windows API: コンソール出力をリダイレクト
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    private const int STD_OUTPUT_HANDLE = -11;
    private static bool s_consoleEnabled = false; // コンソールが有効かどうか

    // コンソール出力用ヘルパー（SHOW_CONSOLE=true の時のみ出力）
    private static void ConsoleWriteLine(string message)
    {
        if (s_consoleEnabled)
        {
            Console.WriteLine(message);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 環境変数 SHOW_CONSOLE が "true" の場合のみコンソールを表示
        string showConsole = Environment.GetEnvironmentVariable("SHOW_CONSOLE") ?? "false";
        bool enableConsole = bool.TryParse(showConsole, out bool parsed) && parsed;

        if (!enableConsole)
        {
            // SHOW_CONSOLE が false の場合、既存のコンソールがあれば解放する
            // （dotnet run で実行した場合など、.NET CLI が割り当てたコンソールを閉じる）
            try
            {
                FreeConsole();
            }
            catch
            {
                // コンソール解放に失敗してもアプリは継続
            }
        }
        else
        {
            // SHOW_CONSOLE が true の場合のみコンソールを割り当て
            try
            {
                AllocConsole();
                // 標準出力をコンソールに接続
                var stdout = GetStdHandle(STD_OUTPUT_HANDLE);
                if (stdout != IntPtr.Zero)
                {
                    // Debug.WriteLine の出力もコンソールに表示されるように設定
                    Trace.Listeners.Add(new ConsoleTraceListener());
                    s_consoleEnabled = true; // コンソール有効化フラグを立てる
                }
            }
            catch
            {
                // コンソール割り当てに失敗してもアプリは継続
            }
        }

        // 未処理例外ハンドラを早期に登録（StartupUri による MainWindow 生成前）
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            ConsoleWriteLine("🔹 App.OnStartup: 初期化開始");

            // 作業ディレクトリを確認
            string currentDir = Directory.GetCurrentDirectory();
            ConsoleWriteLine($"🔹 現在の作業ディレクトリ: {currentDir}");

            // google-sheets-key.jsonファイルの存在チェックは削除
            // （SecretsProvider経由でAzure Functionsから取得するため不要）

            // ここで StartupUri によるウィンドウ生成が行われる
            base.OnStartup(e);

            ConsoleWriteLine("🔹 App.OnStartup: 初期化完了");
        }
        catch (Exception ex)
        {
            ConsoleWriteLine($"❌ App.OnStartupで未処理の例外が発生しました: {ex.Message}");
            ConsoleWriteLine($"スタックトレース:\n{ex.StackTrace}");
            System.Windows.MessageBox.Show(
                $"アプリケーションの起動中に致命的なエラーが発生しました:\n{ex.Message}",
                "致命的なエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private void App_DispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // XAML パース時や描画初期化まわりの例外を検出してソフトウェア描画へフォールバック
        if (!s_softwareFallbackApplied && IsLikelyRenderOrXamlIssue(e.Exception))
        {
            try
            {
                s_softwareFallbackApplied = true;
                ConsoleWriteLine("⚠️ 例外を検出: ソフトウェア描画にフォールバックして再試行します");
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

                // 既存ウィンドウがない/表示されていない場合は再生成して起動継続
                if (this.MainWindow == null || !this.MainWindow.IsVisible)
                {
                    var win = new MainWindow();
                    this.MainWindow = win;
                    win.Show();
                }

                e.Handled = true;
                return;
            }
            catch (Exception retryEx)
            {
                ConsoleWriteLine($"❌ ソフトウェア描画での再試行に失敗: {retryEx.Message}");
                // 続行して通常のダイアログを表示
            }
        }

        // NullReferenceException はポップアップを表示せずログのみ（本番でのノイズ抑止）
        if (e.Exception is NullReferenceException)
        {
            TryLogUnhandled("NullReferenceException suppressed (DispatcherUnhandledException)", e.Exception);
            e.Handled = true; // 継続
            return;
        }

        // それ以外はポップアップ表示して終了
        System.Windows.MessageBox.Show(
            $"未処理の例外が発生しました:\n{e.Exception.Message}",
            "エラー",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private static bool IsLikelyRenderOrXamlIssue(Exception ex)
    {
        // XamlParseException はもちろん、描画系の初期化失敗に言及する例外メッセージを簡易判定
        if (ex is System.Windows.Markup.XamlParseException)
        {
            return true;
        }

        string msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
        return msg.Contains("d3d") ||
               msg.Contains("milcore") ||
               msg.Contains("visual") ||
               msg.Contains("render") ||
               (ex.InnerException != null && IsLikelyRenderOrXamlIssue(ex.InnerException));
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        string message = ex != null ? ex.Message : e.ExceptionObject?.ToString() ?? "不明なエラー";

        // NullReferenceException はポップアップを表示せずログのみ
        if (ex is NullReferenceException)
        {
            TryLogUnhandled("NullReferenceException suppressed (CurrentDomain_UnhandledException)", ex);
            Environment.Exit(1);
            return;
        }

        // それ以外はポップアップ表示して終了
        System.Windows.MessageBox.Show(
            $"致命的なエラーが発生しました:\n{message}",
            "致命的エラー",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Environment.Exit(1);
    }

    // 【v28.4】日付ごとのファイル分割に対応
    private static void TryLogUnhandled(string prefix, Exception? ex)
    {
        try
        {
            string logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Karustep", "Logs");
            if (!System.IO.Directory.Exists(logDir))
            {
                System.IO.Directory.CreateDirectory(logDir);
            }
            // 【v28.4】日付ごとのファイル分割
            string logFilePath = System.IO.Path.Combine(logDir, $"app_error_{DateTime.Now:yyyyMMdd}.log");
            var lines = new System.Collections.Generic.List<string>();
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{prefix}] {ex?.GetType().FullName}: {ex?.Message}");
            if (!string.IsNullOrEmpty(ex?.StackTrace))
            {
                lines.Add(ex!.StackTrace!);
            }
            System.IO.File.AppendAllText(logFilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        catch
        {
            // ログに失敗してもアプリは継続/終了処理を続行
        }
    }
}