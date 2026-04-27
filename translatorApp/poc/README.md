# TranslatorApp POC（技術検証）

Phase 0: 技術検証用のファイル一式です。

## 📁 ファイル構成

```
poc/
├── README.md              # このファイル
├── requirements.txt       # Python依存パッケージ
├── poc-footswitch.html    # POC-1: フットスイッチ検証
├── poc-web-audio.html     # POC-2: Web Audio API録音検証
├── poc_azure_stt.py       # POC-3: Azure STT多言語対応検証
├── poc_deepl.py           # POC-4: DeepL API翻訳検証
├── poc_azure_openai.py    # POC-5: Azure OpenAI検証
└── poc-entra-auth.html    # POC-6: Entra ID認証検証
```

## 🚀 実行手順

### 前提条件

- **ブラウザ**: Google Chrome（最新版推奨）
- **Python**: 3.10以上
- **APIキー**: Azure Speech, DeepL, Azure OpenAI

### Step 1: Python環境セットアップ

```powershell
# POCフォルダに移動
cd translatorApp/poc

# 仮想環境作成（推奨）
python -m venv venv
.\venv\Scripts\Activate.ps1

# 依存パッケージインストール
pip install -r requirements.txt
```

### Step 2: 環境変数の設定

PowerShellで以下を実行：

```powershell
# Azure Speech Service
$env:AZURE_SPEECH_KEY = "your_azure_speech_key"
$env:AZURE_SPEECH_REGION = "japaneast"

# DeepL API
$env:DEEPL_API_KEY = "your_deepl_api_key"

# Azure OpenAI
$env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com"
$env:AZURE_OPENAI_API_KEY = "your_azure_openai_key"
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-4o"
```

### Step 3: POC実行

#### POC-1: フットスイッチ検証（最重要）

```powershell
# ブラウザで開く
start chrome poc-footswitch.html
```

1. ページをChromeで開く
2. フットスイッチを踏んでキーイベントが発火するか確認
3. 「録音中」⇔「停止中」が切り替わるか確認
4. 別ウィンドウにフォーカスを移した状態でも動作するか確認（※動作しないのが想定通り）

**判定基準:**
- ✅ Go: フォーカス中にキーイベントが発火する
- 🔴 No-Go: キーイベントが発火しない → ネイティブアプリ検討

#### POC-2: Web Audio API録音検証

```powershell
start chrome poc-web-audio.html
```

1. 「録音開始」ボタンをクリック
2. マイクへのアクセスを許可
3. 数秒間話す
4. 「録音停止」ボタンをクリック
5. 「WAVダウンロード」でファイルを保存（POC-3で使用）

**判定基準:**
- ✅ Go: WAVファイルが正常にダウンロードできる
- 🔴 No-Go: 録音に失敗する

#### POC-3: Azure STT多言語対応検証

```powershell
# POC-2で録音したWAVファイルを配置
# - test_japanese.wav: 日本語の音声
# - test_english.wav: 英語の音声

python poc_azure_stt.py
```

**判定基準:**
- ✅ Go: 複数言語のlocale指定で文字起こし成功
- 🔴 No-Go: エラーが発生する

#### POC-4: DeepL API翻訳検証

```powershell
python poc_deepl.py
```

**判定基準:**
- ✅ Go: 日英・英日・中日など各種翻訳が成功
- 🔴 No-Go: 翻訳に失敗する

#### POC-5: Azure OpenAI検証

```powershell
python poc_azure_openai.py
```

**判定基準:**
- ✅ Go: SOAP形式カルテが生成される
- 🔴 No-Go: API呼び出しに失敗する

#### POC-6: Entra ID認証検証

※ Azure App登録が必要なため、別途設定が必要です。

## 📊 Go/No-Go判定表

POC完了後、以下の表を埋めてください：

| POC項目              | 結果     | 判定     | 代替案               |
|---------------------|----------|----------|---------------------|
| POC-1: フットスイッチ | □ 成功   | □ Go     | Electron/Tauri       |
| POC-2: Web Audio API | □ 成功   | □ Go     | なし（必須）          |
| POC-3: Azure STT     | □ 成功   | □ Go     | Google STT           |
| POC-4: DeepL翻訳     | □ 成功   | □ Go     | Azure Translator     |
| POC-5: Azure OpenAI  | □ 成功   | □ Go     | OpenAI直接           |
| POC-6: Entra ID認証  | □ 成功   | □ Go     | Auth0                |

### 最終判定

- ✅ **全てGo** → Phase 1へ進行（Webアプリとして開発）
- 🔴 **POC-1がNo-Go** → ネイティブアプリ（Electron/Tauri）として開発
- ⚠️ **その他No-Go** → 代替技術で再検証

## 🔧 トラブルシューティング

### POC-1: フットスイッチが認識されない

1. フットスイッチのキー設定を確認（多くはスペースキーやF13-F24）
2. デバイスマネージャーで「キーボード」として認識されているか確認
3. 別のアプリ（メモ帳など）でフットスイッチが動作するか確認

### POC-2: マイクアクセスが拒否される

1. Chromeの設定 → プライバシーとセキュリティ → サイトの設定 → マイク
2. 該当サイトの許可を確認

### POC-3〜5: APIエラー

1. APIキーが正しく設定されているか確認
2. リージョン設定が正しいか確認
3. APIのクォータ（使用上限）を確認

