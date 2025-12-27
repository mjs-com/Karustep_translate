# Windows版アプリシステム詳細分析_v28.3

現在のWindows版「VoiceRecorder」アプリケーションのシステム構成を、フロントエンド、バックエンド、データベースの観点から詳細に分析しました。

## 1. システム概要
*   **開発言語**: C#
*   **ターゲットフレームワーク**: .NET 8.0 (`net8.0-windows`)
    *   `.NET 8` 自体はクロスプラットフォームですが、`-windows` ターゲットを使用しており、Windows固有のAPIやライブラリに強く依存しています。
*   **アーキテクチャ**: モノリシックなデスクトップアプリケーション（WPF）
*   **ランタイム識別子**: `win-x64`
*   **パッケージ形式**: 単一ファイル発行 (`PublishSingleFile`)

---

## 2. フロントエンド (UI/UX)
**構成: WPF (Windows Presentation Foundation)**
Windows専用のUIフレームワークであるWPFを使用しています。XAMLによる宣言的なUI定義と、分離コード（C#）によるロジックで構成されています。

### 主要ファイル
| ファイル | 役割 |
|---------|------|
| `MainWindow.xaml` / `.cs` | メイン画面（録音操作、要約表示、セッション管理） |
| `ApiKeySettingsWindow.xaml` / `.cs` | API設定画面 |
| `DictionaryEditorWindow.xaml` / `.cs` | 誤認識修正辞書の編集画面 |
| `SystemPromptEditorWindow.xaml` / `.cs` | システムプロンプト編集画面 |
| `App.xaml` / `.cs` | アプリケーションエントリポイント、例外ハンドリング |

### 特徴的な実装とWindows依存性
UI層において、単なる画面表示以上にOSレベルの操作をWindows API (Win32 API / `user32.dll`) を通じて行っています。

#### 操作トリガー（ホットキー/フットスイッチ）
*   **グローバルホットキー**: `RegisterHotKey` と `WM_HOTKEY` メッセージフックを使用。アプリが非アクティブでも動作します。
    *   録音開始/停止: `Ctrl + Shift + ,` (Comma)
    *   一時停止: `Ctrl + Shift + .` (Period)
    *   コピー＆貼り付け実行: `Alt + Ctrl + Shift + ,` または `Win + Ctrl + Shift + ,`（設定で切替可能）
*   **フットスイッチ連携**: アプリ側に専用のデバイスドライバ実装はありません。フットスイッチ側で上記のキーコンビネーション（例: `Ctrl+Shift+,`）を出力するように設定し、キーボード入力として処理する設計です。

#### 他アプリケーション操作 (Automation)
*   **自動貼り付け機能**:
    *   **マウス操作**: `GetCursorPos` で位置を取得し、`mouse_event` でクリックをエミュレートしてフォーカスを移動。
    *   **キー送信**: `keybd_event` で `Ctrl+V` を送信してクリップボードの内容を貼り付け。
*   **ウィンドウ特定・フォーカス**: `WindowFromPoint`, `SetForegroundWindow`, `SetFocus` 等を使用。
*   **スレッドアタッチ**: `AttachThreadInput` を使用してウィンドウ間のフォーカス操作を実現。

#### 周辺ウィンドウ
*   `SystemPromptEditorWindow`: システムプロンプト（AIへの指示書）を編集。`.txt` ファイルを直接読み書きします。
*   `DictionaryEditorWindow`: 誤認識修正用の辞書を編集。`.txt` ファイルを直接読み書きします。

#### 追加のWindows API依存
*   **タスクバー点滅**: `FlashWindowEx` - 録音中のタスクバーアイコン点滅
*   **コンソール制御**: `AllocConsole`, `FreeConsole` - デバッグ用コンソール表示
*   **ソフトウェアレンダリング**: `RenderMode.SoftwareOnly` - GPU描画失敗時のフォールバック

**分析結果**: フロントエンドのコードは100% Windows固有です。

---

## 3. バックエンド (ロジック)
アプリケーションのコアロジックはC#で記述されていますが、ハードウェアアクセス部分でWindows依存が見られます。

