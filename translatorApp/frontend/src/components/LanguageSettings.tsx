interface LanguageOption {
  code: string
  displayName: string
  flag: string
}

const SUPPORTED_LANGUAGES: LanguageOption[] = [
  { code: 'ja-JP', displayName: '日本語', flag: '🇯🇵' },
  { code: 'en-US', displayName: 'English (US)', flag: '🇺🇸' },
  { code: 'en-GB', displayName: 'English (UK)', flag: '🇬🇧' },
  { code: 'zh-CN', displayName: '中文（简体）', flag: '🇨🇳' },
  { code: 'zh-TW', displayName: '中文（繁體）', flag: '🇹🇼' },
  { code: 'ko-KR', displayName: '한국어', flag: '🇰🇷' },
  { code: 'es-ES', displayName: 'Español', flag: '🇪🇸' },
  { code: 'pt-BR', displayName: 'Português', flag: '🇧🇷' },
  { code: 'vi-VN', displayName: 'Tiếng Việt', flag: '🇻🇳' },
  { code: 'th-TH', displayName: 'ภาษาไทย', flag: '🇹🇭' },
  { code: 'tl-PH', displayName: 'Tagalog', flag: '🇵🇭' },
]

interface LanguageSettingsProps {
  doctorLanguage: string
  patientLanguage: string
  onDoctorLanguageChange: (lang: string) => void
  onPatientLanguageChange: (lang: string) => void
}

export function LanguageSettings({
  doctorLanguage,
  patientLanguage,
  onDoctorLanguageChange,
  onPatientLanguageChange,
}: LanguageSettingsProps) {
  const doctorLang = SUPPORTED_LANGUAGES.find(l => l.code === doctorLanguage)
  const patientLang = SUPPORTED_LANGUAGES.find(l => l.code === patientLanguage)
  
  return (
    <div className="bg-slate-800/50 backdrop-blur-sm rounded-2xl border border-slate-700 p-6">
      <h2 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
        <span className="text-xl">⚙️</span>
        言語設定
      </h2>
      
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {/* 医師の言語 */}
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm font-medium text-slate-300">
            <span className="text-2xl">👨‍⚕️</span>
            医師の言語
          </label>
          <select
            value={doctorLanguage}
            onChange={(e) => onDoctorLanguageChange(e.target.value)}
            className="w-full bg-slate-700/50 border border-slate-600 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent transition-all"
          >
            {SUPPORTED_LANGUAGES.map((lang) => (
              <option key={lang.code} value={lang.code}>
                {lang.flag} {lang.displayName}
              </option>
            ))}
          </select>
        </div>
        
        {/* 患者の言語 */}
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm font-medium text-slate-300">
            <span className="text-2xl">🧑</span>
            患者の言語
          </label>
          <select
            value={patientLanguage}
            onChange={(e) => onPatientLanguageChange(e.target.value)}
            className="w-full bg-slate-700/50 border border-slate-600 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent transition-all"
          >
            {SUPPORTED_LANGUAGES.map((lang) => (
              <option key={lang.code} value={lang.code}>
                {lang.flag} {lang.displayName}
              </option>
            ))}
          </select>
        </div>
      </div>
      
      {/* 現在の設定表示 */}
      <div className="mt-4 p-4 bg-slate-700/30 rounded-xl border border-slate-600">
        <p className="text-sm text-slate-400 text-center">
          <span className="text-white font-medium">
            {doctorLang?.flag} {doctorLang?.displayName}
          </span>
          <span className="mx-3 text-xl">⇔</span>
          <span className="text-white font-medium">
            {patientLang?.flag} {patientLang?.displayName}
          </span>
        </p>
      </div>
    </div>
  )
}

