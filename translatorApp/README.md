# TranslatorApp - 医療通訳支援Webアプリ

外国人患者と日本人医師の会話をリアルタイムで文字起こし・翻訳するWebアプリケーション。

## 🚀 クイックスタート

### 1. バックエンドのセットアップ

```powershell
cd translatorApp/backend

# 仮想環境作成（推奨）
python -m venv venv
.\venv\Scripts\Activate.ps1

# 依存パッケージインストール
pip install -r requirements.txt

# 環境変数設定
copy .env.example .env
# .env ファイルを編集してAPIキーを設定
```

### 2. 環境変数の設定

`backend/.env` ファイルを編集：

```env
# Azure Speech Service
AZURE_SPEECH_KEY=your_azure_speech_key
AZURE_SPEECH_REGION=japaneast

# DeepL API
DEEPL_API_KEY=your_deepl_api_key
```

### 3. フロントエンドのセットアップ

```powershell
cd translatorApp/frontend

# 依存パッケージインストール
npm install
```

### 4. 起動

**方法A: バッチファイルを使用**

```
start-backend.bat をダブルクリック（バックエンド）
start-frontend.bat をダブルクリック（フロントエンド）
```

**方法B: コマンドで起動**

```powershell
# ターミナル1: バックエンド
cd backend
python -m uvicorn main:app --reload --port 8000

# ターミナル2: フロントエンド
cd frontend
npm run dev
```

### 5. アクセス

ブラウザで http://localhost:3000 を開く

## 🎮 操作方法

1. **言語設定**: 医師と患者の言語を選択
2. **録音開始**: 
   - フットスイッチ（Ctrl+Shift+,）を踏む
   - または画面のマイクボタンをクリック
3. **話す**: 医師または患者が話す
4. **録音停止**: 再度フットスイッチを踏むかボタンをクリック
5. **結果表示**: 文字起こしと翻訳がチャット形式で表示

## 📁 プロジェクト構成

```
translatorApp/
├── frontend/           # React + TypeScript + Vite
│   ├── src/
│   │   ├── components/ # UIコンポーネント
│   │   ├── hooks/      # カスタムフック
│   │   └── App.tsx     # メインアプリ
│   └── package.json
│
├── backend/            # FastAPI (Python)
│   ├── services/       # API連携サービス
│   │   ├── speech_to_text.py  # Azure STT
│   │   ├── translator.py      # DeepL
│   │   └── language_mapper.py # 言語/ロール変換
│   ├── main.py         # FastAPIエントリポイント
│   └── requirements.txt
│
├── poc/                # Phase 0 技術検証
└── README.md
```

## 🔧 技術スタック

### フロントエンド
- React 18 + TypeScript
- Vite
- Tailwind CSS
- Web Audio API（録音）

### バックエンド
- FastAPI (Python)
- httpx（非同期HTTP）
- Azure Speech Service（STT）
- DeepL API（翻訳）

## 📋 開発フェーズ

- [x] Phase 0: 技術検証（POC）
- [ ] Phase 1: 技術統合（現在）
- [ ] Phase 2: データ永続化
- [ ] Phase 3: 認証とマルチテナント
- [ ] Phase 4: 要約機能
- [ ] Phase 5: UI/UX改善
- [ ] Phase 6: 課金管理
- [ ] Phase 7: テスト・デプロイ

## ⚠️ 注意事項

- フットスイッチはChromeがフォーカス中のみ動作します
- 音声データはサーバーに保存されません（メモリ上で処理後破棄）
- インターネット接続が必要です

## 📄 ライセンス

Proprietary - All Rights Reserved

