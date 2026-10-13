import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { TruncatedText } from '../TruncatedText'

/**
 * jsdom has no layout: scrollWidth/clientWidth are 0, so the element is "not truncated" unless
 * the test forces the overflow through the prototype getters.
 */
function forceOverflow(scrollWidth: number, clientWidth: number) {
  Object.defineProperty(HTMLElement.prototype, 'scrollWidth', { configurable: true, get: () => scrollWidth })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => clientWidth })
}

describe('TruncatedText', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    forceOverflow(0, 0)
  })

  it('shows no tooltip while the whole value is visible', () => {
    forceOverflow(120, 200)
    render(<TruncatedText tooltip="Van Caudenberg Internationale Transporten NV">Van Caudenberg Internationale Transporten NV</TruncatedText>)
    const element = screen.getByText('Van Caudenberg Internationale Transporten NV')
    expect(element).not.toHaveAttribute('title')
    expect(element).not.toHaveAttribute('data-truncated')
    expect(element).toHaveClass('ui-truncated-text')
  })

  it('reveals the full value as a tooltip once the text is cut off', () => {
    forceOverflow(320, 200)
    render(<TruncatedText tooltip="Van Caudenberg Internationale Transporten NV · Klantnummer KL-0042">Van Caudenberg Internationale Transporten NV</TruncatedText>)
    const element = screen.getByText('Van Caudenberg Internationale Transporten NV')
    expect(element).toHaveAttribute('title', 'Van Caudenberg Internationale Transporten NV · Klantnummer KL-0042')
    expect(element).toHaveAttribute('data-truncated', 'true')
  })

  it('always shows a tooltip that carries more than the visible text when asked to', () => {
    forceOverflow(100, 200)
    render(
      <TruncatedText tooltip="Direct transport · Plateau" alwaysShowTooltip>
        Direct transport +1
      </TruncatedText>,
    )
    expect(screen.getByText('Direct transport +1')).toHaveAttribute('title', 'Direct transport · Plateau')
  })

  it('re-measures when its size changes (ResizeObserver) and when the content changes', () => {
    let callback: ResizeObserverCallback | null = null
    const observe = vi.fn()
    const disconnect = vi.fn()
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(cb: ResizeObserverCallback) {
          callback = cb
        }
        observe = observe
        disconnect = disconnect
        unobserve = vi.fn()
      },
    )
    forceOverflow(100, 200)
    const { rerender, unmount } = render(<TruncatedText tooltip="ABC-458">ABC-458</TruncatedText>)
    expect(observe).toHaveBeenCalledTimes(1)
    expect(screen.getByText('ABC-458')).not.toHaveAttribute('title')

    // The column shrinks: the observer fires and the tooltip appears.
    forceOverflow(300, 200)
    act(() => callback?.([], {} as ResizeObserver))
    expect(screen.getByText('ABC-458')).toHaveAttribute('title', 'ABC-458')

    // New content re-measures from scratch.
    forceOverflow(50, 200)
    rerender(<TruncatedText tooltip="XYZ">XYZ</TruncatedText>)
    expect(screen.getByText('XYZ')).not.toHaveAttribute('title')

    unmount()
    expect(disconnect).toHaveBeenCalled()
    vi.unstubAllGlobals()
  })
})