### A. 音声録音機能
*   **ライブラリ**: `NAudio` (v2.2.1) および Windows Multimedia API (`winmm.dll` / MCI)
*   **実装詳細** (`SoundRecorder` クラス - `MainWindow.xaml.cs` 内に定義):
    *   **プライマリ**: `NAudio` の `WaveInEvent` を使用してマイク入力をキャプチャ。
        *   フォーマット: 16kHz, 16bit, モノラル
        *   **ローカル保存**: `WaveFileWriter` で1セッションにつき1つのWAVファイルに継続的に保存。
    *   **フォールバック**: `NAudio` が失敗した場合、非常に古い `mciSendString` コマンドを使用して録音を試みる冗長構成。
    *   **無音検知**: 入力波形の振幅を監視し、180秒間無音が続いた場合に自動停止するロジックが含まれています。
    *   **チャンク処理 (API送信)**: 録音中、オンメモリバッファから一定間隔でオーディオデータを切り出し、WAVヘッダを付与してリアルタイムで文字起こしAPIに送信。
         *   切り出しタイミングは無音検知 + 最小/最大時間で制御
*   **依存性**: `winmm.dll` はWindows専用です。`NAudio` も一部Windows APIに依存します。

#### パフォーマンスモード (appsettings.txt: PERFORMANCE_MODE)
録音とAPIコールのチャンク分割間隔を調整する4つのモードが存在します。
| モード | チャンク切り出し間隔 | 特徴 |
|--------|-------------------|------|
| Realtime (0) | 5〜30秒 | 無音検知あり。30秒到達または無音区間（5秒以上）で送信。 |
| Balanced (1) | 60〜90秒 | 無音検知あり。90秒到達または無音区間（60秒以上）で送信。 |
| LowLoad (2) | 300秒固定 | 無音検知による切り出しなし。5分ごとに固定で送信。 |
| UltraLowLoad (3) | 300秒固定 | 無音検知なし、UI更新もスキップ（コード上はLowLoadと同等）。 |

### B. AI処理・クラウド連携
この部分はHTTP通信が主であり、Windows固有のAPIへの依存はありません。

#### 音声認識 (STT) - `SpeechToText.cs`
*   **サービス**: Azure Cognitive Services Speech (Fast Transcription API)
*   **方式**: 録音データ（チャンク）を `multipart/form-data` 形式で REST API に送信。
    *   ※ドキュメントの一部にBase64とある場合がありますが、実際のコードは `multipart/form-data` です。
*   **エンドポイント**: `https://japaneast.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15`
*   **特徴**:
    *   **マルチパートフォームデータ**: `MultipartFormDataContent` を使用してオーディオファイルとメタデータを送信
    *   **接続プール**: `SocketsHttpHandler` で `MaxConnectionsPerServer = 10`, `PooledConnectionLifetime = 5分` を設定
    *   **ウォームアップ機能**: `WarmUpAsync()` メソッドで録音中にDNS/TLS/接続プールを事前に温める
    *   並列実行数制限（セマフォ: 最大5）
    *   リトライ機能（最大3回、指数バックオフ）

#### 要約・生成 (LLM) - `SummarizeText.cs`
*   **サービス**: Azure OpenAI Service (GPT-4o)
*   **方式**: REST API (`HttpClient`) 経由でプロンプトとテキストを送信。
*   **エンドポイント**: `{AZURE_OPENAI_ENDPOINT}/openai/v1/chat/completions`
*   **認証**: `api-key` ヘッダーを使用。
*   **特徴**:
    *   システムプロンプトにテンプレート変数 `[[DICTIONARY_PLACEHOLDER]]`, `[[RAG_PLACEHOLDER]]` を埋め込み
    *   リトライ機能（最大3回）
    *   RAG機能は無効化済み（処理時間短縮のため）

#### 外部連携 - `GoogleSheetsExporter.cs`
*   **サービス**: Google Sheets API
*   **ライブラリ**: `Google.Apis.Sheets.v4` (v1.70.0)
*   **送信データ**: 日時、PC名、患者名(who)、Fact(事実)、Assessment(評価)、Todo、全結合テキスト。
*   **特徴**:
    *   **Homeシート**: 最新データを2行目に挿入。最大400行で古いデータを自動削除するローテーション機能あり。
    *   **年月シート**: `yyyy年MM月` という名前のシートを自動生成し、データを追記（アーカイブ用）。

