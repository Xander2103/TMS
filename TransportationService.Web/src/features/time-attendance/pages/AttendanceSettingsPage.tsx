import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { formatDateTime } from '../../../utils/dates'
import { getLocationOptions } from '../../locations/api/locationsApi'
import type { LocationOption } from '../../locations/types'
import {
  createKioskDevice, getAttendanceSettings, listKioskDevices, rotateKioskSecret,
  updateAttendanceSettings, updateKioskDevice,
} from '../api/timeAttendanceApi'
import type { AttendanceSettings, KioskDevice } from '../types'
import './time-attendance.css'

/**
 * Instellingen → Urenregistratie: tenant-policies + prikklokbeheer. De provisioning-key
 * van een prikklok verschijnt exact één keer (na aanmaken of rotatie) en is daarna
 * onherleidbaar — dat is bewust.
 */
export function AttendanceSettingsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManageSettings = hasPermission('attendance.manage_settings')
  const canManageKiosks = hasPermission('attendance.manage_kiosks')

  const [settings, setSettings] = useState<AttendanceSettings | null>(null)
  const [devices, setDevices] = useState<KioskDevice[] | null>(null)
  const [locations, setLocations] = useState<LocationOption[]>([])
  const [reloadToken, setReloadToken] = useState(0)
  const [savingSettings, setSavingSettings] = useState(false)

  const [createOpen, setCreateOpen] = useState(false)
  const [newDevice, setNewDevice] = useState({ name: '', locationId: '' })
  const [provisionedKey, setProvisionedKey] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    if (canManageSettings) {
      getAttendanceSettings()
        .then((data) => {
          if (mounted) setSettings(data)
        })
        .catch((err) => showError(describeApiError(err, 'De instellingen konden niet worden geladen.').message))
    }

    if (canManageKiosks) {
      listKioskDevices()
        .then((data) => {
          if (mounted) setDevices(data)
        })
        .catch((err) => showError(describeApiError(err, 'De prikklokken konden niet worden geladen.').message))
      getLocationOptions()
        .then((data) => {
          if (mounted) setLocations(data)
        })
        .catch(() => {
          /* locatiekeuze is optioneel */
        })
    }

    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadToken, canManageSettings, canManageKiosks])

  const saveSettings = async () => {
    if (!settings) return
    setSavingSettings(true)
    try {
      const { kioskConfigured: _ignored, ...payload } = settings
      const updated = await updateAttendanceSettings(payload)
      setSettings(updated)
      showSuccess('Instellingen opgeslagen.')
    } catch (err) {
      showError(describeApiError(err, 'Opslaan mislukt.').message)
    } finally {
      setSavingSettings(false)
    }
  }

  const createDevice = async () => {
    if (!newDevice.name.trim()) {
      showError('Een naam is verplicht.')
      return
    }

    try {
      const result = await createKioskDevice({
        name: newDevice.name.trim(),
        locationId: newDevice.locationId || null,
        isActive: true,
      })
      setCreateOpen(false)
      setNewDevice({ name: '', locationId: '' })
      setProvisionedKey(result.deviceKey)
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De prikklok kon niet worden aangemaakt.').message)
    }
  }

  const toggleDevice = async (device: KioskDevice) => {
    try {
      await updateKioskDevice(device.id, {
        name: device.name,
        locationId: device.locationId,
        isActive: !device.isActive,
      })
      showSuccess(device.isActive ? 'Prikklok uitgeschakeld.' : 'Prikklok ingeschakeld.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De wijziging is niet gelukt.').message)
    }
  }

  const rotateDevice = async (device: KioskDevice) => {
    try {
      const result = await rotateKioskSecret(device.id)
      setProvisionedKey(result.deviceKey)
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De sleutel kon niet vernieuwd worden.').message)
    }
  }

  const deviceColumns: Column<KioskDevice>[] = [
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'location', header: 'Locatie', width: '180px', render: (row) => row.locationName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '110px',
      render: (row) => (row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="danger">Uit</Badge>),
    },
    {
      key: 'lastSeen',
      header: 'Laatste activiteit',
      width: '170px',
      render: (row) => (row.lastSeenAt ? formatDateTime(row.lastSeenAt) : '—'),
    },
    {
      key: 'lastPunch',
      header: 'Laatste punch',
      width: '170px',
      render: (row) => (row.lastPunchAt ? formatDateTime(row.lastPunchAt) : '—'),
    },
    {
      key: 'actions',
      header: '',
      width: '260px',
      render: (row) => (
        <span className="ta-device-actions">
          <Button variant="ghost" onClick={() => rotateDevice(row)}>
            Sleutel vernieuwen
          </Button>
          <Button variant="ghost" onClick={() => toggleDevice(row)}>
            {row.isActive ? 'Uitschakelen' : 'Inschakelen'}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Urenregistratie' }]} />
      <PageHeader title="Urenregistratie" subtitle="Punchregels, waarschuwingen en prikklokken." />

      {canManageSettings && settings && (
        <section aria-label="Instellingen" className="ta-settings">
          {!settings.kioskConfigured && (
            <p className="ta-settings-warning">
              De prikklok is nog niet geconfigureerd op de server (Attendance:PinPepper ontbreekt).
              PIN-codes en kiosk-punches zijn tot dan uitgeschakeld.
            </p>
          )}
          <div className="ta-settings-grid">
            <FormField label="Zelf in-/uitpunten via dashboard" htmlFor="set-self">
              <input
                id="set-self"
                type="checkbox"
                checked={settings.selfPunchEnabled}
                onChange={(event) => setSettings({ ...settings, selfPunchEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label="Prikklok (kiosk) actief" htmlFor="set-kiosk">
              <input
                id="set-kiosk"
                type="checkbox"
                checked={settings.kioskEnabled}
                onChange={(event) => setSettings({ ...settings, kioskEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label="PIN-lengte (4–8)" htmlFor="set-pin">
              <input
                id="set-pin"
                type="number"
                min={4}
                max={8}
                value={settings.pinLength}
                onChange={(event) => setSettings({ ...settings, pinLength: Number(event.target.value) })}
              />
            </FormField>
            <FormField
              label="Waarschuwing vergeten uitpunten (uren)"
              htmlFor="set-forgotten"
              hint="Actieve sessies langer dan deze grens krijgen een melding."
            >
              <input
                id="set-forgotten"
                type="number"
                min={8}
                max={48}
                value={settings.forgottenClockOutAfterHours}
                onChange={(event) => setSettings({ ...settings, forgottenClockOutAfterHours: Number(event.target.value) })}
              />
            </FormField>
            <FormField
              label="Automatisch afsluiten"
              htmlFor="set-autoclose"
              hint="Standaard uit: het systeem waarschuwt alleen. Automatisch afsluiten laat altijd een audittrail achter."
            >
              <input
                id="set-autoclose"
                type="checkbox"
                checked={settings.autoCloseEnabled}
                onChange={(event) => setSettings({ ...settings, autoCloseEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label="Automatisch afsluiten na (uren)" htmlFor="set-autoclose-hours">
              <input
                id="set-autoclose-hours"
                type="number"
                min={8}
                max={72}
                value={settings.autoCloseAfterHours}
                disabled={!settings.autoCloseEnabled}
                onChange={(event) => setSettings({ ...settings, autoCloseAfterHours: Number(event.target.value) })}
              />
            </FormField>
            <FormField
              label="Marge gepland-niet-ingepunt (minuten)"
              htmlFor="set-grace"
              hint="Pas na deze marge na de geplande start telt iemand als 'gepland maar niet ingepunt'."
            >
              <input
                id="set-grace"
                type="number"
                min={0}
                max={480}
                value={settings.plannedNotClockedInGraceMinutes}
                onChange={(event) =>
                  setSettings({ ...settings, plannedNotClockedInGraceMinutes: Number(event.target.value) })}
              />
            </FormField>
          </div>
          <Button onClick={saveSettings} disabled={savingSettings}>
            Instellingen opslaan
          </Button>
        </section>
      )}

      {canManageKiosks && (
        <section aria-label="Prikklokken" className="ta-devices">
          <div className="ta-devices-head">
            <h2>Prikklokken</h2>
            <Button onClick={() => setCreateOpen(true)}>+ Prikklok toevoegen</Button>
          </div>
          <DataTable
            columns={deviceColumns}
            rows={devices ?? []}
            rowKey={(row) => row.id}
            isLoading={devices === null}
            error={null}
            emptyMessage="Nog geen prikklokken geregistreerd."
          />
          <p className="ta-devices-hint">
            Open op de tablet <code>/kiosk</code>, plak daar éénmalig de provisioning-sleutel en zet de
            browser in kioskmodus (volledig scherm). Zie de handleiding voor details.
          </p>
        </section>
      )}

      {!canManageSettings && !canManageKiosks && (
        <p className="placeholder-text">Je hebt geen rechten om urenregistratie-instellingen te beheren.</p>
      )}

      {createOpen && (
        <Modal
          title="Prikklok toevoegen"
          onClose={() => setCreateOpen(false)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setCreateOpen(false)}>
                Annuleren
              </Button>
              <Button onClick={createDevice}>Aanmaken</Button>
            </>
          }
        >
          <FormField label="Naam" htmlFor="dev-name" required>
            <input
              id="dev-name"
              type="text"
              value={newDevice.name}
              onChange={(event) => setNewDevice({ ...newDevice, name: event.target.value })}
              placeholder="bv. Prikklok magazijn"
            />
          </FormField>
          <FormField label="Locatie (optioneel)" htmlFor="dev-location">
            <select
              id="dev-location"
              value={newDevice.locationId}
              onChange={(event) => setNewDevice({ ...newDevice, locationId: event.target.value })}
            >
              <option value="">Geen locatie</option>
              {locations.map((location) => (
                <option key={location.id} value={location.id}>
                  {location.name}
                </option>
              ))}
            </select>
          </FormField>
        </Modal>
      )}

      {provisionedKey && (
        <Modal
          title="Provisioning-sleutel"
          onClose={() => setProvisionedKey(null)}
          footer={<Button onClick={() => setProvisionedKey(null)}>Ik heb de sleutel opgeslagen</Button>}
        >
          <p>
            Plak deze sleutel éénmalig in het instelscherm van de prikklok (<code>/kiosk</code>).
            Ze wordt <strong>niet</strong> opnieuw getoond; verlies je ze, vernieuw dan de sleutel.
          </p>
          <p className="ta-provision-key">{provisionedKey}</p>
        </Modal>
      )}
    </div>
  )
}
