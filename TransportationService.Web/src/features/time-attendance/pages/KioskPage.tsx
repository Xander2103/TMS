import { useCallback, useEffect, useRef, useState } from 'react'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
import {
  KioskUnreachableError, clearDeviceKey, getStoredDeviceKey, kioskIdentify, kioskPing, kioskPunch,
  storeDeviceKey,
} from '../api/kioskApi'
import type { AttendanceStatus, KioskPunchAction } from '../types'
import './kiosk.css'

const RESET_AFTER_CONFIRM_MS = 4_000
const RESET_AFTER_IDLE_MS = 25_000
const MAX_PIN_LENGTH = 8

type Screen =
  | { kind: 'setup'; error: string | null }
  | { kind: 'pin'; error: string | null }
  | { kind: 'welcome'; firstName: string; status: AttendanceStatus; token: string }
  | { kind: 'confirm'; message: string; detail: string | null }
  | { kind: 'blocked'; message: string }

/**
 * Prikklok (spec §10–§15): fullscreen, geen ERP-shell, geen navigatie. Het device
 * authenticeert met de éénmalig geplakte provisioning-sleutel (localStorage); een
 * medewerker identificeert zich met een code en krijgt uitsluitend de acties die nu
 * geldig zijn. Na elke actie of inactiviteit reset het scherm zodat er nooit
 * persoonsgegevens blijven staan. Zonder serverbevestiging wordt een punch NOOIT als
 * gelukt getoond.
 */
