# Mac版 VoiceRecorder 作成計画書 (改訂版)

Windows版「VoiceRecorder」のコア価値である「フットスイッチ操作」と「自動連携」をMac上で**確実かつ安定して**再現するための計画書です。
前回のクロスプラットフォーム案（.NET）を撤回し、**Swiftによるネイティブ開発**を前提としています。

> **参照ドキュメント**: `windows版アプリシステム詳細.md` - Windows版の詳細仕様（API仕様、データフォーマット等）

---

## 0. 開発環境・対象OS

| 項目 | 値 |
|------|-----|
| **開発言語** | Swift 5.9+ |
| **IDE** | Xcode 15+ |
| **対象macOS** | macOS 13 (Ventura) 以降 |
| **アーキテクチャ** | Apple Silicon (arm64) + Intel (x86_64) Universal Binary |

## 1. 開発方針：Native Swift (AppKit)

### 推奨理由
1.  **機能実現の確実性**: アプリの核心である「グローバルホットキー」と「他アプリへの自動貼付」は、macOSの深部API（Core Graphics, Accessibility）を使用します。これらはSwiftから直接操作するのが最も安定し、不具合も少ないです。
2.  **オーバーヘッドの回避**: C#からMacのAPIを呼び出す複雑なブリッジ処理（Interop）を排除し、開発・保守コストを下げます。
3.  **既存資産の扱い**: Windows版のソースコード（C#）は「詳細な仕様書」として参照しますが、コード自体はSwiftで書き直します（通信ロジック等はSwiftでも容易に実装可能です）。

---

## 2. システム構成

### アーキテクチャ図（Mac版）

```
[Mac Application (Swift / macOS Native)]
   │
   ├─ [UI Layer] (SwiftUI + AppKit)
   │      └─ 設定画面、インジケータ、権限リクエスト画面
   │
   ├─ [Core Logic] (Swiftで再実装)
   │      ├─ SpeechToTextService (URLSession)
   │      ├─ SummarizeTextService (URLSession)
   │      └─ GoogleSheetsService (Google API Client for Swift or REST)
   │
   └─ [OS Bridge] (Apple Native APIs)
          ├─ AudioRecorder (AVFoundation)
          ├─ HotkeyMonitor (Core Graphics / CGEvent)
          ├─ AutoPaster (Accessibility / AXUIElement)
          └─ SecureStorage (Keychain Services)
          └─ HardwareIdProvider (IOKit)
```

---

## 3. Windows版から引き継ぐ仕様

### 録音仕様
| 項目 | 仕様 |
|------|------|
| フォーマット | **WAV (PCM)** |
| サンプルレート | **16kHz** |
| ビット深度 | **16bit** |
| チャンネル | **モノラル** |
| 保存先 | `~/Documents/Karustep/` 配下（Macでの推奨） |

### パフォーマンスモード（チャンク分割間隔）
録音データをAPIに送信する間隔を制御するモード。Mac版でも同様に実装推奨。

| モード | チャンク間隔 | 特徴 |
|--------|-------------|------|
| Realtime (0) | 5〜30秒 | 無音検知あり。リアルタイム文字起こし向け |
| Balanced (1) | 60〜90秒 | API呼び出し頻度を削減 |
| LowLoad (2) | 300秒固定 | 5分ごとに送信。デフォルト推奨 |

### 無音検知
- 入力波形の振幅を監視
- **180秒間無音が続いた場合、自動で録音停止**（誤操作防止）

### API仕様（Azure/Google）

#### 音声認識 (STT)
| 項目 | 値 |
|------|-----|
| サービス | Azure Cognitive Services Speech (Fast Transcription API) |
| エンドポイント | `https://japaneast.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15` |
| 認証 | `Ocp-Apim-Subscription-Key` ヘッダー |
| 形式 | `multipart/form-data` でWAVファイル送信 |
| リトライ | 最大3回、指数バックオフ |

#### 要約 (LLM)
| 項目 | 値 |
|------|-----|
| サービス | Azure OpenAI Service (GPT-4o) |
| エンドポイント | `{AZURE_OPENAI_ENDPOINT}/openai/v1/chat/completions` |
| 認証 | `api-key` ヘッダー |
| 形式 | JSON (messages配列) |
| テンプレート変数 | `[[DICTIONARY_PLACEHOLDER]]`, `[[RAG_PLACEHOLDER]]` |

#### Google Sheets
| 項目 | 値 |
|------|-----|
| ライブラリ | Google API Client Library for Swift (または REST直接) |
| 認証 | サービスアカウントJSON |
| シート構造 | Homeシート（最新400行）+ 年月別アーカイブシート |

