# Mac版作成のための準備チェックリスト

このドキュメントは、Windows上でMac版アプリの開発準備を進めるためのチェックリストです。
コード編集以外の環境構築・準備作業を、Windows側とMac側に分けて記載しています。

---

## 📋 Windows側の準備作業

### ステップ1: 開発環境のセットアップ

- [ ] **VS Code拡張機能「Swift」をインストール**
  - VS Codeの拡張機能マーケットプレイスで「Swift」を検索
  - Publisher: The Swift Server Work Group の公式拡張機能をインストール
  - 目的: シンタックスハイライト、AIの精度向上、基本的な構文チェック

- [ ] **Gitがインストールされているか確認**
  - PowerShellで `git --version` を実行して確認
  - 未インストールの場合は [Git for Windows](https://git-scm.com/download/win) をインストール

### ステップ2: プロジェクトフォルダの作成

- [ ] **親ディレクトリに移動**
  ```powershell
  cd C:\Users\junma\OneDrive\ドキュメント\電話問診AIアプリ関係\Karustep_for_windows
  ```

- [ ] **Mac版用フォルダを作成**
  ```powershell
  mkdir VoiceRecorder_Mac
  cd VoiceRecorder_Mac
  ```

- [ ] **VS Codeで新しいウィンドウを開く**
  - `VoiceRecorder_Mac` フォルダを右クリック → 「Codeで開く」
  - または、VS Codeで「ファイル」→「フォルダーを開く」→ `VoiceRecorder_Mac` を選択
  - **重要**: Windows版のウィンドウは閉じずに、並べて開いておくと参照しやすい

### ステップ3: Gitリポジトリの初期化

- [ ] **Gitリポジトリを初期化**
  ```powershell
  git init
  git branch -M main
  ```

- [ ] **`.gitignore` ファイルを作成**
  - `VoiceRecorder_Mac` フォルダ内に `.gitignore` を作成
  - 以下の内容をコピー＆ペースト:
  
  ```gitignore
  # Xcode
  *.xcodeproj/*
  !*.xcodeproj/project.pbxproj
  !*.xcodeproj/xcshareddata/
  *.xcworkspace/*
  !*.xcworkspace/contents.xcworkspacedata
  .DS_Store
  build/
  DerivedData/

  # Swift Package Manager
  .build/
  .swiftpm/

  # macOS
  .DS_Store
  .AppleDouble
  .LSOverride

  # ログファイル
  *.log

  # 一時ファイル
  *.swp
  *~
  ```

- [ ] **初期コミット（オプション）**
  ```powershell
  git add .gitignore
  git commit -m "Initial commit: Add .gitignore"
  ```

### ステップ4: プロジェクト説明ファイルの作成

- [ ] **`README.md` ファイルを作成**
  - `VoiceRecorder_Mac` フォルダ内に `README.md` を作成
  - 以下のテンプレートをコピー＆ペースト（必要に応じて編集）:
  
  ```markdown
  # VoiceRecorder (Mac版)

  Windows版「VoiceRecorder」のmacOS移植版です。

  ## 開発環境

  - **開発言語**: Swift 5.9+
  - **IDE**: Xcode 15+ (Mac側)
  - **編集環境**: VS Code + Cursor (Windows側)
  - **プロジェクト形式**: Swift Package Manager (SPM)

  ## セットアップ方法

  1. このリポジトリをクローン
  2. Xcodeで `Package.swift` を開く
  3. 依存関係が自動で解決されます

  ## 参照ドキュメント

  - `../VoiceRecorder/windows版アプリシステム詳細.md` - Windows版の詳細仕様
  - `../VoiceRecorder/mac版作成計画書.md` - Mac版の開発計画

  ## 開発フロー

  1. Windows側のVS Codeでコード編集
  2. Gitにコミット・プッシュ
  3. Mac側でクローン・ビルド・テスト
  ```

### ステップ5: 参照用ドキュメントの準備（オプション）

- [ ] **仕様書へのアクセス方法を確認**
  - Windows版の仕様書は `../VoiceRecorder/windows版アプリシステム詳細.md` で参照可能
  - 必要に応じて、`docs` フォルダを作成して仕様書をコピーすることも可能:
    ```powershell
    mkdir docs
    Copy-Item "..\VoiceRecorder\windows版アプリシステム詳細.md" "docs\"
    Copy-Item "..\VoiceRecorder\mac版作成計画書.md" "docs\"
    ```

### ステップ6: リモートリポジトリの準備（推奨）

- [ ] **GitHub/GitLab等のリモートリポジトリを作成**
  - GitHubで新しいリポジトリを作成（例: `VoiceRecorder-Mac`）
  - リモートを追加:
    ```powershell
    git remote add origin https://github.com/your-username/VoiceRecorder-Mac.git
    ```
  - 初回プッシュ（`.gitignore` と `README.md` のみ）:
    ```powershell
    git add .gitignore README.md
    git commit -m "Initial setup"
    git push -u origin main
    ```

---

## 🍎 Mac側の準備作業

### ステップ1: 開発環境のセットアップ

- [ ] **Xcodeをインストール**
  - App Storeから「Xcode」を検索してインストール
  - バージョン: Xcode 15以上を推奨
  - インストール後、初回起動時に「Command Line Tools」のインストールを許可

- [ ] **Xcodeのライセンスに同意**
  ```bash
  sudo xcodebuild -license accept
  ```

- [ ] **Gitがインストールされているか確認**
  ```bash
  git --version
  ```
  - Xcodeと一緒にインストールされるため、通常は既にインストール済み

### ステップ2: プロジェクトのクローン

- [ ] **リモートリポジトリからクローン**
  ```bash
  cd ~/Documents  # または任意の場所
  git clone https://github.com/your-username/VoiceRecorder-Mac.git
  cd VoiceRecorder-Mac
  ```

- [ ] **または、ローカルで共有している場合はフォルダを開く**
  - OneDrive等で共有している場合、直接フォルダを開く
  - または、USBメモリ等で `VoiceRecorder_Mac` フォルダをコピー

### ステップ3: Xcodeでプロジェクトを開く

- [ ] **`Package.swift` をXcodeで開く**
  - Finderで `VoiceRecorder-Mac` フォルダを開く
  - `Package.swift` をダブルクリック（または右クリック→「Xcodeで開く」）
  - Xcodeが自動的にSPMプロジェクトとして認識します

- [ ] **依存関係の解決を確認**
  - Xcodeが自動的に依存関係を解決します
  - エラーが出た場合は、Xcodeメニュー → 「File」→「Packages」→「Reset Package Caches」を実行

### ステップ4: ビルド設定の確認

- [ ] **ターゲットプラットフォームの確認**
  - Xcodeのプロジェクト設定で、macOS 13 (Ventura) 以降が選択されていることを確認
  - 「Product」→「Destination」で「My Mac」が選択されていることを確認

- [ ] **アーキテクチャの確認**
  - Apple Silicon (arm64) と Intel (x86_64) の両方に対応するため、Universal Binaryとしてビルド
  - 設定は通常、デフォルトで適切に設定されています

### ステップ5: 権限設定の準備（アプリ実行時に必要）

- [ ] **権限要求の流れを理解**
  - アプリ初回起動時に、以下の権限を要求されます:
    1. **マイク**: 音声録音のため
    2. **入力監視**: グローバルホットキー検知のため
    3. **アクセシビリティ**: 他アプリへの自動貼り付けのため
  - 各権限は「システム設定」→「プライバシーとセキュリティ」で手動で許可する必要があります

- [ ] **開発者証明書の準備（配布時）**
  - Apple Developer Programに登録（年額 $99）
  - Developer ID証明書を取得（公証のため）

---

## ✅ 準備完了の確認

### Windows側の確認項目

- [ ] VS CodeでSwift拡張機能が有効になっている
- [ ] `VoiceRecorder_Mac` フォルダが作成され、VS Codeで開かれている
- [ ] `.gitignore` ファイルが存在する
- [ ] `README.md` ファイルが存在する（オプション）
- [ ] Gitリポジトリが初期化されている
- [ ] リモートリポジトリが設定されている（推奨）

### Mac側の確認項目

- [ ] Xcode 15以上がインストールされている
- [ ] `Package.swift` をXcodeで開ける
- [ ] プロジェクトが正常にビルドできる（まだコードがなくてもエラーが出ない状態）

---

## 📝 次のステップ

準備が完了したら、以下の順序で開発を進めます:

1. **フェーズ1: 技術検証 (PoC)**
   - `Package.swift` と最小限のSwiftコードを生成
   - グローバルホットキー、自動貼り付け、録音機能の検証

2. **フェーズ2: コアロジック実装**
   - Windows版の仕様を参照しながら、Swiftで各サービスを実装

3. **フェーズ3: UI構築と結合**
   - SwiftUIでUIを構築

4. **フェーズ4: テストと配布準備**
   - テスト、公証、DMG作成

詳細は `mac版作成計画書.md` を参照してください。

---

## 🔗 関連ドキュメント

- `windows版アプリシステム詳細.md` - Windows版の詳細仕様
- `mac版作成計画書.md` - Mac版の開発計画と技術仕様
