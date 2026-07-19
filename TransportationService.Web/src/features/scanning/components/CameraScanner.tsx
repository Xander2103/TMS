import { useEffect, useRef, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { createScanDebouncer } from '../scanDebounce'

interface DetectedBarcode {
  rawValue: string
}

interface BarcodeDetectorLike {
  detect(source: CanvasImageSource): Promise<DetectedBarcode[]>
}

interface BarcodeDetectorConstructor {
  new (options?: { formats?: string[] }): BarcodeDetectorLike
  getSupportedFormats?: () => Promise<string[]>
}

function getDetectorConstructor(): BarcodeDetectorConstructor | null {
  const candidate = (window as unknown as { BarcodeDetector?: BarcodeDetectorConstructor }).BarcodeDetector
  return candidate ?? null
}

interface CameraScannerProps {
  disabled: boolean
  onDetected: (value: string) => void
}

/**
 * Camera-based scanning via the native BarcodeDetector API where available (Android
 * Chrome and most modern mobile browsers). Devices without it keep the keyboard-wedge
 * input path — the feature degrades, it never blocks.
 */
export function CameraScanner({ disabled, onDetected }: CameraScannerProps) {
  const [active, setActive] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const supported = getDetectorConstructor() !== null && typeof navigator.mediaDevices?.getUserMedia === 'function'

  useEffect(() => {
    if (!active) return
    let cancelled = false
    const debouncer = createScanDebouncer()
    const Detector = getDetectorConstructor()
    if (!Detector) return

    const detector = new Detector({ formats: ['code_128', 'qr_code', 'ean_13', 'ean_8', 'code_39'] })
    let timer: number | undefined

    void (async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop())
          return
        }
        streamRef.current = stream
        if (videoRef.current) {
          videoRef.current.srcObject = stream
          await videoRef.current.play()
        }
        const tick = async () => {
          if (cancelled || !videoRef.current) return
          try {
            const codes = await detector.detect(videoRef.current)
            const value = codes[0]?.rawValue?.trim()
            if (value && debouncer.accept(value)) {
              onDetected(value)
            }
          } catch {
            // A single failed frame is irrelevant; the loop continues.
          }
          timer = window.setTimeout(() => void tick(), 200)
        }
        void tick()
      } catch {
        if (!cancelled) {
          setError('Camera niet beschikbaar of toegang geweigerd.')
          setActive(false)
        }
      }
    })()

    return () => {
      cancelled = true
      if (timer !== undefined) window.clearTimeout(timer)
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
    }
  }, [active, onDetected])

  if (!supported) {
    return null
  }

  return (
    <div className="scan-camera">
      {error && (
        <p className="scan-error" role="alert">
          {error}
        </p>
      )}
      {active ? (
        <>
          {/* Live camera preview: intentionally muted, no captions apply. */}
          <video ref={videoRef} className="scan-camera-view" playsInline muted />
          <Button variant="secondary" onClick={() => setActive(false)} disabled={disabled}>
            Camera stoppen
          </Button>
        </>
      ) : (
        <Button
          variant="secondary"
          onClick={() => {
            setError(null)
            setActive(true)
          }}
          disabled={disabled}
        >
          📷 Camera scannen
        </Button>
      )}
    </div>
  )
}