### C. ライセンス認証・セキュリティ

#### ハードウェアID (HWID) 生成 - `LicenseManager.cs`
*   **実装**: `System.Management` (WMI) を使用し、以下の情報を取得して結合・ハッシュ化。
    1.  **CPU ID**: `Win32_Processor.ProcessorId`
    2.  **マザーボード**: `Win32_BaseBoard.SerialNumber`
    3.  **ディスク**: `Win32_DiskDrive.SerialNumber`
*   **ハッシュ化**: SHA256でハッシュ後、16文字（4-4-4-4形式）に整形
*   **検証サーバー**: `https://karustep-license-api.onrender.com/verify`
*   **依存性**: **WMIはWindows専用**です。

#### 機密情報管理 - 二層構造

**1. APIキー管理 - `CredentialsProvider.cs` + Windows Credential Manager**
*   **ライブラリ**: `CredentialManagement` (v1.0.2)
*   **保存先**: Windows Credential Manager (資格情報マネージャー)
*   **管理キー**:
    *   `AZURE_SPEECH_KEY` - Azure Speech Service APIキー
    *   `AZURE_OPENAI_API_KEY` - Azure OpenAI APIキー
    *   `AZURE_OPENAI_ENDPOINT` - Azure OpenAI エンドポイントURL
    *   `AZURE_FUNCTIONS_KEY` - Azure Functions 認証キー
    *   `GOOGLE_SPREADSHEET_ID` - Google Spreadsheet ID
*   **依存性**: **Windows Credential Managerは完全にWindows専用**です。

**2. Google Sheets認証情報 - `SecretsProvider.cs` + DPAPI**
*   **実装**: `System.Security.Cryptography.ProtectedData` (DPAPI) を使用
*   **取得元**: Azure Functions (`GetGoogleSheetsKey`) から認証JSON文字列を取得
*   **キャッシュ**:
    *   メモリキャッシュ（7日間）
    *   ディスクキャッシュ（`%LOCALAPPDATA%\Karustep\Secrets\google-sheets-key.enc`、DPAPI暗号化）
*   **依存性**: **DPAPIはWindows専用**です。

### D. 使用NuGetパッケージ一覧
| パッケージ | バージョン | 用途 | Windows依存 |
|-----------|-----------|------|-------------|
| `NAudio` | 2.2.1 | 音声録音・処理 | **あり** (一部) |
| `Google.Apis.Sheets.v4` | 1.70.0 | Google Sheets連携 | なし |
| `System.Management` | 9.0.4 | WMI（ハードウェアID生成） | **あり** |
| `CredentialManagement` | 1.0.2 | Windows資格情報マネージャー | **あり** |
| `DotNetEnv` | 3.1.1 | 環境変数管理（.env形式） | なし |
| `System.Drawing.Common` | 8.0.8 | アイコン処理 | **あり** |

---

## 4. データベース・データ保存
明確なデータベースエンジン（SQL Server, SQLiteなど）は**使用していません**。

### データ保存方式: **ローカルファイルシステムベース**
| データ種別 | 形式 | 保存場所 |
|-----------|------|---------|
| 音声データ | `.wav` | `C:\temp\recording_{sessionId}_{timestamp}.wav` |
| 文字起こし | `.txt` | `C:\temp\recording_{sessionId}_{timestamp}.txt` |
| 要約結果 | `.summary.txt` | `C:\temp\recording_{sessionId}_{timestamp}.summary.txt` |
| 辞書 | `dictionary.txt` | アプリ実行ディレクトリ |
| プロンプト | `00. *.txt` 〜 `11. *.txt` | アプリ実行ディレクトリ |
| 選択プロンプト | `selected_prompt.txt` | アプリ実行ディレクトリ（現在選択中のプロンプトファイルパスを保存） |
| 設定 | `appsettings.txt` | アプリ実行ディレクトリ |

### 設定ファイル (`appsettings.txt`) の項目
```
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o
GOOGLE_SHEET_NAME=Home
PC_NAME=診察室①
HOTKEY_MODIFIER_KEY=Alt
SAVE_AUDIO_FILE=true
PERFORMANCE_MODE=LowLoad
```

