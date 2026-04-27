"""
POC-3: Azure Fast Transcription API 多言語対応検証

検証目的:
- VoiceRecorderで使用しているFast Transcription APIが
  複数言語（日本語+英語など）を同時認識できるか確認

使用方法:
1. 環境変数を設定:
   set AZURE_SPEECH_KEY=your_key
   set AZURE_SPEECH_REGION=japaneast

2. テスト用音声ファイルを用意:
   - test_japanese.wav: 日本語の音声
   - test_english.wav: 英語の音声

3. 実行:
   python poc_azure_stt.py
"""

import httpx
import asyncio
import os
import sys
from pathlib import Path


def get_config():
    """環境変数から設定を取得"""
    key = os.environ.get("AZURE_SPEECH_KEY")
    region = os.environ.get("AZURE_SPEECH_REGION", "japaneast")
    
    if not key:
        print("❌ エラー: 環境変数 AZURE_SPEECH_KEY が設定されていません")
        print()
        print("設定方法（Windows PowerShell）:")
        print('  $env:AZURE_SPEECH_KEY = "your_api_key"')
        print('  $env:AZURE_SPEECH_REGION = "japaneast"')
        print()
        print("設定方法（コマンドプロンプト）:")
        print('  set AZURE_SPEECH_KEY=your_api_key')
        print('  set AZURE_SPEECH_REGION=japaneast')
        sys.exit(1)
    
    return key, region


async def transcribe_audio(
    client: httpx.AsyncClient,
    audio_path: str,
    locales: list[str],
    key: str,
    region: str
) -> dict:
    """
    Azure Fast Transcription API で音声を文字起こし
    
    Args:
        client: HTTPクライアント
        audio_path: 音声ファイルパス
        locales: 言語コードリスト（例: ["ja-JP", "en-US"]）
        key: Azure Speech APIキー
        region: Azureリージョン
    
    Returns:
        APIレスポンス（辞書形式）
    """
    endpoint = f"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15"
    
    with open(audio_path, "rb") as f:
        audio_bytes = f.read()
    
    # localesをJSON配列文字列に変換
    locales_json = str(locales).replace("'", '"')
    
    files = {
        "audio": (os.path.basename(audio_path), audio_bytes, "audio/wav"),
    }
    data = {
        "definition": f'{{"locales":{locales_json},"format":"Display"}}'
    }
    headers = {
        "Ocp-Apim-Subscription-Key": key,
        "Ocp-Apim-Subscription-Region": region,
    }
    
    response = await client.post(endpoint, files=files, data=data, headers=headers)
    
    return {
        "status_code": response.status_code,
        "body": response.json() if response.status_code == 200 else response.text
    }


async def test_multilingual_transcription(
    audio_path: str,
    locales: list[str],
    key: str,
    region: str
) -> bool:
    """
    多言語対応のFast Transcription APIをテスト
    
    Returns:
        True if successful, False otherwise
    """
    print(f"\n{'='*60}")
    print(f"テスト: {audio_path}")
    print(f"locales: {locales}")
    print(f"{'='*60}")
    
    async with httpx.AsyncClient(timeout=120.0) as client:
        result = await transcribe_audio(client, audio_path, locales, key, region)
        
        status = result["status_code"]
        print(f"Status: {status}")
        
        if status == 200:
            body = result["body"]
            
            # 結果表示
            if "combinedPhrases" in body and body["combinedPhrases"]:
                text = body["combinedPhrases"][0].get("text", "")
                print(f"✅ 文字起こし成功")
                print(f"テキスト: {text[:200]}{'...' if len(text) > 200 else ''}")
            
            if "phrases" in body and body["phrases"]:
                detected_locale = body["phrases"][0].get("locale", "unknown")
                confidence = body["phrases"][0].get("confidence", 0)
                print(f"検出言語: {detected_locale} (信頼度: {confidence:.2%})")
            
            # 詳細情報
            duration_ms = body.get("durationInTicks", 0) / 10000
            print(f"音声長: {duration_ms:.0f}ms")
            
            return True
        else:
            print(f"❌ 失敗")
            print(f"エラー: {result['body']}")
            return False