### 設定ファイル・リソース
Mac版で同様に必要なファイル群。アプリバンドル内またはユーザーDocuments配下に配置。

| ファイル | 用途 | 配置場所 |
|---------|------|---------|
| `dictionary.txt` | 音声認識の読み替え辞書 | アプリバンドル or Documents |
| `00.*.txt` 〜 `11.*.txt` | プロンプトテンプレート | アプリバンドル or Documents |
| `selected_prompt.txt` | 選択中のプロンプト記録 | Documents/Karustep/ |
| `settings.plist` (新規) | ユーザー設定 | Documents/Karustep/ |

---

## 4. 機能実装戦略

### ① フットスイッチ連携（グローバルホットキー）
*   **技術**: `CGEvent.tapCreate` を使用して、システム全体のキーボードイベントを監視します。
*   **実装**: フットスイッチから送信されるキーコードを検出し、**アプリがバックグラウンド（非アクティブ）にあっても録音処理を発火させます**。
    *   `CGEventTap` は低レベルAPIであり、フォーカスに関係なく全入力を傍受可能です。
*   **権限**: ユーザーによる「入力監視」の許可が必要です。

#### キー設定とマッピングの注意点
Windows用キーボード/フットスイッチをMacに接続した場合、修飾キーは以下のようにマッピングされます。フットスイッチの設定を変更せずに使用する場合、アプリ側でこのマッピングを待ち受ける必要があります。

| Windows物理キー | Macでの認識 |
|----------------|------------|
| **Ctrl** | **Control** |
| **Alt** | **Option** |
| **Win (Logo)** | **Command** |
| **Shift** | **Shift** |

**推奨する待ち受けキー設定（Windows版フットスイッチをそのまま使う場合）:**

| 操作 | Windows版設定 | Mac版で待ち受けるキー | 備考 |
|------|--------------|----------------------|------|
| 録音開始/停止 | `Ctrl` + `Shift` + `,` | `Control` + `Shift` + `,` | そのまま対応 |
| 一時停止 | `Ctrl` + `Shift` + `.` | `Control` + `Shift` + `.` | そのまま対応 |
| コピー＆貼り付け | `Alt` + `Ctrl` + `Shift` + `,` | **`Option`** + `Control` + `Shift` + `,` | AltはOptionになります |
| コピー＆貼り付け(Win) | `Win` + `Ctrl` + `Shift` + `,` | **`Command`** + `Control` + `Shift` + `,` | WinはCommandになります |

**対策**: 設定画面で「モディファイアキーの選択（Option / Command）」を可能にし、ユーザーの環境に合わせられるようにします。

### ② 自動貼り付け（Magic Paste）
Windows版の `user32.dll` による制御を、Macの Accessibility API で再現します。

*   **技術**: `AXUIElement` (Accessibility API)、`CGEvent`、`NSWorkspace`
*   **権限**: ユーザーによる「アクセシビリティ」の許可が必要です。

**実装手順（非アクティブウィンドウ対応）:**
1.  **カーソル位置取得**: `CGEvent(source: nil)!.location` (または `NSEvent.mouseLocation`) でマウスカーソルの現在位置を取得。
2.  **要素特定**: `AXUIElementCopyElementAtPosition` でカーソル位置にあるUI要素（ウィンドウ）を特定。
3.  **プロセス特定**: 取得した要素から `kAXPIDAttribute` (プロセスID) を取得。
4.  **アクティブ化**: `NSRunningApplication(processIdentifier: pid)?.activate(options: .activateIgnoringOtherApps)` を実行。
    *   **重要**: これにより、非アクティブだった電子カルテ等が最前面に来てフォーカスされます。
5.  **待機**: アプリ切り替えの完了を数ミリ秒待機（`usleep`）。
6.  **ペースト実行**: `CGEvent` で `Cmd+V` (`kVK_Command` + `kVK_ANSI_V`) を送信。
7.  **完了通知**: `NSApplication.shared.requestUserAttention()` でDockアイコンをバウンス（オプション）。

> **参考: Windows API と Mac API の対応**
> | Windows API | Mac API |
> |-------------|---------|
> | `GetCursorPos` | `CGEvent.location` / `NSEvent.mouseLocation` |
> | `WindowFromPoint` | `AXUIElementCopyElementAtPosition` |
> | `SetForegroundWindow` / `SetFocus` | `NSRunningApplication.activate()` |
> | `AttachThreadInput` | （Macでは不要、Accessibility APIで代替） |
> | `keybd_event(Ctrl+V)` | `CGEvent` で `Cmd+V` 送信 |

