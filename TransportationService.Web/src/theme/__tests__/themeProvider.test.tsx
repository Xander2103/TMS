import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ThemeProvider } from '../ThemeProvider'
import { ThemeSwitcher } from '../ThemeSwitcher'
import { AppearanceSettings } from '../AppearanceSettings'
import { useTheme } from '../themeContext'
import { THEME_STORAGE_KEY } from '../themePreference'
import { ToastContext, type ToastContextValue } from '../../components/ui/toastContext'

/**
 * Per-user appearance preference. The provider is keyed on the SESSION (user id): the saved
 * preference from /api/auth/me is authoritative for a signed-in user, the ts.theme cache only
 * bridges the start-up window, and a switch made in this tab never leaks into another user's
 * session on the same device.
 */
const api = vi.hoisted(() => ({ putJson: vi.fn() }))
vi.mock('../../api/apiClient', () => ({ apiClient: { putJson: api.putJson } }))

const toast: ToastContextValue = { showError: vi.fn(), showSuccess: vi.fn(), showToast: vi.fn() }

let systemDark = false
const mediaListeners = new Set<(event: MediaQueryListEvent) => void>()

function Probe() {
  const { preference, resolved } = useTheme()
  return (
    <span data-testid="probe">
      {preference}/{resolved}
    </span>
  )
}

function appliedTheme() {
  return document.documentElement.dataset.theme
}

describe('ThemeProvider', () => {
  beforeEach(() => {
    window.localStorage.clear()
    delete document.documentElement.dataset.theme
    systemDark = false
    mediaListeners.clear()
    api.putJson.mockReset()
    api.putJson.mockResolvedValue({ theme: 'dark' })
    vi.mocked(toast.showError).mockReset()
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: vi.fn(() => ({
        get matches() {
          return systemDark
        },
        media: '(prefers-color-scheme: dark)',
        addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => mediaListeners.add(listener),
        removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => mediaListeners.delete(listener),
      })),
    })
  })

  afterEach(() => {
    // @ts-expect-error jsdom has no matchMedia; remove the stub again
    delete window.matchMedia
  })

  it('applies the Light preference of the signed-in user even when the OS is dark', () => {
    systemDark = true
    render(
      <ThemeProvider savedPreference="light" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('light/light')
    expect(appliedTheme()).toBe('light')
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('applies the Dark preference of the signed-in user even when the OS is light', () => {
    render(
      <ThemeProvider savedPreference="dark" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('dark/dark')
    expect(appliedTheme()).toBe('dark')
  })

  it('follows prefers-color-scheme for the System preference, including live changes', () => {
    systemDark = false
    render(
      <ThemeProvider savedPreference="system" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('system/light')
    expect(appliedTheme()).toBe('light')

    act(() => {
      systemDark = true
      for (const listener of mediaListeners) listener({ matches: true } as MediaQueryListEvent)
    })
    expect(screen.getByTestId('probe')).toHaveTextContent('system/dark')
    expect(appliedTheme()).toBe('dark')
  })

  it('falls back to System when the user never chose a preference', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, 'dark')
    render(
      <ThemeProvider savedPreference={null} sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    // A signed-in user without a saved choice follows the system; the stale device cache
    // from an earlier session must not override that.
    expect(screen.getByTestId('probe')).toHaveTextContent('system/light')
    expect(appliedTheme()).toBe('light')
  })

  it('uses the cached last preference before the session has loaded (no flash), then the saved one', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, 'dark')
    const { rerender } = render(
      <ThemeProvider savedPreference={null} sessionId={null}>
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('dark/dark')
    expect(appliedTheme()).toBe('dark')

    // /api/auth/me resolves: this user saved Light on another device — the server wins.
    rerender(
      <ThemeProvider savedPreference="light" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('light/light')
    expect(appliedTheme()).toBe('light')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
  })

  it('restores the saved preference after a fresh sign-in on a device that never saw it', () => {
    // Nothing cached (another device); the session arrives with Dark.
    const { rerender } = render(
      <ThemeProvider savedPreference={null} sessionId={null}>
        <Probe />
      </ThemeProvider>,
    )
    expect(appliedTheme()).toBe('light')
    rerender(
      <ThemeProvider savedPreference="dark" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(appliedTheme()).toBe('dark')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })

  it('switches immediately and persists on the user when chosen from the quick toggle', async () => {
    render(
      <ToastContext.Provider value={toast}>
        <ThemeProvider savedPreference="light" sessionId="u1">
          <ThemeSwitcher />
          <Probe />
        </ThemeProvider>
      </ToastContext.Provider>,
    )
    const dark = screen.getByRole('button', { name: 'Donker' })
    expect(dark).toHaveAttribute('aria-pressed', 'false')

    await userEvent.click(dark)

    expect(screen.getByTestId('probe')).toHaveTextContent('dark/dark')
    expect(appliedTheme()).toBe('dark')
    expect(dark).toHaveAttribute('aria-pressed', 'true')
    expect(api.putJson).toHaveBeenCalledWith('/api/me/theme', { theme: 'dark' })
    expect(toast.showError).not.toHaveBeenCalled()
  })

  it('keeps the local switch and warns when the server save fails', async () => {
    api.putJson.mockRejectedValue(new Error('offline'))
    render(
      <ToastContext.Provider value={toast}>
        <ThemeProvider savedPreference="light" sessionId="u1">
          <AppearanceSettings />
          <Probe />
        </ThemeProvider>
      </ToastContext.Provider>,
    )
    await userEvent.click(screen.getByRole('radio', { name: /Donker/ }))

    expect(appliedTheme()).toBe('dark')
    await waitFor(() => expect(toast.showError).toHaveBeenCalledTimes(1))
    expect(screen.getByRole('radio', { name: /Donker/ })).toBeChecked()
  })

  it('isolates the preference per user: a switch by user A never colours user B', async () => {
    const { rerender } = render(
      <ThemeProvider savedPreference="light" sessionId="user-a">
        <ThemeSwitcher />
        <Probe />
      </ThemeProvider>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Donker' }))
    expect(appliedTheme()).toBe('dark')
    expect(api.putJson).toHaveBeenCalledTimes(1)

    // A signs out, B signs in on the same device with Light saved (same value as A had!).
    rerender(
      <ThemeProvider savedPreference="light" sessionId="user-b">
        <ThemeSwitcher />
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('light/light')
    expect(appliedTheme()).toBe('light')
    // B's screen was corrected from B's OWN saved row, not by writing anything for B.
    expect(api.putJson).toHaveBeenCalledTimes(1)
  })

  it('does not persist for an anonymous session and keeps the last choice for the login screen', async () => {
    const { rerender } = render(
      <ThemeProvider savedPreference="dark" sessionId="u1">
        <Probe />
      </ThemeProvider>,
    )
    expect(appliedTheme()).toBe('dark')
    // Sign-out: the anonymous shell shows the last applied preference from this device.
    rerender(
      <ThemeProvider savedPreference={null} sessionId={null}>
        <Probe />
      </ThemeProvider>,
    )
    expect(appliedTheme()).toBe('dark')
    expect(api.putJson).not.toHaveBeenCalled()
  })

  it('exposes Systeem / Licht / Donker as the three options', () => {
    render(
      <ThemeProvider savedPreference="system" sessionId="u1">
        <AppearanceSettings />
      </ThemeProvider>,
    )
    expect(screen.getByRole('group', { name: 'Weergave' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Systeem/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Licht/ })).not.toBeChecked()
    expect(screen.getByRole('radio', { name: /Donker/ })).not.toBeChecked()
  })
})
