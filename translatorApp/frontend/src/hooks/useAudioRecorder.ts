import { useState, useRef, useCallback } from 'react'

interface UseAudioRecorderReturn {
  isRecording: boolean
  startRecording: () => Promise<void>
  stopRecording: () => void
  audioBlob: Blob | null
  error: string | null
}

export function useAudioRecorder(): UseAudioRecorderReturn {
  const [isRecording, setIsRecording] = useState(false)
  const [audioBlob, setAudioBlob] = useState<Blob | null>(null)
  const [error, setError] = useState<string | null>(null)
  
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const audioChunksRef = useRef<Blob[]>([])
  const streamRef = useRef<MediaStream | null>(null)
  
  const startRecording = useCallback(async () => {
    try {
      setError(null)
      setAudioBlob(null)
      audioChunksRef.current = []
      
      // マイクアクセス
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          sampleRate: 16000,
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      })
      
      streamRef.current = stream
      
      // MediaRecorder作成
      const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus'
        : 'audio/webm'
      
      const mediaRecorder = new MediaRecorder(stream, { mimeType })
      mediaRecorderRef.current = mediaRecorder
      
      mediaRecorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data)
        }
      }
      
      mediaRecorder.onstop = async () => {
        // WebMからWAVに変換
        const webmBlob = new Blob(audioChunksRef.current, { type: mimeType })
        
        try {
          const wavBlob = await convertToWav(webmBlob)
          setAudioBlob(wavBlob)
        } catch (err) {
          console.error('WAV conversion error:', err)
          setError('音声変換に失敗しました')
        }
        
        // ストリーム停止
        stream.getTracks().forEach(track => track.stop())
      }
      
      // 録音開始
      mediaRecorder.start(1000) // 1秒ごとにチャンク
      setIsRecording(true)
      
    } catch (err) {
      console.error('Recording error:', err)
      if (err instanceof Error) {
        if (err.name === 'NotAllowedError') {
          setError('マイクへのアクセスが許可されていません')
        } else {
          setError(`録音エラー: ${err.message}`)
        }
      }
    }
  }, [])
  
  const stopRecording = useCallback(() => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state !== 'inactive') {
      mediaRecorderRef.current.stop()
      setIsRecording(false)
    }
  }, [])
  
  return {
    isRecording,
    startRecording,
    stopRecording,
    audioBlob,
    error,
  }
}

// WebM → WAV変換関数
async function convertToWav(blob: Blob): Promise<Blob> {
  const arrayBuffer = await blob.arrayBuffer()
  
  // AudioContextでデコード
  const audioContext = new AudioContext({ sampleRate: 16000 })
  const audioBuffer = await audioContext.decodeAudioData(arrayBuffer)
  
  // 16kHzにリサンプリング
  const targetSampleRate = 16000
  const offlineCtx = new OfflineAudioContext(
    1,
    audioBuffer.duration * targetSampleRate,
    targetSampleRate
  )
  
  const source = offlineCtx.createBufferSource()
  source.buffer = audioBuffer
  source.connect(offlineCtx.destination)
  source.start()
  
  const renderedBuffer = await offlineCtx.startRendering()
  
  // WAVエンコード
  const wavBuffer = encodeWav(renderedBuffer)
  
  await audioContext.close()
  
  return new Blob([wavBuffer], { type: 'audio/wav' })
}

// WAVエンコーダー
function encodeWav(audioBuffer: AudioBuffer): ArrayBuffer {
  const numChannels = 1
  const sampleRate = audioBuffer.sampleRate
  const format = 1 // PCM
  const bitDepth = 16
  
  const samples = audioBuffer.getChannelData(0)
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  
  // WAVヘッダー
  writeString(view, 0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeString(view, 8, 'WAVE')
  writeString(view, 12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, format, true)
  view.setUint16(22, numChannels, true)
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * numChannels * bitDepth / 8, true)
  view.setUint16(32, numChannels * bitDepth / 8, true)
  view.setUint16(34, bitDepth, true)
  writeString(view, 36, 'data')
  view.setUint32(40, samples.length * 2, true)
  
  // サンプルデータ（float32 → int16）
  let offset = 44
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]))
    view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7FFF, true)
    offset += 2
  }
  
  return buffer
}

function writeString(view: DataView, offset: number, string: string) {
  for (let i = 0; i < string.length; i++) {
    view.setUint8(offset + i, string.charCodeAt(i))
  }
}

