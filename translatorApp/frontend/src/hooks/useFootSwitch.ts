import { useEffect, useCallback } from 'react'

interface UseFootSwitchOptions {
  onToggle: () => void
  enabled?: boolean
}

/**
 * フットスイッチ（Ctrl+Shift+,）を検出するフック
 * POC-1で検証済みのキーコードを使用
 */
export function useFootSwitch({ onToggle, enabled = true }: UseFootSwitchOptions) {
  const handleKeyDown = useCallback((event: KeyboardEvent) => {
    if (!enabled) return
    
    // フットスイッチ: Ctrl+Shift+, (Comma, keyCode=188)
    // POC-1で検証済みのキーコード
    const isFootSwitch = event.ctrlKey && event.shiftKey && event.code === 'Comma'
    
    if (isFootSwitch && !event.repeat) {
      event.preventDefault()
      console.log('🦶 フットスイッチ検出 (Ctrl+Shift+,)')
      onToggle()
    }
  }, [onToggle, enabled])
  
  useEffect(() => {
    if (!enabled) return
    
    document.addEventListener('keydown', handleKeyDown)
    
    // クリーンアップ
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [handleKeyDown, enabled])
  
  // フォーカス状態の監視（デバッグ用）
  useEffect(() => {
    const handleFocus = () => {
      console.log('🔵 ウィンドウにフォーカス - フットスイッチ有効')
    }
    
    const handleBlur = () => {
      console.log('⚪ ウィンドウがフォーカスを失いました - フットスイッチ無効')
    }
    
    window.addEventListener('focus', handleFocus)
    window.addEventListener('blur', handleBlur)
    
    return () => {
      window.removeEventListener('focus', handleFocus)
      window.removeEventListener('blur', handleBlur)
    }
  }, [])
}

