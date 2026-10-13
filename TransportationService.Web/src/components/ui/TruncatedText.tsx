import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import './TruncatedText.css'

interface TruncatedTextProps {
  /** Visible content; styled children (muted parts, separators) are fine. */
  children: ReactNode
  /**
   * Full text for the native tooltip. Shown ONLY when the content actually overflows (ellipsis),
   * so a value that is fully visible never gets a tooltip repeating it. With `alwaysShowTooltip`
   * the tooltip is shown regardless — for a tooltip that carries MORE than the visible cell.
   */
  tooltip: string
  alwaysShowTooltip?: boolean
  className?: string
}

/**
 * One-line text that cuts off with an ellipsis and reveals its full value on hover. The tooltip
 * only appears when the text really is cut: the element measures itself (scrollWidth vs
 * clientWidth) after layout and again when its size changes, so a column that grows on a wide
 * screen drops the tooltip and a column that shrinks gains it. Cheap: one ResizeObserver per
 * element, no re-render while the overflow state is unchanged. Without layout (tests, SSR) the
 * element counts as not truncated.
 */
export function TruncatedText({ children, tooltip, alwaysShowTooltip = false, className }: TruncatedTextProps) {
  const ref = useRef<HTMLSpanElement>(null)
  const [truncated, setTruncated] = useState(false)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const measure = () => setTruncated(element.scrollWidth > element.clientWidth)
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [children])

  const showTooltip = alwaysShowTooltip || truncated
  return (
    <span
      ref={ref}
      className={['ui-truncated-text', className].filter(Boolean).join(' ')}
      title={showTooltip ? tooltip : undefined}
      data-truncated={truncated ? 'true' : undefined}
    >
      {children}
    </span>
  )
}
