interface RecordingControlsProps {
  isRecording: boolean
  onStart: () => void
  onStop: () => void
  error?: string | null
}

export function RecordingControls({
  isRecording,
  onStart,
  onStop,
  error,
}: RecordingControlsProps) {
  return (
    <div className="bg-slate-800/50 backdrop-blur-sm rounded-2xl border border-slate-700 p-6">
      <div className="flex flex-col items-center gap-4">
        {/* 録音ボタン */}
        <button
          onClick={isRecording ? onStop : onStart}
          className={`w-24 h-24 rounded-full flex items-center justify-center transition-all duration-300 ${
            isRecording
              ? 'bg-red-600 hover:bg-red-700 recording-pulse'
              : 'bg-slate-700 hover:bg-slate-600 hover:scale-105'
          }`}
        >
          {isRecording ? (
            <svg className="w-10 h-10 text-white" fill="currentColor" viewBox="0 0 24 24">
              <rect x="6" y="6" width="12" height="12" rx="2" />
            </svg>
          ) : (
            <svg className="w-10 h-10 text-white" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3z" />
              <path d="M17 11c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
            </svg>
          )}
        </button>
        
        {/* 状態表示 */}
        <div className="text-center">
          <p className={`text-lg font-medium ${isRecording ? 'text-red-400' : 'text-slate-400'}`}>
            {isRecording ? '🔴 録音中...' : '⏹ 停止中'}
          </p>
          <p className="text-sm text-slate-500 mt-1">
            フットスイッチ (Ctrl+Shift+,) または ボタンクリックで操作
          </p>
        </div>
        
        {/* エラー表示 */}
        {error && (
          <div className="bg-red-500/20 border border-red-500/50 text-red-400 px-4 py-2 rounded-lg text-sm">
            ⚠️ {error}
          </div>
        )}
        
        {/* 録音インジケーター */}
        {isRecording && (
          <div className="flex items-center gap-1">
            {[...Array(5)].map((_, i) => (
              <div
                key={i}
                className="w-1 bg-red-500 rounded-full animate-pulse"
                style={{
                  height: `${Math.random() * 20 + 10}px`,
                  animationDelay: `${i * 0.1}s`,
                }}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

