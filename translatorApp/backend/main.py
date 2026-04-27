"""
TranslatorApp Backend
=====================
FastAPI backend for medical translation app.
Uses Azure STT, DeepL API, and Azure OpenAI.
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import base64
import os
import traceback
from dotenv import load_dotenv

from services.speech_to_text import transcribe_audio
from services.translator import translate_text
from services.language_mapper import LanguageRoleMapper

# Load environment variables
load_dotenv()

# デバッグ: 環境変数の確認
print("=" * 50)
print("🔧 環境変数チェック")
print(f"  AZURE_SPEECH_KEY: {'設定済み ✅' if os.getenv('AZURE_SPEECH_KEY') else '未設定 ❌'}")
print(f"  AZURE_SPEECH_REGION: {os.getenv('AZURE_SPEECH_REGION', '未設定')}")
print(f"  DEEPL_API_KEY: {'設定済み ✅' if os.getenv('DEEPL_API_KEY') else '未設定 ❌'}")
print("=" * 50)

app = FastAPI(
    title="TranslatorApp API",
    description="Medical Translation API with real-time transcription",
    version="0.1.0",
)

# CORS設定
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000"],  # Vite dev server
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


class TranscribeRequest(BaseModel):
    """文字起こし＋翻訳リクエスト"""
    audio: str  # Base64 encoded WAV
    doctor_language: str = "ja-JP"
    patient_language: str = "en-US"


class TranscribeResponse(BaseModel):
    """文字起こし＋翻訳レスポンス"""
    original_text: str
    translated_text: str
    detected_language: str
    speaker_role: str  # "doctor" or "patient"
    target_language: str


@app.get("/")
async def root():
    """ヘルスチェック"""
    return {"status": "ok", "service": "TranslatorApp API"}


@app.get("/api/health")
async def health_check():
    """API健全性チェック"""
    return {
        "status": "healthy",
        "azure_stt": bool(os.getenv("AZURE_SPEECH_KEY")),
        "deepl": bool(os.getenv("DEEPL_API_KEY")),
        "azure_openai": bool(os.getenv("AZURE_OPENAI_API_KEY")),
    }


@app.post("/api/transcribe-and-translate", response_model=TranscribeResponse)
async def transcribe_and_translate(request: TranscribeRequest):
    """
    音声を文字起こしして翻訳する
    
    1. Base64デコード
    2. Azure STTで文字起こし（言語自動検出）
    3. 検出言語からロール（医師/患者）を判定
    4. DeepLで翻訳
    """
    try:
        print(f"📥 リクエスト受信: doctor_lang={request.doctor_language}, patient_lang={request.patient_language}")
        print(f"📦 音声データサイズ: {len(request.audio)} bytes (Base64)")
        
        # 1. Base64デコード
        try:
            audio_bytes = base64.b64decode(request.audio)
            print(f"✅ Base64デコード成功: {len(audio_bytes)} bytes")
        except Exception as e:
            print(f"❌ Base64デコードエラー: {e}")
            raise HTTPException(status_code=400, detail=f"Invalid base64 audio data: {e}")
        
        # 2. 言語ロールマッパーを初期化
        mapper = LanguageRoleMapper(
            doctor_language=request.doctor_language,
            patient_language=request.patient_language,
        )
        
        # 3. Azure STTで文字起こし
        locales = [request.doctor_language, request.patient_language]
        print(f"🎤 Azure STT呼び出し: locales={locales}")
        stt_result = await transcribe_audio(audio_bytes, locales)
        
        print(f"📊 STT結果: success={stt_result.get('success')}, text_length={len(stt_result.get('text', ''))}")
        
        if not stt_result["success"]:
            error_msg = stt_result.get("error", "STT failed")
            print(f"❌ STT失敗: {error_msg}")
            raise HTTPException(status_code=500, detail=f"STT failed: {error_msg}")
        
        original_text = stt_result["text"]
        detected_language = stt_result["detected_language"]
        print(f"✅ 文字起こし成功: '{original_text[:50]}...', detected_lang={detected_language}")
        
        # 4. ロール判定
        speaker_role = mapper.get_role(detected_language)
        target_language = mapper.get_translation_target(speaker_role)
        
        # 5. DeepLで翻訳
        print(f"🌐 DeepL翻訳: {detected_language} → {target_language}")
        translated_text = await translate_text(
            text=original_text,
            source_lang=detected_language,
            target_lang=target_language,
        )
        print(f"✅ 翻訳成功: '{translated_text[:50]}...'")
        
        print("=" * 50)
        print("✅ 処理完了!")
        print("=" * 50)
        
        return TranscribeResponse(
            original_text=original_text,
            translated_text=translated_text,
            detected_language=detected_language,
            speaker_role=speaker_role,
            target_language=target_language,
        )
        
    except HTTPException:
        raise
    except Exception as e:
        # 詳細なエラーログ
        print("=" * 50)
        print("❌ エラー発生!")
        print(f"エラー内容: {e}")
        print("スタックトレース:")
        traceback.print_exc()
        print("=" * 50)
        raise HTTPException(status_code=500, detail=str(e))


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000, reload=True)

