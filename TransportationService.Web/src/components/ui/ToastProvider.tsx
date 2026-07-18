import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import './ToastProvider.css'

export type ToastTone = 'success' | 'error' | 'info'

interface Toast {
  id: number
  message: string
  tone: ToastTone
}

interface ToastContextValue {
  showToast: (message: string, tone?: ToastTone) => void
  showSuccess: (message: string) => void
  showError: (message: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

const AUTO_DISMISS_MS = 4000

/** App-wide toast host. Wrap the application once; consume via {@link useToast}. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id))
  }, [])

  const showToast = useCallback(
    (message: string, tone: ToastTone = 'info') => {
      const id = nextId.current++
      setToasts((current) => [...current, { id, message, tone }])
      window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS)
    },
    [dismiss],
  )

  const value = useMemo<ToastContextValue>(
    () => ({
      showToast,
      showSuccess: (message) => showToast(message, 'success'),
      showError: (message) => showToast(message, 'error'),
    }),
    [showToast],
  )

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="ui-toast-region" role="region" aria-live="polite" aria-label="Meldingen">
        {toasts.map((toast) => (
          <div key={toast.id} className={`ui-toast ui-toast-${toast.tone}`} role="status">
            <span>{toast.message}</span>
            <button type="button" onClick={() => dismiss(toast.id)} aria-label="Melding sluiten">
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext)
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider')
  }
  return context
}
