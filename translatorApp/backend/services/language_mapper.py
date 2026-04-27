"""
Language Role Mapper
====================
音声から検出された言語をロール（医師/患者）にマッピングする
"""


class LanguageRoleMapper:
    """
    言語とロールのマッピングを管理するクラス
    """
    
    def __init__(self, doctor_language: str, patient_language: str):
        """
        Args:
            doctor_language: 医師の言語コード (例: "ja-JP")
            patient_language: 患者の言語コード (例: "en-US")
        """
        self.doctor_language = doctor_language
        self.patient_language = patient_language
        
        # 言語→ロールのマッピング
        self.language_to_role = {
            doctor_language: "doctor",
            patient_language: "patient",
        }
        
        # ロール→翻訳先言語のマッピング
        self.role_to_target_language = {
            "doctor": patient_language,   # 医師の発言 → 患者の言語に翻訳
            "patient": doctor_language,   # 患者の発言 → 医師の言語に翻訳
        }
    
    def get_role(self, detected_language: str) -> str:
        """
        検出された言語からロールを判定
        
        Args:
            detected_language: Azure STTが検出した言語コード
        
        Returns:
            "doctor" or "patient" or "unknown"
        """
        # 完全一致を試行
        if detected_language in self.language_to_role:
            return self.language_to_role[detected_language]
        
        # 言語ファミリーで判定（en-US と en-GB は同じ英語として扱う）
        detected_base = detected_language.split('-')[0].lower()
        
        for lang, role in self.language_to_role.items():
            lang_base = lang.split('-')[0].lower()
            if lang_base == detected_base:
                return role
        
        # 判定できない場合はdoctorとして扱う
        return "doctor"
    
    def get_translation_target(self, role: str) -> str:
        """
        ロールから翻訳先言語を取得
        
        Args:
            role: "doctor" or "patient"
        
        Returns:
            翻訳先の言語コード
        """
        return self.role_to_target_language.get(role, self.patient_language)
    
    def get_deepl_code(self, language: str) -> str:
        """
        Azure STT言語コードをDeepL言語コードに変換
        
        Args:
            language: Azure STT言語コード (例: "ja-JP", "en-US")
        
        Returns:
            DeepL言語コード (例: "JA", "EN-US")
        """
        # マッピングテーブル
        mapping = {
            "ja-JP": "JA",
            "en-US": "EN-US",
            "en-GB": "EN-GB",
            "zh-CN": "ZH",
            "zh-TW": "ZH",
            "ko-KR": "KO",
            "es-ES": "ES",
            "pt-BR": "PT-BR",
            "vi-VN": "VI",
            "th-TH": "TH",  # DeepL未対応の可能性あり
            "tl-PH": "EN-US",  # タガログ語はDeepL未対応→英語経由
        }
        
        return mapping.get(language, language.split('-')[0].upper())

