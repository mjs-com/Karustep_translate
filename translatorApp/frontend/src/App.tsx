import { useState, useEffect, useCallback, useRef } from 'react'
import { LanguageSettings } from './components/LanguageSettings'
import { ChatMessage, Message } from './components/ChatMessage'
import { RecordingControls } from './components/RecordingControls'
import { useAudioRecorder } from './hooks/useAudioRecorder'
import { useFootSwitch } from './hooks/useFootSwitch'

function App() {
  // 言語設定
  const [doctorLanguage, setDoctorLanguage] = useState('ja-JP')
  const [patientLanguage, setPatientLanguage] = useState('en-US')
  
  // 会話ログ
  const [messages, setMessages] = useState<Message[]>([])
  const messagesEndRef = useRef<HTMLDivElement>(null)
  
  // 録音フック
  const {
    isRecording,
    startRecording,
    stopRecording,
    audioBlob,
    error: recordingError,
  } = useAudioRecorder()
  
  // フットスイッチフック
  useFootSwitch({
    onToggle: () => {
      if (isRecording) {
        stopRecording()
      } else {
        startRecording()
      }
    },
  })
  
  // 録音完了時の処理
  useEffect(() => {
    if (audioBlob) {
      handleAudioComplete(audioBlob)
    }
  }, [audioBlob])
  
  // メッセージ追加時に自動スクロール
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])
  
  // 音声処理（文字起こし＋翻訳）
  const handleAudioComplete = useCallback(async (blob: Blob) => {
    try {
      // Base64エンコード
      const reader = new FileReader()
      reader.readAsDataURL(blob)
      reader.onloadend = async () => {
        const base64Audio = reader.result as string
        const base64Data = base64Audio.split(',')[1]
        
        // APIリクエスト
        const response = await fetch('/api/transcribe-and-translate', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            audio: base64Data,
            doctor_language: doctorLanguage,
            patient_language: patientLanguage,
          }),
        })
        
        if (!response.ok) {
          const errorText = await response.text()
          console.error('API Error:', response.status, errorText)
          throw new Error(`API request failed: ${response.status} - ${errorText}`)
        }
        
        const result = await response.json()
        console.log('API Response:', result)
        
        // メッセージを追加
        const newMessage: Message = {
          id: Date.now().toString(),
          role: result.speaker_role,
          originalText: result.original_text,
          translatedText: result.translated_text,
          detectedLanguage: result.detected_language,
          timestamp: new Date(),
        }
        
        setMessages(prev => [...prev, newMessage])
      }
    } catch (error) {
      console.error('Error processing audio:', error)
      // エラーメッセージを表示
      setMessages(prev => [...prev, {
        id: Date.now().toString(),
        role: 'system',
        originalText: 'エラーが発生しました。もう一度お試しください。',
        translatedText: '',
        detectedLanguage: 'ja-JP',
        timestamp: new Date(),
      }])
    }
  }, [doctorLanguage, patientLanguage])
  
  return (
    <div className="min-h-screen flex flex-col">
      {/* ヘッダー */}
      <header className="bg-slate-800/50 backdrop-blur-sm border-b border-slate-700 px-6 py-4">
        <div className="max-w-6xl mx-auto flex items-center justify-between">
          <h1 className="text-2xl font-bold text-white flex items-center gap-3">
            <span className="text-3xl">🏥</span>
            TranslatorApp
            <span className="text-sm font-normal text-slate-400 ml-2">医療通訳支援</span>
          </h1>
          
          {/* 接続状態 */}
          <div className="flex items-center gap-2 text-sm">
            <span className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
            <span className="text-slate-400">接続中</span>
          </div>
        </div>
      </header>
      
      {/* メインコンテンツ */}
      <main className="flex-1 max-w-6xl mx-auto w-full p-6 flex flex-col gap-6">
        {/* 言語設定 */}
        <LanguageSettings
          doctorLanguage={doctorLanguage}
          patientLanguage={patientLanguage}
          onDoctorLanguageChange={setDoctorLanguage}
          onPatientLanguageChange={setPatientLanguage}
        />
        
        {/* チャットエリア */}
        <div className="flex-1 bg-slate-800/30 backdrop-blur-sm rounded-2xl border border-slate-700 overflow-hidden flex flex-col">
          {/* チャットヘッダー */}
          <div className="px-6 py-4 border-b border-slate-700 flex items-center justify-between">
            <h2 className="text-lg font-semibold text-white">会話ログ</h2>
            <span className="text-sm text-slate-400">{messages.length} メッセージ</span>
          </div>
          
          {/* メッセージ一覧 */}
          <div className="flex-1 overflow-y-auto p-6 space-y-4 custom-scrollbar min-h-[400px]">
            {messages.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-slate-500">
                <span className="text-6xl mb-4">🎤</span>
                <p className="text-lg">フットスイッチを踏んで録音を開始してください</p>
                <p className="text-sm mt-2">または下のボタンをクリック</p>
              </div>
            ) : (
              messages.map((message) => (
                <ChatMessage key={message.id} message={message} />
              ))
            )}
            <div ref={messagesEndRef} />
          </div>
        </div>
        
        {/* 録音コントロール */}
        <RecordingControls
          isRecording={isRecording}
          onStart={startRecording}
          onStop={stopRecording}
          error={recordingError}
        />
      </main>
      
      {/* フッター */}
      <footer className="bg-slate-800/50 backdrop-blur-sm border-t border-slate-700 px-6 py-3">
        <div className="max-w-6xl mx-auto flex items-center justify-between text-sm text-slate-500">
          <span>© 2024 TranslatorApp</span>
          <span>フットスイッチ: Ctrl+Shift+,</span>
        </div>
      </footer>
    </div>
  )
}

export default App

