import { useLayoutEffect, useRef, type ReactNode } from 'react'

interface RetainedHeightProps {
  /** True while the content is temporarily replaced by a placeholder (e.g. a refetch of a different record). */
  retain: boolean
  className?: string
  children: ReactNode
}

/**
 * Keeps a block from collapsing while its content is swapped for a loading placeholder.
 *
 * Why: when several sections of a long page shrink to one line in the same commit, the document
 * becomes shorter than the current scroll offset and the browser clamps the scroll position —
 * the page "jumps to the top", and the offset is not restored once the content is back. Holding
 * the last measured height as `min-height` during the placeholder window keeps the document
 * height (and therefore the viewport) exactly where it was; the reservation is released as soon
 * as real content renders again, so the layout is never forced. Sets `aria-busy` meanwhile.
 *
 * The measurement lives in a ref and the style is applied in a layout effect (before paint), so
 * neither a render-time ref read nor a state write in an effect is needed.
 */
export function RetainedHeight({ retain, className, children }: RetainedHeightProps) {
  const ref = useRef<HTMLDivElement>(null)
  const lastHeight = useRef(0)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    if (retain) {
      if (lastHeight.current > 0) element.style.minHeight = `${lastHeight.current}px`
      return
    }
    element.style.minHeight = ''
    lastHeight.current = element.offsetHeight
  })

  return (
    <div ref={ref} className={className} aria-busy={retain || undefined}>
      {children}
    </div>
  )
}