export function KioskPage() {
  const [deviceKey, setDeviceKey] = useState<string | null>(getStoredDeviceKey)
  const [screen, setScreen] = useState<Screen>({ kind: deviceKey ? 'pin' : 'setup', error: null })
  const [pin, setPin] = useState('')
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(() => new Date())
  const [deviceName, setDeviceName] = useState<string | null>(null)
  const [setupKey, setSetupKey] = useState('')
  const idleTimer = useRef<number | null>(null)

  // Groot live uurwerk.
  useEffect(() => {
    const handle = setInterval(() => setNow(new Date()), 1_000)
    return () => clearInterval(handle)
  }, [])

  const resetToPin = useCallback((error: string | null = null) => {
    setPin('')
    setScreen({ kind: 'pin', error })
  }, [])

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

  // Devicecheck bij opstart.
  useEffect(() => {
    if (!deviceKey) return
    let mounted = true
    kioskPing(deviceKey)
      .then((result) => {
        if (!mounted) return
        if (result.outcome === 'Success') {
          setDeviceName(result.deviceName)
        } else if (result.outcome === 'InvalidDevice') {
          clearDeviceKey()
          setDeviceKey(null)
          setScreen({ kind: 'setup', error: 'Deze prikklok is niet (meer) geregistreerd. Voer een nieuwe sleutel in.' })
        } else {
          setScreen({ kind: 'blocked', message: result.error ?? 'De prikklok is niet beschikbaar.' })
        }
      })
      .catch(() => {
        if (mounted) setScreen({ kind: 'pin', error: 'Geen verbinding met de server. Punches kunnen momenteel niet veilig geregistreerd worden.' })
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
        setScreen({
          kind: 'welcome',
          firstName: result.firstName ?? '',
          status: result.status,
          token: result.interactionToken,
        })
      } else if (result.outcome === 'InvalidDevice') {
        clearDeviceKey()
        setDeviceKey(null)
        setScreen({ kind: 'setup', error: 'Deze prikklok is niet (meer) geregistreerd.' })
      } else if (result.outcome === 'KioskDisabled' || result.outcome === 'NotConfigured') {
        setScreen({ kind: 'blocked', message: result.error ?? 'De prikklok is uitgeschakeld.' })
      } else {
        resetToPin(result.error ?? 'Code ongeldig.')
      }
    } catch (err) {
      resetToPin(err instanceof KioskUnreachableError
        ? 'Geen verbinding met de server. Inpunten kan momenteel niet veilig geregistreerd worden. Probeer opnieuw.'
        : 'Er ging iets mis. Probeer opnieuw.')
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
          const confirmation: Record<KioskPunchAction, { message: string; detail: string | null }> = {
            ClockIn: { message: `Ingepunt om ${time}`, detail: 'Fijne werkdag!' },
            ClockOut: { message: `Uitgepunt om ${time}`, detail: 'Tot de volgende keer!' },
            StartBreak: { message: `Pauze gestart om ${time}`, detail: 'Smakelijk!' },
            EndBreak: { message: `Pauze beëindigd om ${time}`, detail: 'Werk ze!' },
          }
          setScreen({ kind: 'confirm', ...confirmation[action] })
        } else if (result.outcome === 'TokenExpired') {
          resetToPin('De sessie is verlopen. Voer uw code opnieuw in.')
        } else {
          resetToPin(result.error ?? 'De actie kon niet worden geregistreerd.')
        }
      } catch {
        resetToPin('Geen verbinding met de server. De actie is NIET geregistreerd. Probeer opnieuw.')
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
        setSetupKey('')
        setScreen({ kind: 'pin', error: null })
      } else if (result.outcome === 'InvalidDevice') {
        setScreen({ kind: 'setup', error: 'Sleutel niet herkend. Controleer de provisioning-sleutel.' })
      } else {
        setScreen({ kind: 'setup', error: result.error ?? 'De prikklok is niet beschikbaar.' })
      }
    } catch {
      setScreen({ kind: 'setup', error: 'Geen verbinding met de server. Probeer opnieuw.' })
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
        {now.toLocaleDateString('nl-BE', { weekday: 'long', day: 'numeric', month: 'long' })}
      </span>
      {deviceName && <span className="kiosk-device">{deviceName}</span>}
    </div>
  )

  if (screen.kind === 'setup') {
    return (
      <main className="kiosk">
        {clock}
        <section className="kiosk-panel" aria-label="Prikklok instellen">
          <h1 className="kiosk-heading">Prikklok instellen</h1>
          <p className="kiosk-hint">Plak de provisioning-sleutel uit Instellingen → Urenregistratie.</p>
          <input
            type="password"
            className="kiosk-setup-input"
            value={setupKey}
            onChange={(event) => setSetupKey(event.target.value)}
            placeholder="Provisioning-sleutel"
            aria-label="Provisioning-sleutel"
          />
          <button type="button" className="kiosk-action kiosk-action-primary" onClick={saveSetupKey} disabled={busy}>
            Registreren
          </button>
          {screen.error && <p className="kiosk-error" role="alert">{screen.error}</p>}
        </section>
      </main>
    )
  }

  if (screen.kind === 'blocked') {
    return (
      <main className="kiosk">
        {clock}
        <section className="kiosk-panel">
          <p className="kiosk-error" role="alert">{screen.message}</p>
        </section>
      </main>
    )
  }

  if (screen.kind === 'welcome') {
    const { status } = screen
    return (
      <main className="kiosk">
        {clock}
        <section className="kiosk-panel" aria-label="Acties">
          <h1 className="kiosk-heading">Welkom, {screen.firstName}</h1>
          {status.status === 'Working' && status.clockInAt && (
            <p className="kiosk-hint">Aan het werk sinds {formatTime(status.clockInAt)} · vandaag {formatDurationMinutes(status.workedMinutesToday)}</p>
          )}
          {status.status === 'OnBreak' && status.breakStartedAt && (
            <p className="kiosk-hint">Pauze sinds {formatTime(status.breakStartedAt)}</p>
          )}
          {status.status === 'NotClockedIn' && <p className="kiosk-hint">U bent nog niet ingepunt.</p>}
          {status.status === 'ClockedOut' && <p className="kiosk-hint">U bent al uitgepunt vandaag.</p>}
          <div className="kiosk-actions">
            {status.canClockIn && (
              <button type="button" className="kiosk-action kiosk-action-primary" disabled={busy} onClick={() => punch('ClockIn')}>
                Inpunten
              </button>
            )}
            {status.canStartBreak && (
              <button type="button" className="kiosk-action" disabled={busy} onClick={() => punch('StartBreak')}>
                Pauze starten
              </button>
            )}
            {status.canEndBreak && (
              <button type="button" className="kiosk-action kiosk-action-primary" disabled={busy} onClick={() => punch('EndBreak')}>
                Pauze beëindigen
              </button>
            )}
            {status.canClockOut && (
              <button type="button" className="kiosk-action kiosk-action-out" disabled={busy} onClick={() => punch('ClockOut')}>
                Uitpunten
              </button>
            )}
          </div>
          <button type="button" className="kiosk-back" onClick={() => resetToPin()}>
            Annuleren
          </button>
        </section>
      </main>
    )
  }

  if (screen.kind === 'confirm') {
    return (
      <main className="kiosk">
        {clock}
        <section className="kiosk-panel kiosk-confirm" role="status">
          <p className="kiosk-heading">{screen.message}</p>
          {screen.detail && <p className="kiosk-hint">{screen.detail}</p>}
        </section>
      </main>
    )
  }

  // PIN-scherm
  const keypad: (string | 'back' | 'ok')[] = ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'back', '0', 'ok']
  return (
    <main className="kiosk">
      {clock}
      <section className="kiosk-panel" aria-label="Code invoeren">
        <h1 className="kiosk-heading">Voer uw code in</h1>
        <div className="kiosk-dots" aria-label={`${pin.length} cijfers ingevoerd`} aria-live="polite">
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
              aria-label={key === 'back' ? 'Wissen' : key === 'ok' ? 'Bevestigen' : key}
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
        {screen.error && <p className="kiosk-error" role="alert">{screen.error}</p>}
      </section>
    </main>
  )
}
