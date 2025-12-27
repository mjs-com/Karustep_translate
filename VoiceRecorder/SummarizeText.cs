using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetEnv;

namespace VoiceRecorder
{
    public class SummarizeText
    {
        // 静的なHttpClientインスタンスを宣言
        private static readonly HttpClient httpClient;

        // 静的コンストラクタでHttpClientを初期化
        static SummarizeText()
        {
            httpClient = new HttpClient(
                new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 10, // 同時接続数を最大10に設定
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5) // 接続の再利用期間を5分に設定
                })
            {
                Timeout = TimeSpan.FromSeconds(300) // タイムアウト設定を180→300秒に延長
            };
            // プロキシ相性問題を回避するため100-continueを無効化
            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            httpClient.DefaultRequestHeaders.Add("api-key", CredentialsProvider.GetAzureOpenAIApiKey()); // 静的コンストラクタでAPIキーを設定
        }

        /// <summary>
        /// 軽量POSTでAzure OpenAIのDNS/TLS/接続プールを温める（録音中に先行実行）
        /// </summary>
        public static async Task WarmUpAsync()
        {
            try
            {
                string azureEndpoint  = GetAzureEndpoint();
                string deploymentName = GetDeploymentName();

                if (!Uri.TryCreate(azureEndpoint, UriKind.Absolute, out var _))
                    return;

                // 新バージョンv1 API: /openai/v1/chat/completions
                string endpoint = $"{azureEndpoint}/openai/v1/chat/completions";

                var requestBody = new { model = deploymentName, messages = new[] { new { role = "system", content = "warmup" }, new { role = "user", content = "ping" } }, max_tokens = 1, temperature = 0.0 };
                string jsonRequest = JsonSerializer.Serialize(requestBody);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var _ = await httpClient.PostAsync(endpoint, new StringContent(jsonRequest, Encoding.UTF8, "application/json"), cts.Token);
            }
            catch { /* ベストエフォート */ }
        }

        // ──────────────────────────────────────────
        // 環境変数からAzure OpenAIの構成を読み込む
        // ──────────────────────────────────────────
        private static string GetAzureEndpoint()
        {
            return CredentialsProvider.GetAzureOpenAIEndpoint();
        }

        private static string GetDeploymentName()
        {
            return CredentialsProvider.GetAzureOpenAIDeploymentName();
        }


        private static string GetApiKey()
        {
            return CredentialsProvider.GetAzureOpenAIApiKey();
        }

        public static async Task<(string Summary, long RagProcessingTimeMs, string RagQueryText, string RagContext)> SummarizeAsync(string textFilePath)
        {
            // 環境変数の読み込み
            string azureEndpoint  = GetAzureEndpoint();
            string deploymentName = GetDeploymentName();

            // エンドポイント検証（HTTPS必須）
            if (!Uri.TryCreate(azureEndpoint, UriKind.Absolute, out var azureUri) || azureUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("❌ 無効なAzureエンドポイント（HTTPS必須）");

            // 1) ファイル存在チェック
            if (string.IsNullOrEmpty(textFilePath) || !File.Exists(textFilePath))
                throw new ArgumentException("❌ 入力テキストファイルのパスが無効です。");

            Console.WriteLine($"📄 要約開始: {textFilePath}");

            // 2) 入力テキスト
            string inputText  = await File.ReadAllTextAsync(textFilePath);
            
            // 日付計算のための基準日を明示的に追加（AIが「1週間前」等を計算できるように）
            string todayDate = DateTime.Now.ToString("yyyy年MM月dd日");
            string dateHeader = $"【本日の日付（計算基準日）】{todayDate}\n" +
                                $"※「1週間前」「3日前」「1ヶ月前」などの相対日付は、上記の日付から逆算して具体的な年月日を算出してください。\n\n";
            inputText = dateHeader + inputText;
            string promptPath = MainWindow.CurrentSelectedPrompt;
            if (!File.Exists(promptPath))
                throw new FileNotFoundException($"❌ システムプロンプトが見つかりません: {promptPath}");
            string systemPrompt = await File.ReadAllTextAsync(promptPath);

            // 3) 辞書読み込み
            const string dictionaryFile = "dictionary.txt";
            if (!File.Exists(dictionaryFile))
                throw new FileNotFoundException($"❌ 辞書ファイルが見つかりません: {dictionaryFile}");
            var sbDict = new StringBuilder();
            foreach (var line in await File.ReadAllLinesAsync(dictionaryFile))
            {
                var l = line.Trim();
                if (string.IsNullOrEmpty(l) || !l.Contains("→"))
                {
                    if (!string.IsNullOrEmpty(l)) Console.WriteLine($"⚠️ 無効な辞書行: {l}");
                    continue;
                }
                sbDict.AppendLine(l);
            }

            // 4) RAGは無効化（処理時間短縮のため実行しない）
            long ragProcessingTimeMs = 0;
            string ragQueryText = "";
            string ragContext = "※RAG情報なし※";

            // 5) プロンプト差し込み
            systemPrompt = systemPrompt
                .Replace("[[DICTIONARY_PLACEHOLDER]]", sbDict.ToString())
                .Replace("[[RAG_PLACEHOLDER]]", "※RAG情報なし※");

#if DEBUG
            // デバッグ情報（機微情報は出さない）
            Console.WriteLine($"🔍 デバッグ情報:");
            Console.WriteLine($"🔹 エンドポイント: {azureEndpoint}/openai/v1/chat/completions");
#endif

            // 6) 新バージョンv1 API: /openai/v1/chat/completions（api-version不要）
            string endpoint = $"{azureEndpoint}/openai/v1/chat/completions";

            var requestBody = new
            {
                model = deploymentName,  // デプロイ名とモデル名を同一運用とし、両者に同じ値を使用
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = inputText    }
                },
                temperature = 0.3, // ★ 再現性重視のため低めに設定
                top_p = 0.95       // ★ 多様性を少し持たせる
            };
            string jsonRequest = JsonSerializer.Serialize(requestBody);
            
            
            HttpResponseMessage? response = null;
            int maxRetries = 3;
            int attempt = 0;
            
            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    response = await httpClient.PostAsync(
                        endpoint,
                        new StringContent(jsonRequest, Encoding.UTF8, "application/json")
                    );
                    break; // 成功したらループを抜ける
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"⚠️ LLMリクエストタイムアウト (試行 {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * attempt); // 指数バックオフ
                        continue;
                    }
                    throw;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"⚠️ LLMリクエストエラー (試行 {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * attempt); // 指数バックオフ
                        continue;
                    }
                    throw;
                }
            }
            
            if (response == null)
            {
                throw new Exception("LLMリクエストが最大試行回数に達しました");
            }

            // レスポンスヘッダーを出力