- `PERFORMANCE_MODE` は `Realtime` / `Balanced` / `LowLoad` / `UltraLowLoad` を指定可能（現在のデフォルトは `LowLoad`）。

### キャッシュ・ログの保存場所
| データ | 場所 |
|--------|------|
| HWID | `%APPDATA%\Karustep\hwid.txt` |
| 認証キャッシュ | `%LOCALAPPDATA%\Karustep\Secrets\google-sheets-key.enc` |
| ログファイル | `%APPDATA%\Karustep\Logs\license_{date}.log` |
| エラーログ | `%LOCALAPPDATA%\Karustep\Logs\app_error.log` |

---

## 5. コンポーネント別Windows依存度

### Windows非依存コンポーネント（HTTP通信ベース）
| コンポーネント | ファイル | 備考 |
|---------------|---------|------|
| 音声認識API呼び出し | `SpeechToText.cs` | REST API (multipart/form-data) |
| 要約API呼び出し | `SummarizeText.cs` | REST API (json) |
| Google Sheets連携 | `GoogleSheetsExporter.cs` | Google.Apis.Sheets.v4 ライブラリ使用 |
| データモデル | `RecordingSession.cs` | プロパティ定義部分 |

### Windows依存コンポーネント
| コンポーネント | Windows依存要素 |
|---------------|----------------|
| **UIすべて** | WPF/XAML |
| **グローバルホットキー** | `RegisterHotKey`, `WM_HOTKEY` |
| **自動貼り付け** | `user32.dll` (mouse_event, keybd_event) |
| **録音機能** | NAudio, winmm.dll (MCI) |
| **HWID生成** | WMI (System.Management) |
| **認証情報保存** | Windows Credential Manager |
| **機密データ暗号化** | DPAPI (ProtectedData) |
| **アイコン処理** | System.Drawing.Common |

---

## 6. Windows API 使用箇所一覧

### user32.dll
```csharp
RegisterHotKey / UnregisterHotKey  // グローバルホットキー
GetCursorPos / SetCursorPos        // マウス位置
WindowFromPoint                     // ウィンドウ検出
SetForegroundWindow / SetFocus     // フォーカス制御
mouse_event                         // マウスクリック
keybd_event                         // キー入力
SendMessage / PostMessage          // ウィンドウメッセージ
FlashWindowEx                       // タスクバー点滅
AttachThreadInput                   // スレッド入力接続
GetWindowThreadProcessId           // プロセスID取得
EnumChildWindows                    // 子ウィンドウ列挙
ScreenToClient / ClientToScreen    // 座標変換
```

### kernel32.dll
```csharp
AllocConsole / FreeConsole         // コンソール制御
GetStdHandle / SetStdHandle        // 標準入出力
GetCurrentThreadId                 // スレッドID取得
```

### winmm.dll
```csharp
mciSendString                      // MCI録音（フォールバック）
mciGetErrorString                  // MCIエラー取得
```

---

## 7. アプリケーション処理フロー

### 起動〜録音〜要約の全体シーケンス
```
アプリ起動
  │
  ▼
App.xaml.cs (OnStartup)
  - 未処理例外ハンドラ登録
  - 描画系エラー時はソフトウェアレンダリングへフォールバック
  │
  ▼
MainWindow.xaml.cs (Loaded)
  - ライセンス認証 (CheckLicense)
      │
      ├─▶ LicenseManager.cs
      │     - HWID生成（WMI + SHA256）
      │     - 検証API呼び出し: GET /verify?hwid=...
      │     - 結果でUI切替（MainAppPanel / LicensePanel）
      │
      ▼ （認証OK時）
  - ホットキー登録
  - アイコン設定
  - プロンプトファイル読込（selected_prompt.txtから前回選択を復元）
  │
  ▼
START/STOP/PAUSE 操作
  │
  ├─▶ SoundRecorder（録音）
  │     - 16kHz/16bit/Mono WAVファイルへ保存
  │     - パフォーマンスモードに応じてチャンク分割 (例: 5分ごと)
  │     - ChunkReadyイベント発火
  │
  ├─▶ リアルタイム文字起こし（録音中）
  │     - ChunkReadyイベント受信
  │     - SpeechToText.StartFastTranscriptionWithRetry()
  │     - RecordingSession.AppendTranscript()
  │
  ▼ （STOP時）
文字起こし完了待機 → 要約 → 保存
  │
  ├─▶ WaitForAllChunksAsync()
  │     - 未完了の文字起こしタスクを待機
  │
  ├─▶ SummarizeText.SummarizeAsync()
  │     - システムプロンプト + 辞書 + 文字起こし結果
  │     - Azure OpenAI API呼び出し
  │
  ├─▶ GoogleSheetsExporter.ExportAsync()
  │     - Homeシート2行目に挿入（400行制限で削除）
  │     - 年月シートにAppend
  │
  └─▶ .summary.txt 保存
```