async def create_test_audio_info():
    """テスト用音声ファイルの作成方法を表示"""
    print("""
╔═══════════════════════════════════════════════════════════════════════╗
║                   テスト用音声ファイルについて                         ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                       ║
║ POC-2（poc-web-audio.html）で録音したWAVファイルを使用できます。      ║
║                                                                       ║
║ ファイル要件:                                                         ║
║   - 形式: WAV (PCM)                                                   ║
║   - サンプルレート: 16000Hz 推奨（8000-48000Hz対応）                   ║
║   - チャンネル: モノラル推奨                                          ║
║   - ビット深度: 16bit                                                 ║
║                                                                       ║
║ 配置場所（このスクリプトと同じフォルダ）:                              ║
║   - test_japanese.wav : 日本語の音声（例: "こんにちは、調子はどうですか"）║
║   - test_english.wav  : 英語の音声（例: "Hello, how are you?"）        ║
║                                                                       ║
╚═══════════════════════════════════════════════════════════════════════╝
""")


async def main():
    print("""
╔═══════════════════════════════════════════════════════════════════════╗
║          POC-3: Azure Fast Transcription API 多言語対応検証            ║
╚═══════════════════════════════════════════════════════════════════════╝
""")
    
    key, region = get_config()
    print(f"✅ 設定確認: リージョン = {region}")
    print(f"✅ APIキー: {key[:8]}...")
    
    # テスト用音声ファイルのパス
    script_dir = Path(__file__).parent
    test_japanese = script_dir / "test_japanese.wav"
    test_english = script_dir / "test_english.wav"
    
    # ファイル存在チェック
    files_exist = True
    if not test_japanese.exists():
        print(f"⚠️ ファイル未存在: {test_japanese}")
        files_exist = False
    if not test_english.exists():
        print(f"⚠️ ファイル未存在: {test_english}")
        files_exist = False
    
    if not files_exist:
        await create_test_audio_info()
        
        # ダミーテストとしてAPIエンドポイントの疎通確認のみ行う
        print("\n📡 APIエンドポイント疎通確認...")
        endpoint = f"https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15"
        
        async with httpx.AsyncClient(timeout=10.0) as client:
            try:
                # 空リクエストでエンドポイント確認
                response = await client.post(
                    endpoint,
                    headers={
                        "Ocp-Apim-Subscription-Key": key,
                        "Ocp-Apim-Subscription-Region": region,
                    }
                )
                if response.status_code in [400, 401, 403]:
                    if response.status_code == 401:
                        print("❌ 認証エラー: APIキーが正しくありません")
                    else:
                        print(f"✅ エンドポイント到達可能（ステータス: {response.status_code}）")
                        print("   テスト用音声ファイルを配置して再実行してください。")
            except Exception as e:
                print(f"❌ 接続エラー: {e}")
        
        return
    
    # テストケース定義
    test_cases = []
    
    if test_japanese.exists():
        test_cases.extend([
            (str(test_japanese), ["ja-JP"]),           # 単一言語
            (str(test_japanese), ["ja-JP", "en-US"]),  # 日英
        ])
    
    if test_english.exists():
        test_cases.extend([
            (str(test_english), ["en-US"]),            # 単一言語
            (str(test_english), ["ja-JP", "en-US"]),   # 日英
        ])
    
    # 両方存在する場合、3言語テストも追加
    if test_japanese.exists() and test_english.exists():
        test_cases.append((str(test_japanese), ["ja-JP", "en-US", "zh-CN"]))  # 3言語
    
    # テスト実行
    results = []
    for audio_path, locales in test_cases:
        success = await test_multilingual_transcription(audio_path, locales, key, region)
        results.append((audio_path, locales, success))
    
    # 結果サマリー
    print("\n")
    print("╔═══════════════════════════════════════════════════════════════════════╗")
    print("║                         検証結果サマリー                               ║")
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    
    all_success = True
    for audio, locales, success in results:
        status = "✅ Go" if success else "🔴 No-Go"
        audio_name = os.path.basename(audio)
        print(f"║ {status}: {audio_name:20} + {str(locales):30} ║")
        if not success:
            all_success = False
    
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    if all_success:
        print("║ 🎉 総合判定: ✅ Go - Azure STT多言語対応は使用可能です                 ║")
    else:
        print("║ ⚠️ 総合判定: 🔴 No-Go - 一部テストが失敗しました                      ║")
    print("╚═══════════════════════════════════════════════════════════════════════╝")


if __name__ == "__main__":
    asyncio.run(main())

