import { useCallback, useEffect, useRef, useState } from 'react'
import { LANGUAGE_NAMES } from '../../../i18n/languageNames'
import { LOCALES, isLocale, translate, type Locale } from '../../../i18n/translations'
import {
  KioskUnreachableError, clearDeviceKey, getStoredDeviceKey, kioskIdentify, kioskPing, kioskPunch,
  storeDeviceKey,
} from '../api/kioskApi'
import { formatTime } from '../../../utils/dates'
import type { AttendanceStatus, KioskOutcome, KioskPunchAction } from '../types'
import './kiosk.css'

const RESET_AFTER_CONFIRM_MS = 4_000
const RESET_AFTER_IDLE_MS = 25_000
const MAX_PIN_LENGTH = 8

const CLOCK_LOCALE_TAGS: Record<Locale, string> = { nl: 'nl-BE', fr: 'fr-BE', en: 'en-GB' }

type Screen =
  | { kind: 'setup'; errorKey: string | null }
  | { kind: 'pin'; errorKey: string | null }
  | { kind: 'welcome'; firstName: string; status: AttendanceStatus; token: string }
  | { kind: 'confirm'; messageKey: string; detailKey: string; time: string }
  | { kind: 'blocked'; errorKey: string }

/** Server-outcome → kiosk-foutsleutel: het scherm vertaalt zelf, nooit de servertekst tonen. */
function errorKeyForOutcome(outcome: KioskOutcome): string {
  switch (outcome) {
    case 'KioskDisabled': return 'kiosk.errors.kioskDisabled'
    case 'NotConfigured': return 'kiosk.errors.notConfigured'
    case 'TokenExpired': return 'kiosk.errors.sessionExpired'
    case 'PunchRejected': return 'kiosk.errors.punchRejected'
    case 'InvalidCode': return 'kiosk.errors.invalidCode'
    default: return 'kiosk.errors.unavailable'
  }
}

/**
 * Prikklok (spec §10–§19): fullscreen, geen ERP-shell, expliciet meertalig. Het
 * beginscherm rendert in de DEVICE-standaardtaal (uit ping) met een handmatige
 * NL|FR|EN-keuze; na een geldige identificatie schakelt het interactiescherm naar de
 * persoonlijke taal van de medewerker (pas dán bekend — privacy §18) en elke reset
 * keert terug naar de device-default. Fouten worden op OUTCOME vertaald, nooit op de
 * (Nederlandse) servertekst; zonder serverbevestiging wordt een punch nooit als
 * gelukt getoond.
 */
