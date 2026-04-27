"""
POC-4: DeepL API 翻訳検証

検証目的:
- DeepL APIが医療用語を含むテキストを適切に翻訳できるか確認
- 日→英、英→日、中→日などの翻訳ペアをテスト

使用方法:
1. 環境変数を設定:
   set DEEPL_API_KEY=your_key

2. 実行:
   python poc_deepl.py
"""

import httpx
import asyncio
import os
import sys


def get_config():
    """環境変数から設定を取得"""
    key = os.environ.get("DEEPL_API_KEY")
    
    if not key:
        print("❌ エラー: 環境変数 DEEPL_API_KEY が設定されていません")
        print()
        print("設定方法（Windows PowerShell）:")
        print('  $env:DEEPL_API_KEY = "your_api_key"')
        print()
        print("設定方法（コマンドプロンプト）:")
        print('  set DEEPL_API_KEY=your_api_key')
        print()
        print("DeepL APIキーの取得: https://www.deepl.com/pro-api")
        sys.exit(1)
    
    # API種別判定（Free版とPro版でエンドポイントが異なる）
    if key.endswith(":fx"):
        endpoint = "https://api-free.deepl.com/v2/translate"
        plan = "Free"
    else:
        endpoint = "https://api.deepl.com/v2/translate"
        plan = "Pro"
    
    return key, endpoint, plan


async def translate_text(
    client: httpx.AsyncClient,
    text: str,
    source_lang: str,
    target_lang: str,
    key: str,
    endpoint: str
) -> dict:
    """
    DeepL APIでテキストを翻訳
    
    Args:
        client: HTTPクライアント
        text: 翻訳対象テキスト
        source_lang: 元言語コード（例: "JA", "EN"）
        target_lang: 翻訳先言語コード（例: "EN-US", "JA"）
        key: DeepL APIキー
        endpoint: APIエンドポイント
    
    Returns:
        翻訳結果（辞書形式）
    """
    response = await client.post(
        endpoint,
        headers={
            "Authorization": f"DeepL-Auth-Key {key}",
            "Content-Type": "application/x-www-form-urlencoded",
        },
        data={
            "text": text,
            "source_lang": source_lang,
            "target_lang": target_lang,
        }
    )
    
    return {
        "status_code": response.status_code,
        "body": response.json() if response.status_code == 200 else response.text
    }


async def test_translation(
    text: str,
    source_lang: str,
    target_lang: str,
    key: str,
    endpoint: str,
    description: str = ""
) -> bool:
    """
    翻訳テストを実行
    
    Returns:
        True if successful, False otherwise
    """
    print(f"\n{'='*70}")
    print(f"テスト: {source_lang} → {target_lang} {description}")
    print(f"{'='*70}")
    print(f"原文: {text[:100]}{'...' if len(text) > 100 else ''}")
    
    async with httpx.AsyncClient(timeout=30.0) as client:
        result = await translate_text(client, text, source_lang, target_lang, key, endpoint)
        
        status = result["status_code"]
        
        if status == 200:
            body = result["body"]
            translated = body["translations"][0]["text"]
            detected_lang = body["translations"][0].get("detected_source_language", source_lang)
            
            print(f"✅ 翻訳成功")
            print(f"翻訳: {translated[:200]}{'...' if len(translated) > 200 else ''}")
            print(f"検出言語: {detected_lang}")
            return True
        else:
            print(f"❌ 失敗 (Status: {status})")
            print(f"エラー: {result['body']}")
            return False


async def check_usage(key: str, endpoint: str):
    """
    DeepL API使用量を確認
    """
    usage_endpoint = endpoint.replace("/translate", "/usage")
    
    async with httpx.AsyncClient(timeout=10.0) as client:
        response = await client.get(
            usage_endpoint,
            headers={"Authorization": f"DeepL-Auth-Key {key}"}
        )
        
        if response.status_code == 200:
            usage = response.json()
            used = usage.get("character_count", 0)
            limit = usage.get("character_limit", 0)
            remaining = limit - used
            percentage = (used / limit * 100) if limit > 0 else 0
            
            print(f"\n📊 API使用状況:")
            print(f"   使用済み: {used:,} 文字")
            print(f"   上限: {limit:,} 文字")
            print(f"   残り: {remaining:,} 文字 ({100-percentage:.1f}%)")
        else:
            print(f"⚠️ 使用量確認失敗: {response.status_code}")


