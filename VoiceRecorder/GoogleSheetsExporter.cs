using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Linqを追加
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;

namespace VoiceRecorder
{
    public static class GoogleSheetsExporter
    {
        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        private static readonly string SpreadsheetId = CredentialsProvider.GetGoogleSpreadsheetId();
        private static readonly string SheetName = CredentialsProvider.GetGoogleSheetName();
        private static SheetsService? _service;

        // 存在する年月シート名をキャッシュ
        private static readonly HashSet<string> _existingYearMonthSheets = new HashSet<string>();

        // 静的コンストラクタでの即時初期化は行わない（初回利用時に遅延初期化）

        // 明示的な再初期化（必要に応じて使用可能だが、即時取得は行わない）
        public static void RefreshService()
        {
            _service = null;
        }

        private static void InitializeService()
        {
            try
            {
                // SecretsProviderからJSON文字列として安全に取得（メモリ+DPAPIキャッシュ）
                string credentialsJson = SecretsProvider.GetGoogleSheetsKeyJsonAsync().GetAwaiter().GetResult();
                GoogleCredential credential = GoogleCredential.FromJson(credentialsJson).CreateScoped(Scopes);

                _service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "telephoneAI"
                });

                // Homeシートの関数設定は使用しない（直接書き込みに変更）
                _ = Task.Run(async () => await CleanupHomeSheetAsync());

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Google Sheets service initialization failed: {ex.Message}");
                throw;
            }
        }

        // 初回利用時の遅延初期化
        private static void EnsureService()
        {
            if (_service == null)
            {
                InitializeService();
            }
        }

        // HomeシートのA2セルに年月シートを参照する関数を設定するメソッド
        private static async Task SetHomeSheetFormulaAsync()
        {
            if (_service == null)
            {
                throw new InvalidOperationException("Google Sheets service not initialized");
            }

            try
            {
                // 現在の年月からシート名を生成 (例: "2025年04月")
                string yearMonthSheetName = DateTime.Now.ToString("yyyy年MM月");
                // スピル先に既存データがあると#REF!になるため、A2:Hを明示的にクリア
                try
                {
                    var clearReq = new ClearValuesRequest();
                    string rangeToClear = $"'{SheetName}'!A2:H";
                    await _service.Spreadsheets.Values.Clear(clearReq, SpreadsheetId, rangeToClear).ExecuteAsync();
                }
                catch (Exception clearEx)
                {
                    Console.WriteLine($"Warning: failed to clear Home sheet before setting formula: {clearEx.Message}");
                }
                string formula = $"=QUERY('{yearMonthSheetName}'!A:H, \"SELECT * WHERE A IS NOT NULL ORDER BY A DESC, B DESC\", 0)";
                
                var valueRange = new ValueRange
                {
                    Values = new List<IList<object>> { new List<object> { formula } }
                };

                var updateRequest = _service.Spreadsheets.Values.Update(
                    valueRange,
                    SpreadsheetId,
                    $"'{SheetName}'!A2"
                );
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await updateRequest.ExecuteAsync();
                Console.WriteLine($"Home sheet A2 formula set to: {formula}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting Home sheet formula: {ex.Message}");
            }
        }

        /// <summary>
        /// Homeシートの行数をチェックし、指定された上限を超えた場合に古い行を削除する
        /// </summary>
        private static async Task CleanupHomeSheetAsync()
        {
            if (_service == null)
            {
                throw new InvalidOperationException("Google Sheets service not initialized");
            }

            const int MAX_HOME_ROWS = 400; // 400行に設定

            try
            {
                var spreadsheetForHome = await _service.Spreadsheets.Get(SpreadsheetId).ExecuteAsync();
                var homeSheet = spreadsheetForHome.Sheets.FirstOrDefault(s => s.Properties.Title == SheetName);

                if (homeSheet?.Properties?.SheetId != null && homeSheet.Properties.GridProperties?.RowCount > MAX_HOME_ROWS)
                {
                    // ヘッダー行 (1行目) を考慮し、データ行は2行目から始まるため、StartIndex は MAX_HOME_ROWS + 1
                    // ただし、0-indexed なのでそのまま MAX_HOME_ROWS が501行目を指す
                    int rowsToDelete = (int)homeSheet.Properties.GridProperties.RowCount - MAX_HOME_ROWS;
                    var deleteRequest = new Request
                    {
                        DeleteDimension = new DeleteDimensionRequest
                        {
                            Range = new DimensionRange
                            {
                                SheetId = homeSheet.Properties.SheetId,
                                Dimension = "ROWS",
                                StartIndex = MAX_HOME_ROWS, // 401行目から削除 (0-indexed)
                                EndIndex = (int)homeSheet.Properties.GridProperties.RowCount // 最終行まで
                            }
                        }
                    };
                    var deleteBatchUpdateReq = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = new List<Request> { deleteRequest }
                    };
                    await _service.Spreadsheets.BatchUpdate(deleteBatchUpdateReq, SpreadsheetId).ExecuteAsync();
                    Console.WriteLine($"Deleted {rowsToDelete} rows from Home sheet (keeping {MAX_HOME_ROWS} most recent entries).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up Home sheet: {ex.Message}");
            }
        }

         public static async Task ExportAsync(string day, string time, string whoField, string summaryContent)
         {
             EnsureService();
             if (_service == null)
             {
                 throw new InvalidOperationException("Google Sheets service not initialized");
             }

             string pcName = CredentialsProvider.GetPcName();

             // fact, assessment, todoの内容を抽出
             var (fact, assessment, todo) = ExtractContent(summaryContent);

             // 新しいデータ行を作成 (両方のシートで共通) - PC列を追加
             var newRow = new List<object>
             {
                 day,
                 time,
                 pcName,    // C列にPC名を追加（動的に取得）
                 whoField,  // D列（元C列）にwho
                 fact,      // E列（元D列）にFactの内容
                 assessment,// F列（元E列）にAssessmentの内容
                 todo,      // G列（元F列）にToDoの内容
                 $"{fact}\n\n{assessment}\n\n{todo}" // H列（元G列）に全内容を結合
             };
             // Append用のValueRange (新しい行のみを含む)
             var valueRangeForAppend = new ValueRange { Values = new List<IList<object>> { newRow } };


             // --- Homeシートへの挿入処理（関数化により削除） ---
             // Homeシートは、年月シートの最新データを参照する関数で自動更新されるため、
             // アプリケーションからの直接的なデータ挿入処理は不要となりました。
             // 詳細については、InitializeService() の後に設定されるHomeシートのA2セルの関数を参照してください。
            
            // --- 1. Homeシート 2行目への挿入と書き込み ---
            try
            {
                var spreadsheetForHome = await _service.Spreadsheets.Get(SpreadsheetId).ExecuteAsync();
                var homeSheet = spreadsheetForHome.Sheets.FirstOrDefault(s => s.Properties.Title == SheetName);
                if (homeSheet?.Properties?.SheetId != null)
                {
                    var insertRowReq = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = new List<Request>
                        {
                            new Request
                            {
                                InsertDimension = new InsertDimensionRequest
                                {
                                    Range = new DimensionRange
                                    {
                                        SheetId = homeSheet.Properties.SheetId,
                                        Dimension = "ROWS",
                                        StartIndex = 1,
                                        EndIndex = 2
                                    },
                                    InheritFromBefore = false
                                }
                            }
                        }
                    };
                    await _service.Spreadsheets.BatchUpdate(insertRowReq, SpreadsheetId).ExecuteAsync();

                    var valueRangeForHome = new ValueRange { Values = new List<IList<object>> { newRow } };
                    var updateHomeRequest = _service.Spreadsheets.Values.Update(
                        valueRangeForHome,
                        SpreadsheetId,
                        $"'{SheetName}'!A2:H2"
                    );
                    updateHomeRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                    await updateHomeRequest.ExecuteAsync();
                    Console.WriteLine("Inserted a new row into Home sheet at A2:H2.");
                }
                else
                {
                    Console.WriteLine($"Home sheet not found: '{SheetName}'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting into Home sheet: {ex.Message}");
            }

            // --- 2. 年月シートへの追加処理 (シート自動生成機能付き) ---
             try
             {
                 // 現在の年月からシート名を生成 (例: "2025年04月")
                 string yearMonthSheetName = DateTime.Now.ToString("yyyy年MM月");
                 bool sheetCreated = false;

                 // キャッシュに存在するか、またはAPIで確認して存在すれば追加
                 bool sheetExists = _existingYearMonthSheets.Contains(yearMonthSheetName);
                 if (!sheetExists)
                 {
                     var spreadsheet = await _service.Spreadsheets.Get(SpreadsheetId).ExecuteAsync();
                     sheetExists = spreadsheet.Sheets.Any(s => s.Properties.Title == yearMonthSheetName);
                     if (sheetExists) _existingYearMonthSheets.Add(yearMonthSheetName);
                 }

                 // シートが存在しない場合、作成する
                 if (!sheetExists)
                 {
                     Console.WriteLine($"Sheet '{yearMonthSheetName}' not found. Creating new sheet...");
                     var addSheetRequest = new AddSheetRequest
                     {
                         Properties = new SheetProperties
                         {
                             Title = yearMonthSheetName,
                             Index = 1 // 挿入位置を2番目 (Homeシートの右隣) に指定
                         }
                     };
                     var batchUpdateReq = new BatchUpdateSpreadsheetRequest
                     {
                         Requests = new List<Request> { new Request { AddSheet = addSheetRequest } }
                     };

                     // シート作成を実行し、レスポンスから新しいシートIDを取得 (より確実な方法)
                     var batchUpdateResponse = await _service.Spreadsheets.BatchUpdate(batchUpdateReq, SpreadsheetId).ExecuteAsync();
                     // Note: レスポンスから新シートIDを取得する正確な方法はAPIバージョンやライブラリにより異なる場合があるため、
                     // ここでは再度GetしてシートIDを取得するロバストな方法を採用します。
                     _existingYearMonthSheets.Add(yearMonthSheetName); // キャッシュに追加

                     Console.WriteLine($"Sheet '{yearMonthSheetName}' created successfully.");
                     sheetCreated = true;

                     // --- 書式コピー処理を追加 ---
                     try
                     {
                         // 再度スプレッドシート情報を取得して、Homeシートと新シートのIDを取得
                         var updatedSpreadsheet = await _service.Spreadsheets.Get(SpreadsheetId).ExecuteAsync();
                         var homeSheet = updatedSpreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == SheetName);
                         var newSheet = updatedSpreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == yearMonthSheetName);

                         if (homeSheet?.Properties?.SheetId != null && newSheet?.Properties?.SheetId != null)
                         {
                             var copyPasteRequest = new CopyPasteRequest 
                                 {
                                     Source = new GridRange 
                                     {
                                         SheetId = homeSheet.Properties.SheetId,
                                         StartRowIndex = 0,
                                         EndRowIndex = 1,
                                         StartColumnIndex = 0,
                                         EndColumnIndex = 8
                                     }, // HomeシートのA1:H1
                                     Destination = new GridRange 
                                     {
                                         SheetId = newSheet.Properties.SheetId,
                                         StartRowIndex = 0,
                                         EndRowIndex = 1,
                                         StartColumnIndex = 0,
                                         EndColumnIndex = 8
                                     }, // 新しいシートのA1:H1
                                     PasteType = "PASTE_FORMAT" // 書式のみコピー
                                 };
                             var formatBatchUpdateReq = new BatchUpdateSpreadsheetRequest
                             {
                                 Requests = new List<Request> { new Request { CopyPaste = copyPasteRequest } }
                             };
                             await _service.Spreadsheets.BatchUpdate(formatBatchUpdateReq, SpreadsheetId).ExecuteAsync();
                             Console.WriteLine($"Format copied from '{SheetName}' to '{yearMonthSheetName}'.");

                             // --- 固定行/列と列幅の設定を追加 ---
                             var updatePropertiesReq = new BatchUpdateSpreadsheetRequest
                             {
                                 Requests = new List<Request>
                                 {
                                     // 1行目を固定行、A-C列を固定列に設定
                                     new Request
                                     {
                                         UpdateSheetProperties = new UpdateSheetPropertiesRequest
                                         {
                                             Properties = new SheetProperties
                                             {
                                                 SheetId = newSheet.Properties.SheetId,
                                                 GridProperties = new GridProperties
                                                 {
                                                     FrozenRowCount = 1,  // 1行目を固定
                                                     FrozenColumnCount = 4 // A-D列を固定
                                                 }
                                             },
                                             Fields = "gridProperties.frozenRowCount,gridProperties.frozenColumnCount"
                                         }
                                     },
                                     // 列幅の設定 (A列:75, B列:40, C列:70, D-G列:410)
                                     new Request
                                     {
                                         UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                                         {
                                             Range = new DimensionRange
                                             {
                                                 SheetId = newSheet.Properties.SheetId,
                                                 Dimension = "COLUMNS",
                                                 StartIndex = 0, // A列
                                                 EndIndex = 1    // A列のみ
                                             },
                                             Properties = new DimensionProperties
                                             {
                                                 PixelSize = 75
                                             },
                                             Fields = "pixelSize"
                                         }
                                     },
                                     new Request
                                     {
                                         UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                                         {
                                             Range = new DimensionRange
                                             {
                                                 SheetId = newSheet.Properties.SheetId,
                                                 Dimension = "COLUMNS",
                                                 StartIndex = 1, // B列
                                                 EndIndex = 2     // B列のみ
                                             },
                                             Properties = new DimensionProperties
                                             {
                                                 PixelSize = 40
                                             },
                                             Fields = "pixelSize"
                                         }
                                     },
                                     new Request
                                     {
                                         UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                                         {
                                             Range = new DimensionRange
                                             {
                                                 SheetId = newSheet.Properties.SheetId,
                                                 Dimension = "COLUMNS",
                                                 StartIndex = 2, // C列
                                                 EndIndex = 3     // C列のみ
                                             },
                                             Properties = new DimensionProperties
                                             {
                                                 PixelSize = 70
                                             },
                                             Fields = "pixelSize"
                                         }
                                     },
                                     new Request
                                     {
                                         UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                                         {
                                             Range = new DimensionRange
                                             {
                                                 SheetId = newSheet.Properties.SheetId,
                                                 Dimension = "COLUMNS",
                                                 StartIndex = 3, // D列（元C列）
                                                 EndIndex = 8     // D-H列（元D-G列）
                                             },
                                             Properties = new DimensionProperties
                                             {
                                                 PixelSize = 410
                                             },
                                             Fields = "pixelSize"
                                         }
                                     }
                                 }
                             };
                             await _service.Spreadsheets.BatchUpdate(updatePropertiesReq, SpreadsheetId).ExecuteAsync();
                             Console.WriteLine($"Set frozen rows/columns and column widths for '{yearMonthSheetName}'.");
                             // --- 固定行/列と列幅の設定ここまで ---
                         }
                         else
                         {
                             Console.WriteLine($"Could not find Sheet IDs for format copy ('{SheetName}' or '{yearMonthSheetName}').");
                         }
                     }
                     catch (Exception formatEx)
                     {
                         Console.WriteLine($"Error copying format: {formatEx.Message}");
                         // 書式コピーが失敗してもヘッダー追加とデータ追記は試行する
                     }
                     // --- 書式コピー処理ここまで ---


                     // ヘッダー行を追加 (書式コピー後に実行)
                     var headerRow = new List<object> { "day", "time", "PC", "who", "Fact", "Assessment", "ToDo", "All" }; // PC列を追加
                     var headerValueRange = new ValueRange { Values = new List<IList<object>> { headerRow } };
                     var appendHeaderRequest = _service.Spreadsheets.Values.Append(
                         headerValueRange,
                         SpreadsheetId,
                         $"'{yearMonthSheetName}'!A1:H1" // G1からH1に変更
                     );
                     appendHeaderRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                     await appendHeaderRequest.ExecuteAsync();
                     Console.WriteLine($"Header added to sheet '{yearMonthSheetName}'.");
                 }

                 // 年月シートにデータを追加 (Append)
                 var appendDataRequest = _service.Spreadsheets.Values.Append(
                     valueRangeForAppend, // 追加するデータはnewRowのみ
                     SpreadsheetId,
                     $"'{yearMonthSheetName}'!A:H"
                 );
                 appendDataRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

                 var responseYearMonth = await appendDataRequest.ExecuteAsync();
                 string action = sheetCreated ? "created and data appended" : "data appended";
                 Console.WriteLine($"Sheet '{yearMonthSheetName}' {action}. Updated range: {responseYearMonth.Updates.UpdatedRange}");
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"Error processing Google Sheet '{DateTime.Now:yyyy年MM月}': {ex.Message}");
             }
        }

        /// <summary>
        /// 要約ファイルからFact、Assessment、ToDoを抽出
        /// </summary>
        private static (string fact, string assessment, string todo) ExtractContent(string content)
        {
            string factPattern = @"fact\[([\s\S]+?)\]";
            string assessmentPattern = @"assessment\[([\s\S]+?)\]";
            string todoPattern = @"todo\[([\s\S]+?)\]";

            var factMatch = Regex.Match(content, factPattern);
            var assessmentMatch = Regex.Match(content, assessmentPattern);
            var todoMatch = Regex.Match(content, todoPattern);

            string fact = factMatch.Success ? factMatch.Groups[1].Value.Trim() : "未設定";
            string assessment = assessmentMatch.Success ? assessmentMatch.Groups[1].Value.Trim() : "未設定";
            string todo = todoMatch.Success ? todoMatch.Groups[1].Value.Trim() : "未設定";

            return (fact, assessment, todo);
        }
    }
}