"""
POC-5: Azure OpenAI GPT-4o 検証

検証目的:
- VoiceRecorderと同じパラメータでAzure OpenAIが動作するか確認
- 医療カルテ（SOAP形式）の要約生成をテスト

使用方法:
1. 環境変数を設定:
   set AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com
   set AZURE_OPENAI_API_KEY=your_key
   set AZURE_OPENAI_DEPLOYMENT=gpt-4o  (オプション、デフォルト: gpt-4o)

2. 実行:
   python poc_azure_openai.py
"""

import httpx
import asyncio
import os
import sys
import json


def get_config():
    """環境変数から設定を取得"""
    endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT")
    key = os.environ.get("AZURE_OPENAI_API_KEY")
    deployment = os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o")
    
    missing = []
    if not endpoint:
        missing.append("AZURE_OPENAI_ENDPOINT")
    if not key:
        missing.append("AZURE_OPENAI_API_KEY")
    
    if missing:
        print("❌ エラー: 以下の環境変数が設定されていません")
        for var in missing:
            print(f"   - {var}")
        print()
        print("設定方法（Windows PowerShell）:")
        print('  $env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com"')
        print('  $env:AZURE_OPENAI_API_KEY = "your_api_key"')
        print('  $env:AZURE_OPENAI_DEPLOYMENT = "gpt-4o"')
        print()
        print("設定方法（コマンドプロンプト）:")
        print('  set AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com')
        print('  set AZURE_OPENAI_API_KEY=your_api_key')
        print('  set AZURE_OPENAI_DEPLOYMENT=gpt-4o')
        sys.exit(1)
    
    return endpoint, key, deployment


# VoiceRecorderのSummarizeText.csと同じシステムプロンプト構造
SOAP_SYSTEM_PROMPT = """あなたは医療カルテを作成するAIアシスタントです。
与えられた診察の会話ログから、SOAP形式のカルテを作成してください。

【SOAP形式について】
S (Subjective): 患者の主訴、自覚症状、病歴
O (Objective): 医師の所見、検査結果、バイタルサイン
A (Assessment): 診断、評価
P (Plan): 治療計画、処方、次回予約

【注意事項】
- 医療用語は正確に使用してください
- 患者のプライバシーに配慮した記載をしてください
- 不明な情報は「記載なし」としてください
"""

REFERRAL_SYSTEM_PROMPT = """あなたは医療文書を作成するAIアシスタントです。
与えられたSOAP形式のカルテから、英文の紹介状（Referral Letter）を作成してください。

【形式】
- 宛先: To Whom It May Concern
- 患者情報: 年齢、性別（わかる範囲で）
- 主訴と経過
- 診断
- 現在の治療
- 紹介目的

【注意事項】
- 正式な医療文書の形式を使用してください
- 専門用語は英語で正確に記載してください
"""


async def test_chat_completion(
    endpoint: str,
    key: str,
    deployment: str,
    system_prompt: str,
    user_message: str,
    test_name: str
) -> tuple[bool, str]:
    """
    Azure OpenAI Chat Completionをテスト
    
    Returns:
        (success: bool, response_text: str)
    """
    print(f"\n{'='*70}")
    print(f"テスト: {test_name}")
    print(f"{'='*70}")
    
    # VoiceRecorderと同じエンドポイント形式
    api_url = f"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-08-01-preview"
    
    # VoiceRecorderのSummarizeText.csと同じパラメータ
    request_body = {
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_message}
        ],
        "temperature": 0.3,   # VoiceRecorderと同じ
        "top_p": 0.95,        # VoiceRecorderと同じ
        "max_tokens": 4000,
    }
    
    print(f"パラメータ: temperature={request_body['temperature']}, top_p={request_body['top_p']}")
    print(f"入力文字数: {len(user_message)} 文字")
    
    async with httpx.AsyncClient(timeout=120.0) as client:
        try:
            response = await client.post(
                api_url,
                headers={
                    "api-key": key,
                    "Content-Type": "application/json"
                },
                json=request_body
            )
            
            if response.status_code == 200:
                result = response.json()
                content = result["choices"][0]["message"]["content"]
                usage = result.get("usage", {})
                
                print(f"✅ 成功")
                print(f"トークン使用量:")
                print(f"   入力: {usage.get('prompt_tokens', 'N/A')}")
                print(f"   出力: {usage.get('completion_tokens', 'N/A')}")
                print(f"   合計: {usage.get('total_tokens', 'N/A')}")
                print(f"\n--- 生成結果 ---")
                print(content[:1000])
                if len(content) > 1000:
                    print(f"... (以下省略、全{len(content)}文字)")
                
                return True, content
            else:
                print(f"❌ 失敗 (Status: {response.status_code})")
                print(f"エラー: {response.text}")
                return False, ""
                
        except httpx.TimeoutException:
            print("❌ タイムアウト (120秒)")
            return False, ""
        except Exception as e:
            print(f"❌ エラー: {e}")
            return False, ""


