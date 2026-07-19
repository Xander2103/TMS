import { useEffect, useRef, useState } from 'react'
import './pod.css'

interface SignaturePadProps {
  disabled?: boolean
  /** Called with a PNG data URL after every stroke, or null when cleared/empty. */
  onChange: (dataUrl: string | null) => void
}

/**
 * Minimal pointer-based signature canvas (finger/stylus/mouse). Emits a PNG data URL;
 * the backend stores it through the shared file-storage abstraction.
 */
export function SignaturePad({ disabled, onChange }: SignaturePadProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const [hasInk, setHasInk] = useState(false)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    // Fixed internal resolution; CSS scales responsively.
    canvas.width = 600
    canvas.height = 200
    const ctx = canvas.getContext('2d')
    if (ctx) {
      ctx.lineWidth = 2.5
      ctx.lineCap = 'round'
      ctx.lineJoin = 'round'
      ctx.strokeStyle = '#1d1d1f'
    }
  }, [])

  function pointFromEvent(event: React.PointerEvent<HTMLCanvasElement>): { x: number; y: number } {
    const canvas = canvasRef.current!
    const rect = canvas.getBoundingClientRect()
    return {
      x: ((event.clientX - rect.left) / rect.width) * canvas.width,
      y: ((event.clientY - rect.top) / rect.height) * canvas.height,
    }
  }

  function handleDown(event: React.PointerEvent<HTMLCanvasElement>) {
    if (disabled) return
    drawing.current = true
    canvasRef.current?.setPointerCapture(event.pointerId)
    const ctx = canvasRef.current?.getContext('2d')
    if (!ctx) return
    const { x, y } = pointFromEvent(event)
    ctx.beginPath()
    ctx.moveTo(x, y)
  }

  function handleMove(event: React.PointerEvent<HTMLCanvasElement>) {
    if (!drawing.current || disabled) return
    const ctx = canvasRef.current?.getContext('2d')
    if (!ctx) return
    const { x, y } = pointFromEvent(event)
    ctx.lineTo(x, y)
    ctx.stroke()
  }

  function handleUp() {
    if (!drawing.current) return
    drawing.current = false
    setHasInk(true)
    onChange(canvasRef.current?.toDataURL('image/png') ?? null)
  }

  function clear() {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (canvas && ctx) {
      ctx.clearRect(0, 0, canvas.width, canvas.height)
    }
    setHasInk(false)
    onChange(null)
  }

  return (
    <div className="pod-signature">
      <canvas
        ref={canvasRef}
        className="pod-signature-canvas"
        onPointerDown={handleDown}
        onPointerMove={handleMove}
        onPointerUp={handleUp}
        onPointerLeave={handleUp}
        aria-label="Handtekeningveld"
      />
      <div className="pod-signature-bar">
        <span>{hasInk ? 'Handtekening gezet' : 'Laat hier tekenen'}</span>
        <button type="button" onClick={clear} disabled={disabled || !hasInk}>
          Wissen
        </button>
      </div>
    </div>
  )
}