### セッション状態遷移
```
[初期状態] ─── START ──→ [録音中] ─── PAUSE ──→ [一時停止中]
     ↑                      │                        │
     │                      │                        │
     │                   STOP                     RESUME
     │                      │                        │
     │                      ▼                        ▼
     └─────────────── [停止済み] ←───────────── [録音中]
                           │
                           ▼
                    （要約処理中）
                           │
                           ▼
                    [処理完了]
```

---

## 8. Azure API 詳細仕様

### A. Azure Speech-to-Text (Fast Transcription API)
| 項目 | 値 |
|------|-----|
| **エンドポイント** | `https://japaneast.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15` |
| **メソッド** | POST |
| **Content-Type** | `multipart/form-data` |

**リクエストヘッダー:**
```http
Ocp-Apim-Subscription-Key: {AZURE_SPEECH_KEY}
Ocp-Apim-Subscription-Region: japaneast
```

**リクエストボディ (multipart/form-data):**
```
audio: [WAVファイルバイナリ]
definition: {"locales":["ja-JP"],"format":"Display"}
```

**レスポンス例:**
```json
{
  "combinedPhrases": [
    { "text": "文字起こし結果のテキスト..." }
  ],
  "phrases": [...]
}
```

### B. Azure OpenAI (Chat Completions)
| 項目 | 値 |
|------|-----|
| **エンドポイント** | `{AZURE_OPENAI_ENDPOINT}/openai/v1/chat/completions` |
| **メソッド** | POST |
| **Content-Type** | `application/json` |

**リクエストヘッダー:**
```http
api-key: {AZURE_OPENAI_API_KEY}
```

**リクエストボディ:**
```json
{
  "model": "{AZURE_OPENAI_DEPLOYMENT_NAME}",
  "messages": [
    { "role": "system", "content": "システムプロンプト..." },
    { "role": "user", "content": "文字起こしテキスト..." }
  ],
  "temperature": 0.3,
  "top_p": 0.95
}
```

**レスポンス:**
```json
{
  "choices": [
    { "message": { "content": "要約結果..." } }
  ]
}
```

### C. ライセンス認証API
| 項目 | 値 |
|------|-----|
| **エンドポイント** | `https://karustep-license-api.onrender.com/verify` |
| **メソッド** | GET |
| **クエリパラメータ** | `hwid={HWID}` |
| **成功判定** | HTTP 200 OK |

### D. Google Sheets Key取得API (Azure Functions)
| 項目 | 値 |
|------|-----|
| **エンドポイント** | `https://func-karustep-trial-ggcphgaecpcbajft.japanwest-01.azurewebsites.net/api/GetGoogleSheetsKey` |
| **メソッド** | GET |
| **認証** | `?code={AZURE_FUNCTIONS_KEY}` |

**レスポンス:**
```json
{
  "credentials": "サービスアカウントJSON文字列（エスケープ済み）"
}
```

---

## 9. 入出力フォーマット仕様

### A. 録音ファイル
| 項目 | 値 |
|------|-----|
| **形式** | WAV (PCM) |
| **サンプルレート** | 16,000 Hz |
| **ビット深度** | 16 bit |
| **チャンネル** | モノラル (1ch) |
| **命名規則** | `recording_{sessionId}_{yyyyMMdd_HHmmss}.wav` |