async def main():
    print("""
╔═══════════════════════════════════════════════════════════════════════╗
║                    POC-4: DeepL API 翻訳検証                          ║
╚═══════════════════════════════════════════════════════════════════════╝
""")
    
    key, endpoint, plan = get_config()
    print(f"✅ 設定確認: プラン = {plan}")
    print(f"✅ エンドポイント: {endpoint}")
    print(f"✅ APIキー: {key[:8]}...")
    
    # 使用量確認
    await check_usage(key, endpoint)
    
    # テストケース定義（医療テキストを使用）
    test_cases = [
        # 日本語 → 英語（医療テキスト）
        (
            "患者さんは3日前から咳と発熱があります。胸部レントゲンでは異常所見は認められませんでした。",
            "JA", "EN-US",
            "（医療テキスト: 症状報告）"
        ),
        # 英語 → 日本語（医療テキスト）
        (
            "The patient has been experiencing cough and fever for 3 days. Chest X-ray showed no abnormal findings. I recommend taking acetaminophen for the fever.",
            "EN", "JA",
            "（医療テキスト: 診断・処方）"
        ),
        # 中国語 → 日本語
        (
            "患者有咳嗽和发烧已经三天了。我头痛得厉害。",
            "ZH", "JA",
            "（中国語: 患者の訴え）"
        ),
        # 日本語 → 中国語
        (
            "お薬を処方しますので、1日3回、食後に服用してください。",
            "JA", "ZH",
            "（日本語: 処方説明）"
        ),
        # 韓国語 → 日本語
        (
            "어제부터 배가 아파요. 열은 없어요.",
            "KO", "JA",
            "（韓国語: 患者の訴え）"
        ),
        # ポルトガル語 → 日本語
        (
            "Eu tenho dor de cabeça desde ontem. Também estou com náusea.",
            "PT", "JA",
            "（ポルトガル語: 患者の訴え）"
        ),
        # 日本語 → スペイン語
        (
            "血液検査の結果、特に問題はありませんでした。次回は1ヶ月後に来てください。",
            "JA", "ES",
            "（日本語: 検査結果説明）"
        ),
        # ベトナム語 → 日本語
        (
            "Tôi bị đau bụng từ sáng nay. Tôi cũng bị tiêu chảy.",
            "VI", "JA",
            "（ベトナム語: 患者の訴え）"
        ),
    ]
    
    # テスト実行
    results = []
    for text, src, tgt, desc in test_cases:
        success = await test_translation(text, src, tgt, key, endpoint, desc)
        results.append((src, tgt, desc, success))
        
        # API制限回避のため少し待機
        await asyncio.sleep(0.5)
    
    # 結果サマリー
    print("\n")
    print("╔═══════════════════════════════════════════════════════════════════════╗")
    print("║                         検証結果サマリー                               ║")
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    
    all_success = True
    for src, tgt, desc, success in results:
        status = "✅ Go" if success else "🔴 No-Go"
        print(f"║ {status}: {src:5} → {tgt:5} {desc:40} ║")
        if not success:
            all_success = False
    
    print("╠═══════════════════════════════════════════════════════════════════════╣")
    if all_success:
        print("║ 🎉 総合判定: ✅ Go - DeepL APIは医療翻訳に使用可能です                 ║")
    else:
        print("║ ⚠️ 総合判定: 🔴 No-Go - 一部テストが失敗しました                      ║")
    print("╚═══════════════════════════════════════════════════════════════════════╝")
    
    # 追加情報
    print("""
📋 補足情報:
- DeepL Free版: 月500,000文字まで
- DeepL Pro版: 従量課金制（$20/100万文字〜）
- Glossary（用語集）機能で医療用語の翻訳精度を向上可能
""")


if __name__ == "__main__":
    asyncio.run(main())

