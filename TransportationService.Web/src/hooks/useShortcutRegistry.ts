import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../features/auth/authContextValue'
import {
  SEQUENCE_TIMEOUT_MS, SHORTCUTS, isTypingTarget, matchShortcut, type ShortcutDef,
} from '../config/shortcuts'

export interface ShortcutRegistryState {
  paletteOpen: boolean
  setPaletteOpen: (open: boolean) => void
  helpOpen: boolean
  setHelpOpen: (open: boolean) => void
  /** Shortcuts the current user may use (permission-filtered), for the help dialog. */
  availableShortcuts: ShortcutDef[]
}

/**
 * The ONE window keydown listener behind the shortcut registry. Sequences ("g p") get a
 * 1.5s window; typing targets are ignored except for mod+K (reachable from a form too).
 */
export function useShortcutRegistry(): ShortcutRegistryState {
  const navigate = useNavigate()
  const { hasAnyPermission } = useAuth()
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [helpOpen, setHelpOpen] = useState(false)
  const pendingPrefix = useRef<string | null>(null)
  const prefixTimer = useRef<number | null>(null)

  useEffect(() => {
    const available = SHORTCUTS.filter(
      (shortcut) => shortcut.permissions.length === 0 || hasAnyPermission(shortcut.permissions))

    function clearPrefix() {
      pendingPrefix.current = null
      if (prefixTimer.current !== null) {
        window.clearTimeout(prefixTimer.current)
        prefixTimer.current = null
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      const mod = event.ctrlKey || event.metaKey
      if (event.altKey) return
      // While typing only mod+K stays live; plain keys belong to the field.
      if (isTypingTarget(event.target) && !(mod && event.key.toLowerCase() === 'k')) return

      const match = matchShortcut({ key: event.key, mod }, pendingPrefix.current, available)
      if (match === 'prefix') {
        event.preventDefault()
        pendingPrefix.current = event.key.toLowerCase()
        prefixTimer.current = window.setTimeout(clearPrefix, SEQUENCE_TIMEOUT_MS)
        return
      }

      clearPrefix()
      if (!match) return

      event.preventDefault()
      switch (match.action.kind) {
        case 'palette':
          setPaletteOpen((open) => !open)
          break
        case 'help':
          setHelpOpen((open) => !open)
          break
        case 'navigate':
          navigate(match.action.route)
          break
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      clearPrefix()
    }
  }, [navigate, hasAnyPermission])

  return {
    paletteOpen,
    setPaletteOpen,
    helpOpen,
    setHelpOpen,
    availableShortcuts: SHORTCUTS.filter(
      (shortcut) => shortcut.permissions.length === 0 || hasAnyPermission(shortcut.permissions)),
  }
}
