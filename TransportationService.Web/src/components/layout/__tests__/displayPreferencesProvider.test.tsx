import { act, render, screen, waitFor } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  DisplayPreferencesProvider, resetDisplayPreferencesForTests,
} from '../DisplayPreferencesProvider'
import {
  formatDateTime, resetDateFormatPreferenceForTests, resetTimeZonePreferenceForTests,
} from '../../../utils/dates'

/**
 * C-03 / I-2: the tenant zone must be known BEFORE a routed page renders a timestamp, and a
 * failing or slow fetch must never block the app. The old shape wrote module-level state from a
 * plain effect with no gate and no subscriber, so a page whose own data resolved first rendered
 * its windows in the default zone and never corrected itself.
 *
 * The process zone is forced to America/New_York so neither the browser zone (02:00) nor UTC
 * (06:00) can be mistaken for a correct tenant reading.
 */
declare const process: { env: Record<string, string | undefined> }

const ORIGINAL_TZ = process.env.TZ

beforeAll(() => {
  process.env.TZ = 'America/New_York'
})

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ
  else process.env.TZ = ORIGINAL_TZ
})

const api = vi.hoisted(() => ({ getJson: vi.fn() }))
vi.mock('../../../api/apiClient', () => ({ apiClient: { getJson: api.getJson } }))

/** Stands in for any routed page that renders an operational timestamp. */
function Probe() {
  return <span data-testid="stamp">{formatDateTime('2026-07-15T06:00:00Z')}</span>
}

function renderGate() {
  return render(
    <DisplayPreferencesProvider fallback={<span>voorkeuren laden</span>}>
      <Probe />
    </DisplayPreferencesProvider>,
  )
}

beforeEach(() => {
  api.getJson.mockReset()
  resetDisplayPreferencesForTests()
  resetTimeZonePreferenceForTests()
  resetDateFormatPreferenceForTests()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('DisplayPreferencesProvider', () => {
  it('holds the routed content back until the preferences resolve, then renders the tenant zone', async () => {
    let resolvePrefs: (value: unknown) => void = () => {}
    api.getJson.mockReturnValue(new Promise((resolve) => { resolvePrefs = resolve }))

    renderGate()

    // Nothing that could carry a wrong-zone timestamp has rendered yet.
    expect(screen.getByText('voorkeuren laden')).toBeInTheDocument()
    expect(screen.queryByTestId('stamp')).not.toBeInTheDocument()

    await act(async () => {
      resolvePrefs({ dateFormat: 'yyyy-MM-dd', decimalSeparator: ',', timezone: 'Asia/Tokyo' })
    })

    // Tokyo, from the very first render of the page: 06:00Z is 15:00 there.
    expect(await screen.findByTestId('stamp')).toHaveTextContent('2026-07-15 15:00')
  })

  it('falls back to the defaults and still renders when the fetch fails', async () => {
    api.getJson.mockRejectedValue(new Error('offline'))

    renderGate()

    // Europe/Amsterdam + dd/MM/yyyy — the seeded backend defaults, never the browser zone.
    expect(await screen.findByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')
  })

  it('never blocks the app on a hanging request; a late answer still applies to what renders next', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let resolvePrefs: (value: unknown) => void = () => {}
    api.getJson.mockReturnValue(new Promise((resolve) => { resolvePrefs = resolve }))

    renderGate()
    expect(screen.getByText('voorkeuren laden')).toBeInTheDocument()

    await act(async () => {
      vi.advanceTimersByTime(5000)
    })

    // The gate opened on the defaults rather than holding the app hostage.
    expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 08:00')

    await act(async () => {
      resolvePrefs({ dateFormat: 'dd/MM/yyyy', timezone: 'Asia/Tokyo' })
    })

    // The late answer is applied to the formatters, so the NEXT page (a navigation, a refetch)
    // is correct. Already-mounted content is deliberately not remounted — silently throwing away
    // a half-typed form to fix a clock would be the worse trade.
    await waitFor(() => expect(screen.getByTestId('stamp')).toHaveTextContent('15/07/2026 08:00'))
    render(<Probe />)
    const stamps = await screen.findAllByTestId('stamp')
    expect(stamps[stamps.length - 1]).toHaveTextContent('15/07/2026 15:00')
  })

  it('fetches the preferences once per session, however many shells mount', async () => {
    api.getJson.mockResolvedValue({ dateFormat: 'dd/MM/yyyy', timezone: 'Europe/Amsterdam' })

    renderGate()
    renderGate()

    await screen.findAllByTestId('stamp')
    expect(api.getJson).toHaveBeenCalledTimes(1)
    expect(api.getJson).toHaveBeenCalledWith('/api/company-settings/display')
  })
})
