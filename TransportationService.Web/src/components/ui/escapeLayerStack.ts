import { useEffect, useRef } from 'react'

/**
 * Escape-to-close for stacked overlays (Modal, SectionDrawer, …), shared like `bodyScrollLock`.
 *
 * Every overlay used to listen for Escape on `document` on its own, so one key press reached
 * all of them: Escape in a confirm dialog also closed the drawer hosting it. This module keeps
 * one stack of open layers and one document listener that hands Escape to the TOP layer only.
 * The layer below gets the next press, exactly as if the user had closed the top one with its
 * own button. A layer that is on top but chooses to ignore Escape (a busy dialog) swallows it;
 * it never falls through.
 *
 * The listener stays on the bubble phase deliberately: hosts that must keep Escape to
 * themselves (e.g. the dossier note editor) call `stopPropagation()` on React's synthetic
 * event, which stops the native event at the React root — before it reaches `document`.
 * A capture-phase listener would run first and silently break that contract.
 */
interface EscapeLayer {
  onEscape: () => void
}

const layers: EscapeLayer[] = []
let listening = false

function handleDocumentKeyDown(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  const top = layers[layers.length - 1]
  if (top) top.onEscape()
}

function pushLayer(layer: EscapeLayer): () => void {
  layers.push(layer)
  if (!listening) {
    document.addEventListener('keydown', handleDocumentKeyDown)
    listening = true
  }
  let released = false
  return () => {
    if (released) return
    released = true
    // By identity, not `pop()`: overlays do not always unmount in LIFO order (a drawer can close
    // underneath a dialog that stays open).
    const index = layers.indexOf(layer)
    if (index !== -1) layers.splice(index, 1)
    if (layers.length === 0 && listening) {
      document.removeEventListener('keydown', handleDocumentKeyDown)
      listening = false
    }
  }
}

/**
 * Registers the calling overlay as an escape layer while `active` is true. `onEscape` is read
 * through a ref, so callers may pass a fresh closure every render without re-ordering the stack.
 */
export function useEscapeLayer(active: boolean, onEscape: () => void) {
  const onEscapeRef = useRef(onEscape)
  useEffect(() => {
    onEscapeRef.current = onEscape
  })

  useEffect(() => {
    if (!active) return
    return pushLayer({ onEscape: () => onEscapeRef.current() })
  }, [active])
}

/** Number of overlays currently registered (diagnostics and tests). */
export function escapeLayerCount(): number {
  return layers.length
}