### B. 要約出力フォーマット
要約結果は以下の構造化テキスト形式で出力されます：
```
info[
 - 日時: {yyyy-mm-dd hh:mm:ss}
 - who: {患者氏名}
]

fact[
＜S＞
主訴：...
現病歴：
...
]

assessment[
＜O＞
...検査結果・所見...
]

todo[
＜A/P＞
#疾患名1
...治療方針...

#疾患名2
...治療方針...
]
```

### C. Google Sheets列構造
| 列 | 内容 |
|----|------|
| A | day (日付: yyyy/MM/dd) |
| B | time (時刻: HH:mm) |
| C | PC (PC名/診察室名) |
| D | who (患者名) |
| E | Fact (＜S＞セクション) |
| F | Assessment (＜O＞セクション) |
| G | ToDo (＜A/P＞セクション) |
| H | All (E+F+G結合テキスト) |

### D. プロンプトファイル形式
- **ファイル名**: `NN. 説明.txt` (NN: 00〜11の連番)
- **エンコーディング**: UTF-8
- **プレースホルダ**:
  - `[[DICTIONARY_PLACEHOLDER]]` → 辞書内容に置換
  - `[[RAG_PLACEHOLDER]]` → RAG情報に置換（現在は無効化）

### E. 辞書ファイル形式 (`dictionary.txt`)
```
よみがな→正しい表記
そうかしりつびょういん→草加市立病院
かるぼしすていん→カルボシステイン
```
- 1行1エントリ
- `→` で区切り
- UTF-8エンコーディング

---

## 10. 複数セッション（タブ）管理

### セッションデータモデル (`RecordingSession.cs`)
```csharp
public class RecordingSession : INotifyPropertyChanged, IDisposable
{
    // 基本情報
    public string SessionId { get; }           // yyyyMMdd_HHmmss形式
    public string PatientName { get; set; }    // 患者名（自動抽出または手動入力）
    public DateTime? RecordingStartTime { get; }
    
    // 状態フラグ
    public bool IsRecording { get; }
    public bool IsPaused { get; }
    public bool IsStopped { get; }
    
    // ファイル管理
    public string OutputDirectory { get; }
    public string CurrentTextFilePath { get; }
    public string SummaryFilePath { get; }
    public List<string> SessionRecordingFiles { get; }
    
    // 録音コンポーネント
    public SoundRecorder? Recorder { get; }
    
    // 文字起こし結果
    public string AccumulatedTranscript { get; }
}
```

### タブ表示用Converterクラス
| Converter | 用途 |
|-----------|------|
| `SessionIndexConverter` | セッション番号を①②③...形式に変換 |
| `TimeDisplayConverter` | 録音開始時刻をHH:mm形式に変換 |
| `SessionStatusIconConverter` | 状態に応じたアイコン（●/⏸/☑）を返す |
| `SessionStatusColorConverter` | 状態に応じた色（赤/黄/緑）を返す |

### 患者名自動抽出ロジック
- 文字起こしテキストの先頭500文字から `○○さん` パターンを検出
- 除外ワード: 「奥さん」「先生」「母」「父」等の一般名詞
- フルネーム優先（「鈴木」より「鈴木花子」を採用）

---

## 11. 例外処理・リトライ設計

### グローバル例外ハンドリング (`App.xaml.cs`)
- `DispatcherUnhandledException`: UI スレッド例外
- `AppDomain.CurrentDomain.UnhandledException`: 非UIスレッド例外
- 描画系エラー検出時: ソフトウェアレンダリングへフォールバック
- `NullReferenceException`: ログのみ、ポップアップ抑制

### APIリトライ戦略
| API | リトライ回数 | バックオフ | 対象エラー |
|-----|------------|-----------|-----------|
| Azure Speech-to-Text | 3回 | 指数 (1s→2s→4s) | 429, 503, 504 |
| Azure OpenAI | 3回 | 指数 (1s→2s→4s) | タイムアウト, HTTP エラー |
| Google Sheets | なし | - | 失敗してもアプリ継続 |
| ライセンスAPI | なし | - | タイムアウト10秒 |

### 録音エラーハンドリング
- NAudio失敗時: MCIへ自動フォールバック
- 録音ファイル0バイト: エラー扱い
- マイク未接続: ユーザーへエラーメッセージ表示
