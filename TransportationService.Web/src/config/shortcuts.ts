/**
 * Central keyboard-shortcut registry. One window listener (installed by useShortcutRegistry)
 * interprets these — no scattered document listeners. Sequences use "g x" chords; "mod" is
 * Ctrl on Windows/Linux and Cmd on macOS.
 */

export interface ShortcutDef {
  /** "mod+k", "g p", "/", "?" */
  keys: string
  label: string
  action: { kind: 'palette' } | { kind: 'help' } | { kind: 'navigate'; route: string }
  /** Any-of permissions; empty = any signed-in user. */
  permissions: string[]
}

export const SHORTCUTS: ShortcutDef[] = [
  { keys: 'mod+k', label: 'Globaal zoeken / commando’s', action: { kind: 'palette' }, permissions: [] },
  { keys: '/', label: 'Zoeken focussen', action: { kind: 'palette' }, permissions: [] },
  { keys: '?', label: 'Deze hulp tonen', action: { kind: 'help' }, permissions: [] },
  { keys: 'g p', label: 'Planbord', action: { kind: 'navigate', route: '/planning-center' }, permissions: ['planning.view'] },
  { keys: 'g o', label: 'Operationeel centrum', action: { kind: 'navigate', route: '/operations' }, permissions: ['operations.view'] },
  { keys: 'g d', label: 'Dossiers', action: { kind: 'navigate', route: '/dossiers' }, permissions: ['dossiers.view'] },
  { keys: 'g i', label: 'Incidenten', action: { kind: 'navigate', route: '/incidents' }, permissions: ['incidents.view'] },
  { keys: 'g f', label: 'Rendement', action: { kind: 'navigate', route: '/profitability' }, permissions: ['profitability.view'] },
  { keys: 'g w', label: 'Magazijnen', action: { kind: 'navigate', route: '/warehouses' }, permissions: ['warehouse.view', 'warehouse.manage'] },
]

export const SEQUENCE_TIMEOUT_MS = 1500

export interface KeyStroke {
  key: string
  mod: boolean
}

/** True when the event target is a place where the user is typing. */
export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
}

/**
 * Matches a stroke (with optional pending prefix) against the registry.
 * Returns the matched shortcut, 'prefix' when the stroke starts a sequence, or null.
 */
export function matchShortcut(
  stroke: KeyStroke,
  pendingPrefix: string | null,
  shortcuts: ShortcutDef[] = SHORTCUTS,
): ShortcutDef | 'prefix' | null {
  const token = stroke.mod ? `mod+${stroke.key.toLowerCase()}` : stroke.key.toLowerCase()

  if (pendingPrefix) {
    const sequence = `${pendingPrefix} ${token}`
    return shortcuts.find((s) => s.keys === sequence) ?? null
  }

  const direct = shortcuts.find((s) => s.keys === token || (token === '?' && s.keys === '?'))
  if (direct) return direct

  const isPrefix = shortcuts.some((s) => s.keys.startsWith(`${token} `))
  return isPrefix ? 'prefix' : null
}