#if DEBUG
            Console.WriteLine($"🔹 レスポンスステータス: {response.StatusCode}");
            Console.WriteLine($"🔹 レスポンスヘッダー:");
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"   - {header.Key}: {string.Join(", ", header.Value)}");
            }
#endif

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                
#if DEBUG
                Console.WriteLine($"🔹 エラーレスポンス本文: {err}");
#endif
                
                // 詳細なエラー情報を解析して表示
                try
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(err);
                    if (errorJson.TryGetProperty("error", out var errorObj))
                    {
#if DEBUG
                        string code = errorObj.TryGetProperty("code", out var codeElem) ? codeElem.GetString() ?? "不明" : "不明";
                        string message = errorObj.TryGetProperty("message", out var msgElem) ? msgElem.GetString() ?? "不明" : "不明";
                        Console.WriteLine($"🔹 詳細エラー情報: コード={code}, メッセージ={message}");
#endif
                    }
                }
                catch (Exception)
                {
#if DEBUG
                    Console.WriteLine($"🔹 エラーJSONの解析に失敗");
#endif
                }
                
                throw new Exception($"❌ Azure OpenAI API エラー: {response.StatusCode} - {err}");
            }

            var responseBody = await response!.Content.ReadAsStringAsync();
            
#if DEBUG
            Console.WriteLine($"🔹 レスポンス本文: {responseBody.Substring(0, Math.Min(500, responseBody.Length))}...");
#endif
            JsonElement json;
            try
            {
                json = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("JSONの解析に失敗しました", ex);
            }
            if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                throw new InvalidOperationException("choicesが見つかりません");
            
            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var messageObj))
                throw new InvalidOperationException("messageが見つかりません");
            
            if (!messageObj.TryGetProperty("content", out var content))
                throw new InvalidOperationException("contentが見つかりません");
            
            string summary = content.GetString() ?? throw new InvalidOperationException("要約の取得に失敗しました");
            
            // todoセクションの閉じ括弧 "]" が欠けている場合に追加
            if (summary.Contains("todo[") && summary.LastIndexOf("todo[", StringComparison.Ordinal) > summary.LastIndexOf("]", StringComparison.Ordinal))
            {
                summary += "]";
                
#if DEBUG
                Console.WriteLine("⚠️ todoセクションの閉じ括弧を自動追加しました");
#endif
            }

            
#if DEBUG
            Console.WriteLine($"✅ 要約完了:\n{summary}");
