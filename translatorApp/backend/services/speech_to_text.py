"""
Speech to Text Service
======================
Azure Fast Transcription API を使用した音声文字起こし
VoiceRecorder の SpeechToText.cs を Python に移植
"""

import httpx
import os
import json
from typing import Optional

# 設定
AZURE_SPEECH_KEY = os.getenv("AZURE_SPEECH_KEY", "")
AZURE_SPEECH_REGION = os.getenv("AZURE_SPEECH_REGION", "japaneast")
ENDPOINT = f"https://{AZURE_SPEECH_REGION}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15"

# VoiceRecorderと同じ接続プール設定
_client: Optional[httpx.AsyncClient] = None


async def get_client() -> httpx.AsyncClient:
    """接続プール付きHTTPクライアントを取得"""
    global _client
    if _client is None:
        _client = httpx.AsyncClient(
            timeout=httpx.Timeout(120.0, connect=10.0),
            limits=httpx.Limits(max_connections=10, max_keepalive_connections=5),
        )
    return _client


async def transcribe_audio(
    audio_bytes: bytes,
    locales: list[str],
    max_retries: int = 3,
) -> dict:
    """
    Azure Fast Transcription API で音声を文字起こし
    
    Args:
        audio_bytes: WAV形式の音声データ
        locales: 言語コードリスト (例: ["ja-JP", "en-US"])
        max_retries: 最大リトライ回数
    
    Returns:
        {
            "success": bool,
            "text": str,
            "detected_language": str,
            "error": str (optional)
        }
    """
    if not AZURE_SPEECH_KEY:
        return {
            "success": False,
            "text": "",
            "detected_language": "",
            "error": "AZURE_SPEECH_KEY is not set",
        }
    
    client = await get_client()
    
    # リトライ処理（VoiceRecorderと同じ指数バックオフ）
    import asyncio
    
    for attempt in range(max_retries):
        try:
            # multipart/form-data リクエスト
            definition = {
                "locales": locales,
                "format": "Display",
            }
            
            files = {
                "audio": ("audio.wav", audio_bytes, "audio/wav"),
            }
            data = {
                "definition": json.dumps(definition),
            }
            headers = {
                "Ocp-Apim-Subscription-Key": AZURE_SPEECH_KEY,
                "Ocp-Apim-Subscription-Region": AZURE_SPEECH_REGION,
            }
            
            response = await client.post(
                ENDPOINT,
                files=files,
                data=data,
                headers=headers,
            )
            
            if response.status_code == 200:
                result = response.json()
                
                # 結果を解析
                text = ""
                detected_language = locales[0]  # デフォルト
                
                if "combinedPhrases" in result and result["combinedPhrases"]:
                    text = result["combinedPhrases"][0].get("text", "")
                
                if "phrases" in result and result["phrases"]:
                    detected_language = result["phrases"][0].get("locale", detected_language)
                
                return {
                    "success": True,
                    "text": text,
                    "detected_language": detected_language,
                }
            
            elif response.status_code in [429, 500, 502, 503, 504]:
                # リトライ可能なエラー
                wait_time = 2 ** attempt  # 1s, 2s, 4s
                print(f"STT API error {response.status_code}, retrying in {wait_time}s...")
                await asyncio.sleep(wait_time)
                continue
            
            else:
                return {
                    "success": False,
                    "text": "",
                    "detected_language": "",
                    "error": f"API error: {response.status_code} - {response.text}",
                }
                
        except httpx.TimeoutException:
            if attempt < max_retries - 1:
                wait_time = 2 ** attempt
                print(f"STT timeout, retrying in {wait_time}s...")
                await asyncio.sleep(wait_time)
                continue
            return {
                "success": False,
                "text": "",
                "detected_language": "",
                "error": "Request timeout",
            }
        
        except Exception as e:
            return {
                "success": False,
                "text": "",
                "detected_language": "",
                "error": str(e),
            }
    
    return {
        "success": False,
        "text": "",
        "detected_language": "",
        "error": "Max retries exceeded",
    }

