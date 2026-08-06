import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { getHrReminderSettings, updateHrReminderSettings, type HrReminderSettings } from '../api/hrReminderSettingsApi'
import './hr-reminder-settings-page.css'

/**
 * "HR-herinneringen" (/settings/hr-reminders): verjaardagen, dienstjubilea, einde
 * dienstverband en de opvolging van onvolledige personeelsdossiers (Task 3/4 van de
 * HR-maturity-wave). Eén PUT met het volledige `HrReminderSettingsDto` per opslag,
 * zelfde patroon als `SettingsTab` (Peppol): describeApiError → fieldErrors + toast.
 */
export function HrReminderSettingsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('hr_settings.manage')

  const [loaded, setLoaded] = useState<HrReminderSettings | null>(null)
  const [form, setForm] = useState<HrReminderSettings | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let mounted = true
    getHrReminderSettings()
      .then((data) => {
        if (!mounted) return
        setLoaded(data)
        setForm(data)
      })
      .catch(() => {
        if (mounted) setLoadError('De HR-herinneringsinstellingen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  const dirty = form !== null && loaded !== null && JSON.stringify(form) !== JSON.stringify(loaded)

  function setField<K extends keyof HrReminderSettings>(key: K, value: HrReminderSettings[K]) {
    setForm((current) => (current ? { ...current, [key]: value } : current))
  }

  async function handleSave() {
    if (!form) return
    setSaving(true)
    setFieldErrors({})
    try {
      const updated = await updateHrReminderSettings(form)
      setLoaded(updated)
      setForm(updated)
      showSuccess('HR-herinneringsinstellingen opgeslagen.')
    } catch (err) {
      const { message, fieldErrors: errors } = describeApiError(err, 'De instellingen konden niet worden opgeslagen.')
      setFieldErrors(errors)
      showError(message)
    } finally {
      setSaving(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!form) return <p className="placeholder-text">Instellingen laden…</p>

  const disabled = !canManage || saving
  const dossierDisabled = disabled || !form.dossierRemindersEnabled

  return (
    <div>
      <UnsavedChangesGuard when={dirty && !saving} />
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'HR-herinneringen' }]} />
      <PageHeader
        title="HR-herinneringen"
        subtitle={
          canManage
            ? 'Verjaardagen, dienstjubilea, einde dienstverband en opvolging van onvolledige dossiers.'
            : 'Je hebt alleen leesrechten voor deze instellingen.'
        }
      />

      <div className="hr-reminder-settings-sections">
        <section className="hr-reminder-settings-card">
          <h2>Verjaardagen</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.birthdayEnabled}
              disabled={disabled}
              onChange={(e) => setField('birthdayEnabled', e.target.checked)}
            />
            Verjaardagsherinnering actief
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label="Dagen op voorhand" htmlFor="hr-birthday-days" hint="0 = op de dag zelf.">
              <input
                id="hr-birthday-days"
                type="number"
                min={0}
                max={60}
                value={form.birthdayDaysBefore}
                disabled={disabled}
                onChange={(e) => setField('birthdayDaysBefore', Number(e.target.value) || 0)}
              />
            </FormField>
            <FormField label="Rolcodes ontvangers (CSV)" htmlFor="hr-birthday-roles">
              <input
                id="hr-birthday-roles"
                type="text"
                value={form.birthdayRecipientRoleCodes}
                disabled={disabled}
                onChange={(e) => setField('birthdayRecipientRoleCodes', e.target.value)}
              />
            </FormField>
          </div>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.birthdayEmailEnabled}
              disabled={disabled}
              onChange={(e) => setField('birthdayEmailEnabled', e.target.checked)}
            />
            Automatische e-mail naar de medewerker
          </label>
        </section>

        <section className="hr-reminder-settings-card">
          <h2>Dienstjubilea</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.seniorityEnabled}
              disabled={disabled}
              onChange={(e) => setField('seniorityEnabled', e.target.checked)}
            />
            Jubileumherinnering actief
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label="Mijlpaaljaren (CSV)" htmlFor="hr-seniority-years">
              <input
                id="hr-seniority-years"
                type="text"
                value={form.seniorityMilestoneYears}
                disabled={disabled}
                onChange={(e) => setField('seniorityMilestoneYears', e.target.value)}
              />
            </FormField>
            <FormField label="Waarschuwing (dagen op voorhand)" htmlFor="hr-seniority-warning">
              <input
                id="hr-seniority-warning"
                type="number"
                min={0}
                max={365}
                value={form.seniorityWarningDays}
                disabled={disabled}
                onChange={(e) => setField('seniorityWarningDays', Number(e.target.value) || 0)}
              />
            </FormField>
          </div>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.seniorityEmployeeEmailEnabled}
              disabled={disabled}
              onChange={(e) => setField('seniorityEmployeeEmailEnabled', e.target.checked)}
            />
            Automatische e-mail naar de medewerker op de mijlpaaldatum
          </label>
        </section>

        <section className="hr-reminder-settings-card">
          <h2>Einde dienstverband</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.employmentEndEnabled}
              disabled={disabled}
              onChange={(e) => setField('employmentEndEnabled', e.target.checked)}
            />
            Herinnering einde dienstverband actief
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label="Dagen op voorhand" htmlFor="hr-employment-end-days">
              <input
                id="hr-employment-end-days"
                type="number"
                min={0}
                max={365}
                value={form.employmentEndDaysBefore}
                disabled={disabled}
                onChange={(e) => setField('employmentEndDaysBefore', Number(e.target.value) || 0)}
              />
            </FormField>
          </div>
        </section>

        <section className="hr-reminder-settings-card">
          <h2>Opvolging onvolledige dossiers</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.dossierRemindersEnabled}
              disabled={disabled}
              onChange={(e) => setField('dossierRemindersEnabled', e.target.checked)}
            />
            Opvolging onvolledige dossiers actief
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField
              label="Eerste melding na (dagen)"
              htmlFor="hr-dossier-reminder-days"
              hint="Hr-rol wordt verwittigd zodra een dossier dit aantal dagen onvolledig is."
              error={getFieldError(fieldErrors, 'dossierReminderDays')}
            >
              <input
                id="hr-dossier-reminder-days"
                type="number"
                min={1}
                max={365}
                value={form.dossierReminderDays}
                disabled={dossierDisabled}
                onChange={(e) => setField('dossierReminderDays', Number(e.target.value) || 0)}
              />
            </FormField>
            <FormField
              label="Escalatie na (dagen)"
              htmlFor="hr-dossier-escalation-days"
              hint="Hr + management worden verwittigd (kritiek) na dit aantal dagen."
              error={getFieldError(fieldErrors, 'dossierEscalationDays')}
            >
              <input
                id="hr-dossier-escalation-days"
                type="number"
                min={1}
                max={365}
                value={form.dossierEscalationDays}
                disabled={dossierDisabled}
                onChange={(e) => setField('dossierEscalationDays', Number(e.target.value) || 0)}
              />
            </FormField>
          </div>
        </section>
      </div>

      {canManage && (
        <FormActions dirty={dirty}>
          <Button variant="secondary" onClick={() => setForm(loaded)} disabled={!dirty || saving}>
            Herstellen
          </Button>
          <Button onClick={() => void handleSave()} disabled={!dirty || saving}>
            {saving ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </FormActions>
      )}
    </div>
  )
}
