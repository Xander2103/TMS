import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  THEME_STORAGE_KEY, applyResolvedTheme, cacheThemePreference, isThemePreference, readAppliedTheme,
  readCachedThemePreference, resolveTheme, systemPrefersDark, watchSystemTheme,
} from '../themePreference'

/** Stub matchMedia (absent in jsdom) with a controllable "dark" answer and change events. */
function stubMatchMedia(dark: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  const query = {
    matches: dark,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
    removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => listeners.delete(listener),
  }
  Object.defineProperty(window, 'matchMedia', { configurable: true, writable: true, value: vi.fn(() => query) })
  return {
    emit(next: boolean) {
      query.matches = next
      for (const listener of listeners) listener({ matches: next } as MediaQueryListEvent)
    },
    listenerCount: () => listeners.size,
  }
}

describe('themePreference', () => {
  beforeEach(() => {
    window.localStorage.clear()
    delete document.documentElement.dataset.theme
    document.documentElement.style.colorScheme = ''
  })

  afterEach(() => {
    // @ts-expect-error jsdom has no matchMedia; remove the stub again
    delete window.matchMedia
  })

  it('recognises exactly system, light and dark', () => {
    expect(isThemePreference('system')).toBe(true)
    expect(isThemePreference('light')).toBe(true)
    expect(isThemePreference('dark')).toBe(true)
    expect(isThemePreference('sepia')).toBe(false)
    expect(isThemePreference(null)).toBe(false)
  })

  it('resolves explicit preferences regardless of the system, and system via prefers-color-scheme', () => {
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  it('falls back to light when matchMedia is unavailable (fallback if nothing is known)', () => {
    expect(systemPrefersDark()).toBe(false)
    expect(resolveTheme('system')).toBe('light')
    expect(watchSystemTheme(() => {})).toBeTypeOf('function')
  })

  it('reads the OS preference and reports changes while subscribed', () => {
    const media = stubMatchMedia(true)
    expect(systemPrefersDark()).toBe(true)
    const seen: boolean[] = []
    const stop = watchSystemTheme((dark) => seen.push(dark))
    media.emit(false)
    expect(seen).toEqual([false])
    stop()
    media.emit(true)
    expect(seen).toEqual([false])
    expect(media.listenerCount()).toBe(0)
  })

  it('caches only valid preferences under the key the index.html bootstrap reads', () => {
    expect(readCachedThemePreference()).toBeNull()
    cacheThemePreference('dark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(readCachedThemePreference()).toBe('dark')
    window.localStorage.setItem(THEME_STORAGE_KEY, 'neon')
    expect(readCachedThemePreference()).toBeNull()
  })

  it('applies the theme to the document root, color-scheme and the theme-color meta', () => {
    const meta = document.createElement('meta')
    meta.name = 'theme-color'
    meta.content = '#6d28d9'
    document.head.appendChild(meta)

    applyResolvedTheme('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(document.documentElement.style.colorScheme).toBe('dark')
    expect(meta.content).not.toBe('#6d28d9')
    expect(readAppliedTheme()).toBe('dark')

    applyResolvedTheme('light')
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(meta.content).toBe('#6d28d9')
    meta.remove()
  })
})
