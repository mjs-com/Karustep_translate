export interface Message {
  id: string
  role: 'doctor' | 'patient' | 'system'
  originalText: string
  translatedText: string
  detectedLanguage: string
  timestamp: Date
}

interface ChatMessageProps {
  message: Message
}

export function ChatMessage({ message }: ChatMessageProps) {
  const isDoctor = message.role === 'doctor'
  const isSystem = message.role === 'system'
  
  if (isSystem) {
    return (
      <div className="flex justify-center message-enter">
        <div className="bg-slate-700/50 text-slate-400 px-4 py-2 rounded-full text-sm">
          {message.originalText}
        </div>
      </div>
    )
  }
  
  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit' })
  }
  
  return (
    <div
      className={`flex ${isDoctor ? 'justify-start' : 'justify-end'} message-enter`}
    >
      <div
        className={`max-w-[75%] rounded-2xl p-4 ${
          isDoctor
            ? 'bg-blue-600/20 border border-blue-500/30'
            : 'bg-green-600/20 border border-green-500/30'
        }`}
      >
        {/* ヘッダー */}
        <div className="flex items-center gap-2 mb-2">
          <span className="text-xl">
            {isDoctor ? '👨‍⚕️' : '🧑'}
          </span>
          <span
            className={`text-sm font-medium ${
              isDoctor ? 'text-blue-400' : 'text-green-400'
            }`}
          >
            {isDoctor ? '医師' : '患者'}
          </span>
          <span className="text-xs text-slate-500">
            {formatTime(message.timestamp)}
          </span>
        </div>
        
        {/* 原文 */}
        <p className="text-white text-lg leading-relaxed">
          {message.originalText}
        </p>
        
        {/* 翻訳文 */}
        {message.translatedText && (
          <div className="mt-3 pt-3 border-t border-slate-600/50">
            <p className="text-sm text-slate-400 mb-1">翻訳:</p>
            <p className={`text-base ${isDoctor ? 'text-blue-200' : 'text-green-200'}`}>
              {message.translatedText}
            </p>
          </div>
        )}
        
        {/* 言語タグ */}
        <div className="mt-2 flex items-center gap-2">
          <span className="text-xs bg-slate-700/50 text-slate-400 px-2 py-1 rounded-full">
            {message.detectedLanguage}
          </span>
        </div>
      </div>
    </div>
  )
}