### ③ セキュリティ（APIキー管理）
*   **技術**: **Keychain Services**。
*   **実装**: APIキーやGoogle認証情報は、プレーンテキストで保存せず、macOS標準のKeychainに暗号化して保存します。

**Keychainに保存する項目:**
| キー名 | 用途 |
|--------|------|
| `com.karustep.azure-speech-key` | Azure STT APIキー |
| `com.karustep.azure-openai-key` | Azure OpenAI APIキー |
| `com.karustep.azure-openai-endpoint` | Azure OpenAI エンドポイント |
| `com.karustep.google-sheets-id` | Google SpreadsheetのID |
| `com.karustep.google-service-account` | Google認証JSON（暗号化） |

### ④ ライセンス認証 (HWID)
WindowsのWMIの代わりに、AppleのI/O Kitフレームワークを使用します。

*   **技術**: `IOKit` フレームワーク
*   **取得情報**: `IOPlatformUUID` (ロジックボードのシリアル番号)
*   **生成ロジック**: `IOPlatformUUID` を取得し、SHA256でハッシュ化 → 4-4-4-4形式に整形。
    *   ※Macはパーツ交換が一般的ではないため、UUID単体で十分な識別性を持ちます。

### ⑤ セッション管理
Windows版と同様に、複数患者のセッションを同時管理する機能を実装します。

*   **機能**:
    *   タブUI（またはサイドバー）での複数セッション切り替え
    *   患者名の自動抽出（「〇〇さん」パターン検出）
    *   セッションごとの録音一時停止/再開状態の管理

---

## 5. 必要な権限と実装詳細

### Info.plist への記述（必須）
コードを書くだけでなく、`Info.plist` に使用目的を明記する必要があります。

```xml
<key>NSMicrophoneUsageDescription</key>
<string>診察内容を録音し、文字起こしを行うためにマイクを使用します。</string>
<key>NSAppleEventsUsageDescription</key>
<string>他のアプリケーションへのテキスト貼り付けを行うために必要です。</string>
```

### 必須権限一覧
アプリ初回起動時に、以下の権限をユーザーに要求する必要があります。

| 権限 | 用途 | 設定場所 |
|------|------|---------|
| **マイク** | 音声録音 | システム設定 > プライバシーとセキュリティ > マイク |
| **入力監視** | グローバルホットキー検知 | システム設定 > プライバシーとセキュリティ > 入力監視 |
| **アクセシビリティ** | 他アプリへの自動貼り付け | システム設定 > プライバシーとセキュリティ > アクセシビリティ |
| **ネットワーク** | API通信 | 自動（Sandboxなしの場合） |

### 権限取得のUX設計
1.  初回起動時に「権限設定ウィザード」を表示
2.  「システム設定を開く」ボタンで直接設定画面 (`x-apple.systempreferences:`) へ誘導
3.  権限変更を検知してアプリを再起動（入力監視権限の付与後は再起動が必要なため）

---

## 6. エラーハンドリングとログ

### エラー処理方針
| エラー種別 | 対応 |
|-----------|------|
| ネットワークエラー | リトライ（最大3回、指数バックオフ）後、ユーザーに通知 |
| 録音エラー | マイク権限を確認し、設定画面への誘導 |
| API認証エラー | APIキーの再入力を促す |
| 無音180秒 | 自動停止 + 通知 |
| 録音ファイル0バイト | エラー扱い、再録音を促す |

### ログ出力
*   **出力先**: `~/Library/Logs/Karustep/` 配下
*   **フォーマット**: `yyyy-MM-dd_HH-mm-ss.log`
*   **内容**: タイムスタンプ、ログレベル、モジュール名、メッセージ
*   **保持期間**: 30日間（自動削除）

---

## 7. 配布・展開戦略

### 配布方法
*   **方式**: **Developer ID 配布**（自社サイト等からの直接配布）。
*   **App Store**: **不可**。本アプリ必須の「入力監視」「アクセシビリティ（制御）」権限は、App Storeのサンドボックス規定では許可されません。
*   **公証 (Notarization)**: macOS Gatekeeperの警告を回避するため、Appleへの公証提出は必須です。

### 配布物の構成
```
Karustep-Mac-vX.X.X.dmg
  ├─ Karustep.app
  ├─ インストール手順.pdf
  └─ アンインストール方法.txt
```

---

## 8. 開発ロードマップ

### フェーズ1: 技術検証 (PoC) — 推定1〜2週間
まず「Macで本当にこの挙動ができるか」を検証する小さなプロトタイプを作ります。