export function KioskPage() {
  const [deviceKey, setDeviceKey] = useState<string | null>(getStoredDeviceKey)
  const [deviceLanguage, setDeviceLanguage] = useState<Locale>('nl')
  const [locale, setLocale] = useState<Locale>('nl')
  const [screen, setScreen] = useState<Screen>({ kind: deviceKey ? 'pin' : 'setup', errorKey: null })
  const [pin, setPin] = useState('')
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(() => new Date())
  const [deviceName, setDeviceName] = useState<string | null>(null)
  const [setupKey, setSetupKey] = useState('')
  const idleTimer = useRef<number | null>(null)

  const kt = useCallback(
    (key: string, params?: Record<string, string | number>) => translate(locale, key, params),
    [locale],
  )

  // Groot live uurwerk.
  useEffect(() => {
    const handle = setInterval(() => setNow(new Date()), 1_000)
    return () => clearInterval(handle)
  }, [])

  const resetToPin = useCallback((errorKey: string | null = null, resetLanguage = true) => {
    setPin('')
    if (resetLanguage) {
      // Privacy + kiosk-UX: terug naar de standaardtaal van het device.
      setLocale((current) => (current === deviceLanguage ? current : deviceLanguage))
    }
    setScreen({ kind: 'pin', errorKey })
  }, [deviceLanguage])

  // Privacyreset: welkom-/bevestigingsscherm blijft nooit staan.
  useEffect(() => {
    if (idleTimer.current) {
      window.clearTimeout(idleTimer.current)
      idleTimer.current = null
    }

    if (screen.kind === 'confirm') {
      idleTimer.current = window.setTimeout(() => resetToPin(), RESET_AFTER_CONFIRM_MS)
    } else if (screen.kind === 'welcome') {
      idleTimer.current = window.setTimeout(() => resetToPin(), RESET_AFTER_IDLE_MS)
    }

    return () => {
      if (idleTimer.current) window.clearTimeout(idleTimer.current)
    }
  }, [screen, resetToPin])

  // Devicecheck bij opstart: naam + standaardtaal.
  useEffect(() => {
    if (!deviceKey) return
    let mounted = true
    kioskPing(deviceKey)
      .then((result) => {
        if (!mounted) return
        if (result.outcome === 'Success') {
          setDeviceName(result.deviceName)
          if (isLocale(result.defaultLanguage)) {
            setDeviceLanguage(result.defaultLanguage)
            setLocale(result.defaultLanguage)
          }
        } else if (result.outcome === 'InvalidDevice') {
          clearDeviceKey()
          setDeviceKey(null)
          setScreen({ kind: 'setup', errorKey: 'kiosk.setup.deviceRemoved' })
        } else {
          setScreen({ kind: 'blocked', errorKey: errorKeyForOutcome(result.outcome) })
        }
      })
      .catch(() => {
        if (mounted) setScreen({ kind: 'pin', errorKey: 'kiosk.errors.offlineIdentify' })
      })
    return () => {
      mounted = false
    }
  }, [deviceKey])

  const submitPin = useCallback(async () => {
    if (!deviceKey || pin.length === 0 || busy) return
    setBusy(true)
    try {
      const result = await kioskIdentify(deviceKey, pin)
      if (result.outcome === 'Success' && result.status && result.interactionToken) {
        setPin('')
        // Persoonlijke taal van de medewerker — alleen ná geldige identificatie (§18).
        if (isLocale(result.preferredLanguage)) {
          setLocale(result.preferredLanguage)
        }
        setScreen({
          kind: 'welcome',
          firstName: result.firstName ?? '',
          status: result.status,
          token: result.interactionToken,
        })
      } else if (result.outcome === 'InvalidDevice') {
        clearDeviceKey()
        setDeviceKey(null)
        setScreen({ kind: 'setup', errorKey: 'kiosk.setup.deviceRemoved' })
      } else if (result.outcome === 'KioskDisabled' || result.outcome === 'NotConfigured') {
        setScreen({ kind: 'blocked', errorKey: errorKeyForOutcome(result.outcome) })
      } else {
        resetToPin('kiosk.errors.invalidCode', false)
      }
    } catch (err) {
      resetToPin(err instanceof KioskUnreachableError ? 'kiosk.errors.offlineIdentify' : 'kiosk.errors.generic', false)
    } finally {
      setBusy(false)
    }
  }, [busy, deviceKey, pin, resetToPin])

  const punch = useCallback(
    async (action: KioskPunchAction) => {
      if (!deviceKey || screen.kind !== 'welcome' || busy) return
      setBusy(true)
      try {
        const result = await kioskPunch(deviceKey, screen.token, action)
        if (result.outcome === 'Success' && result.occurredAt) {
          const time = formatTime(result.occurredAt)
          const confirmation: Record<KioskPunchAction, { messageKey: string; detailKey: string }> = {
            ClockIn: { messageKey: 'kiosk.confirm.clockedIn', detailKey: 'kiosk.confirm.clockedInDetail' },
            ClockOut: { messageKey: 'kiosk.confirm.clockedOut', detailKey: 'kiosk.confirm.clockedOutDetail' },
            StartBreak: { messageKey: 'kiosk.confirm.breakStarted', detailKey: 'kiosk.confirm.breakStartedDetail' },
            EndBreak: { messageKey: 'kiosk.confirm.breakEnded', detailKey: 'kiosk.confirm.breakEndedDetail' },
          }
          setScreen({ kind: 'confirm', ...confirmation[action], time })
        } else if (result.outcome === 'TokenExpired') {
          resetToPin('kiosk.errors.sessionExpired')
        } else {
          resetToPin(errorKeyForOutcome(result.outcome))
        }
      } catch {
        resetToPin('kiosk.errors.offlinePunch')
      } finally {
        setBusy(false)
      }
    },
    [busy, deviceKey, resetToPin, screen],
  )

  // Fysiek toetsenbord/USB-keypad (scanners emuleren vaak een toetsenbord).
  useEffect(() => {
    if (screen.kind !== 'pin') return
    const onKeyDown = (event: KeyboardEvent) => {
      if (/^\d$/.test(event.key)) {
        setPin((current) => (current.length < MAX_PIN_LENGTH ? current + event.key : current))
      } else if (event.key === 'Backspace') {
        setPin((current) => current.slice(0, -1))
      } else if (event.key === 'Enter') {
        void submitPin()
      } else if (event.key === 'Escape') {
        setPin('')
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [screen.kind, submitPin])

  const saveSetupKey = async () => {
    const key = setupKey.trim()
    if (!key) return
    setBusy(true)
    try {
      const result = await kioskPing(key)
      if (result.outcome === 'Success') {
        storeDeviceKey(key)
        setDeviceKey(key)
        setDeviceName(result.deviceName)
        if (isLocale(result.defaultLanguage)) {
          setDeviceLanguage(result.defaultLanguage)
          setLocale(result.defaultLanguage)
        }
        setSetupKey('')
        setScreen({ kind: 'pin', errorKey: null })
      } else if (result.outcome === 'InvalidDevice') {
        setScreen({ kind: 'setup', errorKey: 'kiosk.setup.invalidKey' })
      } else {
        setScreen({ kind: 'setup', errorKey: errorKeyForOutcome(result.outcome) })
      }
    } catch {
      setScreen({ kind: 'setup', errorKey: 'kiosk.setup.offline' })
    } finally {
      setBusy(false)
    }
  }

  const clock = (
    <div className="kiosk-clock" aria-hidden="true">
      <span className="kiosk-time">
        {String(now.getHours()).padStart(2, '0')}:{String(now.getMinutes()).padStart(2, '0')}
      </span>
      <span className="kiosk-date">
        {now.toLocaleDateString(CLOCK_LOCALE_TAGS[locale], { weekday: 'long', day: 'numeric', month: 'long' })}
      </span>
      {deviceName && <span className="kiosk-device">{deviceName}</span>}
    </div>
  )

  const languageBar = (
    <div className="kiosk-langbar" role="group" aria-label="NL / FR / EN">
      {LOCALES.map((candidate) => (
        <button
          key={candidate}
          type="button"
          lang={candidate}
          className={candidate === locale ? 'kiosk-lang kiosk-lang-active' : 'kiosk-lang'}
          aria-pressed={candidate === locale}
          title={LANGUAGE_NAMES[candidate]}
          onClick={() => setLocale(candidate)}
        >
          {candidate.toUpperCase()}
        </button>
      ))}
    </div>
  )

  if (screen.kind === 'setup') {
    return (
      <main className="kiosk" lang={locale}>
        {languageBar}
        {clock}
        <section className="kiosk-panel" aria-label={kt('kiosk.setup.title')}>
          <h1 className="kiosk-heading">{kt('kiosk.setup.title')}</h1>
          <p className="kiosk-hint">{kt('kiosk.setup.hint')}</p>
          <input
            type="password"
            className="kiosk-setup-input"
            value={setupKey}
            onChange={(event) => setSetupKey(event.target.value)}
            placeholder={kt('kiosk.setup.keyPlaceholder')}
            aria-label={kt('kiosk.setup.keyLabel')}
          />
          <button type="button" className="kiosk-action kiosk-action-primary" onClick={saveSetupKey} disabled={busy}>
            {kt('kiosk.setup.register')}
          </button>
          {screen.errorKey && <p className="kiosk-error" role="alert">{kt(screen.errorKey)}</p>}
        </section>
      </main>
    )
  }

  if (screen.kind === 'blocked') {
    return (
      <main className="kiosk" lang={locale}>
        {languageBar}
        {clock}
        <section className="kiosk-panel">
          <p className="kiosk-error" role="alert">{kt(screen.errorKey)}</p>
        </section>
      </main>
    )
  }

  if (screen.kind === 'welcome') {
    const { status } = screen
    const durationUnit = locale === 'nl' ? 'u' : 'h'
    const workedToday =
      `${Math.floor(status.workedMinutesToday / 60)}${durationUnit}${String(status.workedMinutesToday % 60).padStart(2, '0')}`
    return (
      <main className="kiosk" lang={locale}>
        {clock}
        <section className="kiosk-panel" aria-label={kt('kiosk.welcome.title', { name: screen.firstName })}>
          <h1 className="kiosk-heading">{kt('kiosk.welcome.title', { name: screen.firstName })}</h1>
          {status.status === 'Working' && status.clockInAt && (
            <p className="kiosk-hint">
              {kt('kiosk.welcome.workingSince', { time: formatTime(status.clockInAt), duration: workedToday })}
            </p>
          )}
          {status.status === 'OnBreak' && status.breakStartedAt && (
            <p className="kiosk-hint">
              {kt('kiosk.welcome.breakSince', { time: formatTime(status.breakStartedAt) })}
            </p>
          )}
          {status.status === 'NotClockedIn' && <p className="kiosk-hint">{kt('kiosk.welcome.notClockedIn')}</p>}
          {status.status === 'ClockedOut' && <p className="kiosk-hint">{kt('kiosk.welcome.alreadyClockedOut')}</p>}
          <div className="kiosk-actions">
            {status.canClockIn && (
              <button type="button" className="kiosk-action kiosk-action-primary" disabled={busy} onClick={() => punch('ClockIn')}>
                {kt('kiosk.actions.clockIn')}
              </button>
            )}
            {status.canStartBreak && (
              <button type="button" className="kiosk-action" disabled={busy} onClick={() => punch('StartBreak')}>
                {kt('kiosk.actions.startBreak')}
              </button>
            )}
            {status.canEndBreak && (
              <button type="button" className="kiosk-action kiosk-action-primary" disabled={busy} onClick={() => punch('EndBreak')}>
                {kt('kiosk.actions.endBreak')}
              </button>
            )}
            {status.canClockOut && (
              <button type="button" className="kiosk-action kiosk-action-out" disabled={busy} onClick={() => punch('ClockOut')}>
                {kt('kiosk.actions.clockOut')}
              </button>
            )}
          </div>
          <button type="button" className="kiosk-back" onClick={() => resetToPin()}>
            {kt('kiosk.welcome.cancel')}
          </button>
        </section>
      </main>
    )
  }

  if (screen.kind === 'confirm') {
    return (
      <main className="kiosk" lang={locale}>
        {clock}
        <section className="kiosk-panel kiosk-confirm" role="status">
          <p className="kiosk-heading">{kt(screen.messageKey, { time: screen.time })}</p>
          <p className="kiosk-hint">{kt(screen.detailKey)}</p>
        </section>
      </main>
    )
  }

  // PIN-scherm
  const keypad: (string | 'back' | 'ok')[] = ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'back', '0', 'ok']
  return (
    <main className="kiosk" lang={locale}>
      {languageBar}
      {clock}
      <section className="kiosk-panel" aria-label={kt('kiosk.pin.prompt')}>
        <h1 className="kiosk-heading">{kt('kiosk.pin.prompt')}</h1>
        <div className="kiosk-dots" aria-label={kt('kiosk.pin.digitsEntered', { count: pin.length })} aria-live="polite">
          {pin.length === 0 && <span className="kiosk-dots-empty">● ● ● ●</span>}
          {pin.split('').map((_, index) => (
            // eslint-disable-next-line react/no-array-index-key
            <span key={index} className="kiosk-dot" aria-hidden="true">●</span>
          ))}
        </div>
        <div className="kiosk-keypad">
          {keypad.map((key) => (
            <button
              key={key}
              type="button"
              className={key === 'ok' ? 'kiosk-key kiosk-key-ok' : 'kiosk-key'}
              disabled={busy}
              aria-label={key === 'back' ? kt('kiosk.pin.erase') : key === 'ok' ? kt('kiosk.pin.confirm') : key}
              onClick={() => {
                if (key === 'back') setPin((current) => current.slice(0, -1))
                else if (key === 'ok') void submitPin()
                else setPin((current) => (current.length < MAX_PIN_LENGTH ? current + key : current))
              }}
            >
              {key === 'back' ? '←' : key === 'ok' ? '✓' : key}
            </button>
          ))}
        </div>
        {screen.errorKey && <p className="kiosk-error" role="alert">{kt(screen.errorKey)}</p>}
      </section>
    </main>
  )
}
