/**
 * Haptic/audio feedback abstraction for scan results. The interface is the extension point:
 * a native wrapper (or future audio implementation) can swap in richer signals without the
 * scan UI changing. The default implementation uses the Vibration API where available and
 * degrades to a no-op silently (e.g. desktop browsers, iOS Safari).
 */
export interface ScanSignal {
  success(): void
  warning(): void
}

function vibrate(pattern: number[]): void {
  if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
    navigator.vibrate(pattern)
  }
}

export const deviceScanSignal: ScanSignal = {
  success: () => vibrate([40]),
  warning: () => vibrate([90, 60, 90]),
}