| 検証項目 | 成功基準 |
|---------|---------|
| ホットキー検知 | バックグラウンドで `Control+Shift+,` を拾い、ログ出力できる |
| 自動貼付 | 非アクティブなメモ.app上にあるカーソル位置を特定し、アクティブ化→貼付ができる |
| 録音 | AVFoundationで16kHz/16bit/Monoで録音できる |
| 権限ダイアログ | マイク・入力監視・アクセシビリティ各許可ダイアログの挙動を把握 |

### フェーズ2: コアロジック実装 (Swift) — 推定2〜3週間
Windows版の挙動をSwiftで再現します。
1.  `SpeechToTextService`: Azure STT APIとの通信（URLSession + multipart/form-data）
2.  `SummarizeTextService`: Azure OpenAI APIとの通信（URLSession + JSON）
3.  `GoogleSheetsService`: Google Sheets APIとの連携
4.  `AudioRecorder`: AVFoundationによる録音 + 無音検知 + チャンク分割
5.  `KeychainService`: APIキー等の安全な保存・取得

### フェーズ3: UI構築と結合 — 推定2〜3週間
1.  **設定画面**: APIキー入力、プロンプト選択、ホットキー設定
2.  **権限設定ウィザード**: 初回起動時のガイド
3.  **メイン画面**: 録音状態表示、文字起こし結果、セッション管理（タブUI）
4.  **メニューバーアイコン**: 常駐アプリとしての操作

### フェーズ4: テストと配布準備 — 推定1〜2週間
1.  各OSバージョン（Sonoma, Ventura）での動作確認
2.  Apple Developer Programへの登録と公証プロセスの確立
3.  DMGインストーラの作成
4.  ユーザードキュメント（インストール手順、初期設定ガイド）作成

---

## 9. テスト戦略

### 単体テスト (XCTest)
| テスト対象 | 内容 |
|-----------|------|
| SpeechToTextService | モックサーバーでAPI応答をテスト |
| SummarizeTextService | モックサーバーでAPI応答をテスト |
| KeychainService | 保存・取得・削除の動作確認 |
| AudioRecorder | フォーマット変換、無音検知ロジック |

### 統合テスト
| シナリオ | 確認事項 |
|---------|---------|
| 録音→文字起こし→要約 | End-to-Endで正常動作するか |
| 複数セッション切り替え | データが混在しないか |
| 権限なし状態 | 適切にエラーハンドリングされるか |

### 手動テスト項目
- [ ] フットスイッチ（実機）で `Control+Shift+,` が送信され、録音開始/停止ができる
- [ ] フットスイッチ（実機）で `Option+Control+Shift+,` が送信され、自動貼り付けができる
- [ ] バックグラウンド状態でホットキーが動作する
- [ ] カーソル位置にある非アクティブなウィンドウへの自動貼り付けが成功する
- [ ] 電子カルテ（対象アプリ）への自動貼り付けが成功する
- [ ] 長時間録音（30分以上）でメモリリークがない
- [ ] スリープ復帰後に正常動作する

---

## 10. 今後の検討事項

### 未決定事項
| 項目 | 選択肢 | 決定時期 |
|------|--------|---------|
| Google Sheets認証方式 | サービスアカウント or OAuth2 | フェーズ2開始前 |
| プロンプトの更新方法 | アプリ内蔵 or サーバー配信 | フェーズ3開始前 |
| ライセンス認証 | HWID方式移植 or 別方式 | フェーズ4開始前 |

### フットスイッチ配布時の確認事項
| 確認項目 | 詳細 |
|---------|------|
| **キーマッピング互換性** | Windows用に設定したフットスイッチがMacでも同じキーコードを送信するか要検証 |
| **Controlキーの扱い** | MacのControlキーはWindowsのCtrlキーと同等だが、フットスイッチ側の設定が対応しているか |
| **Option/Altの対応** | MacのOptionキー = WindowsのAltキーの対応確認 |
| **専用設定の必要性** | 必要に応じてMac用のフットスイッチ設定プロファイルを別途用意 |

### Windows版との機能差異（許容範囲）
| 機能 | Windows版 | Mac版 | 備考 |
|------|-----------|-------|------|
| ウィンドウフラッシュ | FlashWindowEx | NSApplication.requestUserAttention | 同等機能あり |
| コンソールログ | AllocConsole | Terminal.app or Console.app | 代替手段あり |
| タスクトレイ | NotifyIcon | NSStatusBar (メニューバー) | Mac標準の方式 |