#endif

            // 7) 保存は行わず、要約文字列とRAG処理時間を返す
            // string summaryPath = Path.ChangeExtension(textFilePath, ".summary.txt");
            // await File.WriteAllTextAsync(summaryPath, summary);
            
            // #if DEBUG
            // Console.WriteLine($"💾 要約データ保存: {summaryPath}");
            // #endif

            return (summary, ragProcessingTimeMs, ragQueryText, ragContext);
        }

        /// <summary>
        /// v30.0: 結合済みテキスト（事前情報＋文字起こし）を受け取って要約する
        /// 再生成機能でも使用される
        /// </summary>
        /// <param name="combinedText">事前情報と文字起こしを結合したテキスト</param>
        /// <param name="systemPromptPath">使用するシステムプロンプトのパス</param>
        /// <returns>要約結果</returns>
        public static async Task<(string Summary, long RagProcessingTimeMs, string RagQueryText, string RagContext)> 
            SummarizeFromCombinedTextAsync(string combinedText, string systemPromptPath)
        {
            // 環境変数の読み込み
            string azureEndpoint  = GetAzureEndpoint();
            string deploymentName = GetDeploymentName();

            // エンドポイント検証（HTTPS必須）
            if (!Uri.TryCreate(azureEndpoint, UriKind.Absolute, out var azureUri) || azureUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("❌ 無効なAzureエンドポイント（HTTPS必須）");

            // 入力テキストチェック
            if (string.IsNullOrWhiteSpace(combinedText))
                throw new ArgumentException("❌ 入力テキストが空です。");

            Console.WriteLine($"📄 要約開始（結合テキスト）: {combinedText.Length} 文字");

            // 日付計算のための基準日を明示的に追加
            string todayDate = DateTime.Now.ToString("yyyy年MM月dd日");
            string dateHeader = $"【本日の日付（計算基準日）】{todayDate}\n" +
                                $"※「1週間前」「3日前」「1ヶ月前」などの相対日付は、上記の日付から逆算して具体的な年月日を算出してください。\n\n";
            string inputText = dateHeader + combinedText;

            // システムプロンプト読み込み（引数で指定されたパスを使用）
            if (!File.Exists(systemPromptPath))
                throw new FileNotFoundException($"❌ システムプロンプトが見つかりません: {systemPromptPath}");
            string systemPrompt = await File.ReadAllTextAsync(systemPromptPath);

            // 辞書読み込み
            const string dictionaryFile = "dictionary.txt";
            if (!File.Exists(dictionaryFile))
                throw new FileNotFoundException($"❌ 辞書ファイルが見つかりません: {dictionaryFile}");
            var sbDict = new StringBuilder();
            foreach (var line in await File.ReadAllLinesAsync(dictionaryFile))
            {
                var l = line.Trim();
                if (string.IsNullOrEmpty(l) || !l.Contains("→"))
                {
                    if (!string.IsNullOrEmpty(l)) Console.WriteLine($"⚠️ 無効な辞書行: {l}");
                    continue;
                }
                sbDict.AppendLine(l);
            }

            // RAGは無効化
            long ragProcessingTimeMs = 0;
            string ragQueryText = "";
            string ragContext = "※RAG情報なし※";

            // プロンプト差し込み
            systemPrompt = systemPrompt
                .Replace("[[DICTIONARY_PLACEHOLDER]]", sbDict.ToString())
                .Replace("[[RAG_PLACEHOLDER]]", "※RAG情報なし※");

#if DEBUG
            Console.WriteLine($"🔍 デバッグ情報（結合テキスト要約）:");
            Console.WriteLine($"🔹 エンドポイント: {azureEndpoint}/openai/v1/chat/completions");
            Console.WriteLine($"🔹 システムプロンプト: {Path.GetFileName(systemPromptPath)}");
#endif

            // API呼び出し
            string endpoint = $"{azureEndpoint}/openai/v1/chat/completions";

            var requestBody = new
            {
                model = deploymentName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = inputText    }
                },
                temperature = 0.3,
                top_p = 0.95
            };
            string jsonRequest = JsonSerializer.Serialize(requestBody);

            HttpResponseMessage? response = null;
            int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    response = await httpClient.PostAsync(
                        endpoint,
                        new StringContent(jsonRequest, Encoding.UTF8, "application/json")
                    );
                    break;
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"⚠️ LLMリクエストタイムアウト (試行 {attempt}/{maxRetries}): {ex.Message}");
                    await Task.Delay(1000 * attempt);
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    Console.WriteLine($"⚠️ LLMリクエストエラー (試行 {attempt}/{maxRetries}): {ex.Message}");
                    await Task.Delay(1000 * attempt);
                }
            }

            if (response == null)
            {
                throw new Exception("LLMリクエストが最大試行回数に達しました");
            }

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                throw new Exception($"❌ Azure OpenAI API エラー: {response.StatusCode} - {err}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            JsonElement json;
            try
            {
                json = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("JSONの解析に失敗しました", ex);
            }

            if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                throw new InvalidOperationException("choicesが見つかりません");

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var messageObj))
                throw new InvalidOperationException("messageが見つかりません");

            if (!messageObj.TryGetProperty("content", out var content))
                throw new InvalidOperationException("contentが見つかりません");

            string summary = content.GetString() ?? throw new InvalidOperationException("要約の取得に失敗しました");

            // todoセクションの閉じ括弧 "]" が欠けている場合に追加
            if (summary.Contains("todo[") && summary.LastIndexOf("todo[", StringComparison.Ordinal) > summary.LastIndexOf("]", StringComparison.Ordinal))
            {
                summary += "]";
#if DEBUG
                Console.WriteLine("⚠️ todoセクションの閉じ括弧を自動追加しました");
#endif
            }

#if DEBUG
            Console.WriteLine($"✅ 要約完了（結合テキスト）:\n{summary}");
#endif

            return (summary, ragProcessingTimeMs, ragQueryText, ragContext);
        }

        // RAG関連の補助関数は削除（処理時間短縮のため未使用）
    }
}
