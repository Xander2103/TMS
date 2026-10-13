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
import { useLocale } from '../../../i18n/localeContext'
import { LANGUAGE_NAMES } from '../../../i18n/languageNames'
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
  const { t } = useLocale()
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
  const [newDevice, setNewDevice] = useState<{ name: string; locationId: string; defaultLanguage: 'nl' | 'fr' | 'en' }>({
    name: '', locationId: '', defaultLanguage: 'nl',
  })
  const [provisionedKey, setProvisionedKey] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    if (canManageSettings) {
      getAttendanceSettings()
        .then((data) => {
          if (mounted) setSettings(data)
        })
        .catch((err) => showError(describeApiError(err, t('attendance.settings.loadFailed')).message))
    }

    if (canManageKiosks) {
      listKioskDevices()
        .then((data) => {
          if (mounted) setDevices(data)
        })
        .catch((err) => showError(describeApiError(err, t('attendance.devices.loadFailed')).message))
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
      // kioskConfigured is server-derived and stripped from the update payload.
      const { kioskConfigured, ...payload } = settings
      void kioskConfigured
      const updated = await updateAttendanceSettings(payload)
      setSettings(updated)
      showSuccess(t('attendance.settings.saved'))
    } catch (err) {
      showError(describeApiError(err, t('attendance.settings.saveFailed')).message)
    } finally {
      setSavingSettings(false)
    }
  }

  const createDevice = async () => {
    if (!newDevice.name.trim()) {
      showError(t('attendance.devices.nameRequired'))
      return
    }

    try {
      const result = await createKioskDevice({
        name: newDevice.name.trim(),
        locationId: newDevice.locationId || null,
        isActive: true,
        defaultLanguage: newDevice.defaultLanguage,
      })
      setCreateOpen(false)
      setNewDevice({ name: '', locationId: '', defaultLanguage: 'nl' })
      setProvisionedKey(result.deviceKey)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('attendance.devices.createFailed')).message)
    }
  }

  const toggleDevice = async (device: KioskDevice) => {
    try {
      await updateKioskDevice(device.id, {
        name: device.name,
        locationId: device.locationId,
        isActive: !device.isActive,
        defaultLanguage: device.defaultLanguage,
      })
      showSuccess(device.isActive ? t('attendance.devices.toggleDisabled') : t('attendance.devices.toggleEnabled'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('attendance.devices.toggleFailed')).message)
    }
  }

  const rotateDevice = async (device: KioskDevice) => {
    try {
      const result = await rotateKioskSecret(device.id)
      setProvisionedKey(result.deviceKey)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('attendance.devices.rotateFailed')).message)
    }
  }

  const deviceColumns: Column<KioskDevice>[] = [
    { key: 'name', header: t('attendance.devices.columnName'), render: (row) => row.name },
    { key: 'location', header: t('attendance.devices.columnLocation'), width: '180px', render: (row) => row.locationName ?? '—' },
    {
      key: 'language',
      header: t('attendance.devices.columnLanguage'),
      width: '120px',
      render: (row) => LANGUAGE_NAMES[row.defaultLanguage],
    },
    {
      key: 'status',
      header: t('attendance.devices.columnStatus'),
      width: '110px',
      render: (row) => (row.isActive
        ? <Badge tone="success">{t('attendance.devices.active')}</Badge>
        : <Badge tone="danger">{t('attendance.devices.inactive')}</Badge>),
    },
    {
      key: 'lastSeen',
      header: t('attendance.devices.columnLastSeen'),
      width: '170px',
      render: (row) => (row.lastSeenAt ? formatDateTime(row.lastSeenAt) : '—'),
    },
    {
      key: 'lastPunch',
      header: t('attendance.devices.columnLastPunch'),
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
            {t('attendance.devices.rotate')}
          </Button>
          <Button variant="ghost" onClick={() => toggleDevice(row)}>
            {row.isActive ? t('attendance.devices.disable') : t('attendance.devices.enable')}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('attendance.settings.title') }]} />
      <PageHeader title={t('attendance.settings.title')} subtitle={t('attendance.settings.subtitle')} />

      {canManageSettings && settings && (
        <section aria-label={t('attendance.settings.sectionLabel')} className="ta-settings">
          {!settings.kioskConfigured && (
            <p className="ta-settings-warning">
              {t('attendance.settings.pepperMissing')}
            </p>
          )}
          <div className="ta-settings-grid">
            <FormField label={t('attendance.settings.selfPunch')} htmlFor="set-self">
              <input
                id="set-self"
                type="checkbox"
                checked={settings.selfPunchEnabled}
                onChange={(event) => setSettings({ ...settings, selfPunchEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label={t('attendance.settings.kioskActive')} htmlFor="set-kiosk">
              <input
                id="set-kiosk"
                type="checkbox"
                checked={settings.kioskEnabled}
                onChange={(event) => setSettings({ ...settings, kioskEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label={t('attendance.settings.pinLength')} htmlFor="set-pin">
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
              label={t('attendance.settings.forgottenAfter')}
              htmlFor="set-forgotten"
              hint={t('attendance.settings.forgottenAfterHint')}
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
              label={t('attendance.settings.autoClose')}
              htmlFor="set-autoclose"
              hint={t('attendance.settings.autoCloseHint')}
            >
              <input
                id="set-autoclose"
                type="checkbox"
                checked={settings.autoCloseEnabled}
                onChange={(event) => setSettings({ ...settings, autoCloseEnabled: event.target.checked })}
              />
            </FormField>
            <FormField label={t('attendance.settings.autoCloseAfter')} htmlFor="set-autoclose-hours">
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
              label={t('attendance.settings.grace')}
              htmlFor="set-grace"
              hint={t('attendance.settings.graceHint')}
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
            {t('attendance.settings.save')}
          </Button>
        </section>
      )}

      {canManageKiosks && (
        <section aria-label={t('attendance.devices.title')} className="ta-devices">
          <div className="ta-devices-head">
            <h2>{t('attendance.devices.title')}</h2>
            <Button onClick={() => setCreateOpen(true)}>{t('attendance.devices.add')}</Button>
          </div>
          <DataTable
            columns={deviceColumns}
            rows={devices ?? []}
            rowKey={(row) => row.id}
            isLoading={devices === null}
            error={null}
            emptyMessage={t('attendance.devices.empty')}
          />
          <p className="ta-devices-hint">
            {t('attendance.devices.hint')}
          </p>
        </section>
      )}

      {!canManageSettings && !canManageKiosks && (
        <p className="placeholder-text">{t('attendance.settings.noPermission')}</p>
      )}

      {createOpen && (
        <Modal
          title={t('attendance.devices.createTitle')}
          onClose={() => setCreateOpen(false)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setCreateOpen(false)}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={createDevice}>{t('attendance.devices.create')}</Button>
            </>
          }
        >
          <FormField label={t('attendance.devices.nameField')} htmlFor="dev-name" required>
            <input
              id="dev-name"
              type="text"
              value={newDevice.name}
              onChange={(event) => setNewDevice({ ...newDevice, name: event.target.value })}
              placeholder={t('attendance.devices.namePlaceholder')}
            />
          </FormField>
          <FormField label={t('attendance.devices.locationField')} htmlFor="dev-location">
            <select
              id="dev-location"
              value={newDevice.locationId}
              onChange={(event) => setNewDevice({ ...newDevice, locationId: event.target.value })}
            >
              <option value="">{t('attendance.devices.noLocation')}</option>
              {locations.map((location) => (
                <option key={location.id} value={location.id}>
                  {location.name}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('attendance.devices.languageField')} htmlFor="dev-language">
            <select
              id="dev-language"
              value={newDevice.defaultLanguage}
              onChange={(event) =>
                setNewDevice({ ...newDevice, defaultLanguage: event.target.value as 'nl' | 'fr' | 'en' })}
            >
              {(['nl', 'fr', 'en'] as const).map((language) => (
                <option key={language} value={language}>
                  {LANGUAGE_NAMES[language]}
                </option>
              ))}
            </select>
          </FormField>
        </Modal>
      )}

      {provisionedKey && (
        <Modal
          title={t('attendance.devices.provisionTitle')}
          onClose={() => setProvisionedKey(null)}
          footer={<Button onClick={() => setProvisionedKey(null)}>{t('attendance.devices.provisionConfirm')}</Button>}
        >
          <p>
            {t('attendance.devices.provisionExplanation')}
          </p>
          <p className="ta-provision-key">{provisionedKey}</p>
        </Modal>
      )}
    </div>
  )
}
