import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
        if (mounted) setLoadError('employees.hrReminders.loadFailed')
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
      showSuccess(t('employees.hrReminders.saved'))
    } catch (err) {
      const { message, fieldErrors: errors } = describeApiError(err, t('employees.hrReminders.saveFailed'))
      setFieldErrors(errors)
      showError(message)
    } finally {
      setSaving(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{t(loadError)}</p>
  if (!form) return <p className="placeholder-text">{t('employees.hrReminders.loading')}</p>

  const disabled = !canManage || saving
  const dossierDisabled = disabled || !form.dossierRemindersEnabled

  return (
    <div>
      <UnsavedChangesGuard when={dirty && !saving} />
      <Breadcrumbs items={[{ label: t('employees.hrReminders.breadcrumbSettings'), to: '/settings' }, { label: t('employees.hrReminders.title') }]} />
      <PageHeader
        title={t('employees.hrReminders.title')}
        subtitle={canManage ? t('employees.hrReminders.subtitleManage') : t('employees.hrReminders.subtitleReadOnly')}
      />

      <div className="hr-reminder-settings-sections">
        <section className="hr-reminder-settings-card">
          <h2>{t('employees.hrReminders.birthdaysHeading')}</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.birthdayEnabled}
              disabled={disabled}
              onChange={(e) => setField('birthdayEnabled', e.target.checked)}
            />
            {t('employees.hrReminders.birthdayEnabled')}
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label={t('employees.hrReminders.daysBefore')} htmlFor="hr-birthday-days" hint={t('employees.hrReminders.daysBeforeHint')}>
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
            <FormField label={t('employees.hrReminders.birthdayRoles')} htmlFor="hr-birthday-roles">
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
            {t('employees.hrReminders.birthdayEmail')}
          </label>
        </section>

        <section className="hr-reminder-settings-card">
          <h2>{t('employees.hrReminders.seniorityHeading')}</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.seniorityEnabled}
              disabled={disabled}
              onChange={(e) => setField('seniorityEnabled', e.target.checked)}
            />
            {t('employees.hrReminders.seniorityEnabled')}
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label={t('employees.hrReminders.milestoneYears')} htmlFor="hr-seniority-years">
              <input
                id="hr-seniority-years"
                type="text"
                value={form.seniorityMilestoneYears}
                disabled={disabled}
                onChange={(e) => setField('seniorityMilestoneYears', e.target.value)}
              />
            </FormField>
            <FormField label={t('employees.hrReminders.seniorityWarningDays')} htmlFor="hr-seniority-warning">
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
            {t('employees.hrReminders.seniorityEmail')}
          </label>
        </section>

        <section className="hr-reminder-settings-card">
          <h2>{t('employees.hrReminders.employmentEndHeading')}</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.employmentEndEnabled}
              disabled={disabled}
              onChange={(e) => setField('employmentEndEnabled', e.target.checked)}
            />
            {t('employees.hrReminders.employmentEndEnabled')}
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField label={t('employees.hrReminders.daysBefore')} htmlFor="hr-employment-end-days">
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
          <h2>{t('employees.hrReminders.dossierHeading')}</h2>
          <label className="hr-reminder-settings-checkbox">
            <input
              type="checkbox"
              checked={form.dossierRemindersEnabled}
              disabled={disabled}
              onChange={(e) => setField('dossierRemindersEnabled', e.target.checked)}
            />
            {t('employees.hrReminders.dossierEnabled')}
          </label>
          <div className="hr-reminder-settings-grid">
            <FormField
              label={t('employees.hrReminders.dossierReminderDays')}
              htmlFor="hr-dossier-reminder-days"
              hint={t('employees.hrReminders.dossierReminderDaysHint')}
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
              label={t('employees.hrReminders.dossierEscalationDays')}
              htmlFor="hr-dossier-escalation-days"
              hint={t('employees.hrReminders.dossierEscalationDaysHint')}
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
            {t('employees.hrReminders.reset')}
          </Button>
          <Button onClick={() => void handleSave()} disabled={!dirty || saving}>
            {saving ? t('employees.hrReminders.saving') : t('employees.hrReminders.save')}
          </Button>
        </FormActions>
      )}
    </div>
  )
}
