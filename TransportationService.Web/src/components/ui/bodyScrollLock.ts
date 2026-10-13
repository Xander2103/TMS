/**
 * Ref-counted scroll lock on `document.body`, shared by every overlay (Modal, SectionDrawer, …).
 *
 * The document body is the app's scroller. Overlays used to write `body.style.overflow`
 * themselves, each with its own idea of "the previous value". React unmounts a parent's effects
 * before its children's, so a drawer closing together with its confirm dialog restored `''` first
 * and the dialog then restored the `'hidden'` it had captured — leaving the page unscrollable.
 * One counter removes the ordering problem: the first lock remembers the author's value, the
 * last release puts it back, and whatever happens in between is irrelevant.
 */
let lockCount = 0
let originalOverflow = ''

/**
 * Locks body scrolling until the returned function is called. The release function is
 * idempotent, so it is safe as an effect cleanup that may run more than once.
 */
export function lockBodyScroll(): () => void {
  if (lockCount === 0) {
    originalOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
  }
  lockCount++

  let released = false
  return () => {
    if (released) return
    released = true
    lockCount--
    if (lockCount === 0) {
      document.body.style.overflow = originalOverflow
      originalOverflow = ''
    }
  }
}

/** Number of overlays currently holding the lock (diagnostics and tests). */
export function bodyScrollLockCount(): number {
  return lockCount
}