async def main():
    print("""
╔═══════════════════════════════════════════════════════════════════════╗
║                  POC-5: Azure OpenAI GPT-4o 検証                      ║
╚═══════════════════════════════════════════════════════════════════════╝
""")
    
    endpoint, key, deployment = get_config()
    print(f"✅ エンドポイント: {endpoint}")
    print(f"✅ デプロイメント: {deployment}")
    print(f"✅ APIキー: {key[:8]}...")
    
    # テスト用の会話ログ（日英混在を想定）
    conversation_log = """
【医師（日本語）】
今日はどうされましたか？

【患者（英語）→ 日本語訳】
3日前から咳と熱があります。

【医師（日本語）】
熱は何度くらいありますか？

【患者（英語）→ 日本語訳】
昨日は38.5度でした。今朝は38度です。

【医師（日本語）】
咳はどんな咳ですか？痰は出ますか？

【患者（英語）→ 日本語訳】
乾いた咳です。痰はあまり出ません。
喉も少し痛いです。

【医師（日本語）】
わかりました。胸の音を聞かせてください。
...
肺の音は綺麗ですね。胸部レントゲンでも異常は見られませんでした。
おそらくウイルス性の上気道炎だと思います。

【患者（英語）→ 日本語訳】
抗生物質は必要ですか？

【医師（日本語）】
ウイルス性なので抗生物質は効きません。
解熱剤と咳止めを処方しますので、
十分な水分を取って、ゆっくり休んでください。
3日経っても良くならない場合は、また来てください。

【患者（英語）→ 日本語訳】
わかりました。ありがとうございます。
"""
    
    results = []
    
    # テスト1: SOAP形式カルテ生成
    success1, soap_content = await test_chat_completion(
        endpoint, key, deployment,
        SOAP_SYSTEM_PROMPT,
        f"以下の診察の会話ログからSOAP形式のカルテを作成してください。\n\n{conversation_log}",
        "SOAP形式カルテ生成（VoiceRecorder相当）"
    )
    results.append(("SOAP形式カルテ生成", success1))
    
    # テスト2: 英文紹介状生成
    if success1 and soap_content:
        success2, _ = await test_chat_completion(
            endpoint, key, deployment,
            REFERRAL_SYSTEM_PROMPT,
            f"以下のSOAPカルテから英文紹介状を作成してください。\n\n{soap_content}",
            "英文紹介状生成"
        )
        results.append(("英文紹介状生成", success2))
    else:
        results.append(("英文紹介状生成", False))
    
    # テスト3: 短い応答テスト（レイテンシ確認）
    import time
    start_time = time.time()
    success3, _ = await test_chat_completion(
        endpoint, key, deployment,
        "あなたは医療アシスタントです。簡潔に回答してください。",
        "上気道炎の一般的な治療法を3行で説明してください。",
        "短い応答テスト（レイテンシ確認）"
    )
    latency = time.time() - start_time
    results.append((f"短い応答テスト (レイテンシ: {latency:.2f}秒)", success3))
    
    # 結果サマリー
    print("\n")
    print("╔═══════════════════════════════════════════════════════════════════════╗")
    print("║                         検証結果サマリー                               ║")
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    
    all_success = True
    for name, success in results:
        status = "✅ Go" if success else "🔴 No-Go"
        print(f"║ {status}: {name:55} ║")
        if not success:
            all_success = False
    
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    if all_success:
        print("║ 🎉 総合判定: ✅ Go - Azure OpenAIは使用可能です                       ║")
        print("║    VoiceRecorderと同じパラメータ (temp=0.3, top_p=0.95) で動作確認    ║")
    else:
        print("║ ⚠️ 総合判定: 🔴 No-Go - 一部テストが失敗しました                      ║")
    print("╚═══════════════════════════════════════════════════════════════════════╝")
    
    # コスト情報
    print("""
📋 Azure OpenAI GPT-4o 料金目安（2024年12月時点）:
- 入力: $2.50 / 100万トークン
- 出力: $10.00 / 100万トークン
- 1回の診察要約（約2000トークン）: 約$0.02〜0.03
""")


if __name__ == "__main__":
    asyncio.run(main())

