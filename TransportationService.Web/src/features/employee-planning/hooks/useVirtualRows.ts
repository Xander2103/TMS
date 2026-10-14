import { useCallback, useEffect, useMemo, useState, type RefObject } from 'react'

export interface VirtualRowsOptions {
  /** The scroll container (overflow: auto) that holds the rows. */
  scrollRef: RefObject<HTMLElement | null>
  rowCount: number
  /** Height guess per row before it has been measured; keyed by index. */
  estimateRow: (index: number) => number
  /** Rows rendered above/below the viewport to keep fast scrolling smooth. Default 6. */
  overscan?: number
  /** Below this many rows everything is rendered: virtualization is pure overhead for small teams. */
  threshold?: number
  /** Stable keys so measurements survive re-ordering/filtering. */
  rowKey: (index: number) => string
  /**
   * B49: identifies the row layout (e.g. the density). Measured heights belong to one layout; when
   * it changes they are discarded so the new estimate — not a stale normal-density height — sizes
   * the spacers until the rows are measured again.
   */
  layoutKey?: string
}

export interface VirtualRowsResult {
  /** First and one-past-last index to render. */
  start: number
  end: number
  /** Spacer heights (px) replacing the rows above and below the rendered window. */
  topPad: number
  bottomPad: number
  /** Whether windowing is active (false below the threshold). */
  active: boolean
  /** Ref callback for a rendered row element: measures its real height. */
  measure: (index: number) => (element: HTMLElement | null) => void
}

/**
 * Minimal row virtualization for a table inside a scroll container. Heights start from a
 * data-driven estimate and are corrected by measuring rendered rows, so variable-height rows
 * (several chips per day) stay accurate without a dependency. Spacer rows above and below the
 * window keep the scrollbar honest; sticky header/column styling is untouched because the
 * table itself is still a plain table.
 */
export function useVirtualRows({
  scrollRef,
  rowCount,
  estimateRow,
  overscan = 6,
  threshold = 60,
  rowKey,
  layoutKey = '',
}: VirtualRowsOptions): VirtualRowsResult {
  const active = rowCount > threshold
  const [viewport, setViewport] = useState({ scrollTop: 0, height: 0 })
  // Heights are stored with the layout they were measured in; another layout reads as "unmeasured".
  const [measuredState, setMeasuredState] = useState<{ layoutKey: string; heights: Record<string, number> }>({ layoutKey, heights: {} })
  const measured = useMemo(
    () => (measuredState.layoutKey === layoutKey ? measuredState.heights : {}),
    [measuredState, layoutKey],
  )
  // Header rows sit inside the same scroller and take part of its height; measured from the
  // first rendered row's offset instead of hardcoding header heights.
  const [headerOffset, setHeaderOffset] = useState(0)

  useEffect(() => {
    const element = scrollRef.current
    if (!element || !active) return
    let frame = 0
    const update = () => {
      frame = 0
      setViewport({ scrollTop: element.scrollTop, height: element.clientHeight })
    }
    update()
    const onScroll = () => {
      if (frame === 0) frame = window.requestAnimationFrame(update)
    }
    element.addEventListener('scroll', onScroll, { passive: true })
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(onScroll) : null
    observer?.observe(element)
    return () => {
      element.removeEventListener('scroll', onScroll)
      observer?.disconnect()
      if (frame !== 0) window.cancelAnimationFrame(frame)
    }
  }, [scrollRef, active])

  const heightOf = useCallback(
    (index: number) => measured[rowKey(index)] ?? estimateRow(index),
    [measured, rowKey, estimateRow],
  )

  const { start, end, topPad, bottomPad } = useMemo(() => {
    if (!active) return { start: 0, end: rowCount, topPad: 0, bottomPad: 0 }
    const top = Math.max(0, viewport.scrollTop - headerOffset)
    const bottom = top + Math.max(viewport.height, 1)
    let offset = 0
    let first = 0
    while (first < rowCount && offset + heightOf(first) < top) {
      offset += heightOf(first)
      first += 1
    }
    const renderFrom = Math.max(0, first - overscan)
    let topPadding = 0
    for (let i = 0; i < renderFrom; i += 1) topPadding += heightOf(i)

    let last = first
    let visibleOffset = offset
    while (last < rowCount && visibleOffset < bottom) {
      visibleOffset += heightOf(last)
      last += 1
    }
    const renderTo = Math.min(rowCount, last + overscan)
    let bottomPadding = 0
    for (let i = renderTo; i < rowCount; i += 1) bottomPadding += heightOf(i)
    return { start: renderFrom, end: renderTo, topPad: topPadding, bottomPad: bottomPadding }
  }, [active, rowCount, viewport, headerOffset, heightOf, overscan])

  const measure = useCallback(
    (index: number) => (element: HTMLElement | null) => {
      // Ref callbacks run at commit time, so reading layout here is safe.
      if (!element || !active) return
      const key = rowKey(index)
      const height = element.getBoundingClientRect().height
      if (height === 0) return
      if (index === start) {
        const scroller = scrollRef.current
        if (scroller) {
          const rowTop = element.getBoundingClientRect().top - scroller.getBoundingClientRect().top + scroller.scrollTop
          const offset = Math.max(0, rowTop - topPad)
          setHeaderOffset((current) => (Math.abs(current - offset) > 0.5 ? offset : current))
        }
      }
      setMeasuredState((current) => {
        const heights = current.layoutKey === layoutKey ? current.heights : {}
        if (current.layoutKey === layoutKey && Math.abs((heights[key] ?? 0) - height) <= 0.5) return current
        return { layoutKey, heights: { ...heights, [key]: height } }
      })
    },
    [active, rowKey, start, topPad, scrollRef, layoutKey],
  )

  return { start, end, topPad, bottomPad, active, measure }
}
