"""
Translation Service
===================
DeepL API を使用した翻訳サービス
"""

import httpx
import os
from typing import Optional

# 設定
DEEPL_API_KEY = os.getenv("DEEPL_API_KEY", "")

# エンドポイント（Free版とPro版で異なる）
def get_endpoint() -> str:
    if DEEPL_API_KEY.endswith(":fx"):
        return "https://api-free.deepl.com/v2/translate"
    return "https://api.deepl.com/v2/translate"

# 接続プール
_client: Optional[httpx.AsyncClient] = None


async def get_client() -> httpx.AsyncClient:
    """接続プール付きHTTPクライアントを取得"""
    global _client
    if _client is None:
        _client = httpx.AsyncClient(
            timeout=httpx.Timeout(30.0, connect=10.0),
            limits=httpx.Limits(max_connections=10, max_keepalive_connections=5),
        )
    return _client


def convert_to_deepl_code(azure_code: str) -> str:
    """
    Azure STT言語コードをDeepL言語コードに変換
    
    Args:
        azure_code: Azure STT言語コード (例: "ja-JP", "en-US")
    
    Returns:
        DeepL言語コード (例: "JA", "EN-US")
    """
    mapping = {
        "ja-JP": "JA",
        "ja": "JA",
        "en-US": "EN-US",
        "en-GB": "EN-GB",
        "en": "EN",
        "zh-CN": "ZH",
        "zh-TW": "ZH",
        "zh": "ZH",
        "ko-KR": "KO",
        "ko": "KO",
        "es-ES": "ES",
        "es": "ES",
        "pt-BR": "PT-BR",
        "pt": "PT",
        "vi-VN": "VI",
        "vi": "VI",
        "th-TH": "TH",  # DeepL未対応の可能性
        "tl-PH": "EN-US",  # タガログ語→英語
    }
    
    return mapping.get(azure_code, azure_code.split('-')[0].upper())


async def translate_text(
    text: str,
    source_lang: str,
    target_lang: str,
) -> str:
    """
    DeepL APIでテキストを翻訳
    
    Args:
        text: 翻訳対象テキスト
        source_lang: 元言語コード (Azure STT形式)
        target_lang: 翻訳先言語コード (Azure STT形式)
    
    Returns:
        翻訳されたテキスト
    """
    if not DEEPL_API_KEY:
        return f"[翻訳エラー: DEEPL_API_KEY未設定] {text}"
    
    if not text.strip():
        return ""
    
    # 言語コードを変換
    deepl_source = convert_to_deepl_code(source_lang)
    deepl_target = convert_to_deepl_code(target_lang)
    
    # ソース言語の-US/-GBを除去（DeepLのソース言語では不要）
    if deepl_source.startswith("EN"):
        deepl_source = "EN"
    if deepl_source.startswith("PT"):
        deepl_source = "PT"
    
    client = await get_client()
    
    try:
        response = await client.post(
            get_endpoint(),
            headers={
                "Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}",
                "Content-Type": "application/x-www-form-urlencoded",
            },
            data={
                "text": text,
                "source_lang": deepl_source,
                "target_lang": deepl_target,
            },
        )
        
        if response.status_code == 200:
            result = response.json()
            return result["translations"][0]["text"]
        else:
            print(f"DeepL API error: {response.status_code} - {response.text}")
            return f"[翻訳エラー: {response.status_code}] {text}"
            
    except Exception as e:
        print(f"Translation error: {e}")
        return f"[翻訳エラー] {text}"

