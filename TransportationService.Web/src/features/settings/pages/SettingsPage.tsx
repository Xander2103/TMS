import { useEffect, useState, type ReactNode } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { LANGUAGE_NAMES } from '../../../i18n/languageNames'
import { LOCALES } from '../../../i18n/translations'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { getCompanySettings, updateCompanySettings } from '../api/settingsApi'
import { DATE_FORMAT_OPTIONS, formatExample, setDateFormatPreference } from '../../../utils/dates'
import type { CompanySettings } from '../types'
import './settings.css'

type NumericField =
  | 'paymentTermDays'
  | 'defaultVatRatePercent'
  | 'defaultLoadingMinutes'
  | 'defaultUnloadingMinutes'
  | 'qualificationExpiryWarningDays'
  | 'employeeNumberNextValue'
  | 'customerNumberNextValue'
  | 'driverNumberNextValue'
  | 'orderNumberNextValue'
  | 'tripNumberNextValue'
  | 'invoiceNumberNextValue'
  | 'vehicleNumberNextValue'
  | 'trailerNumberNextValue'
  | 'defaultPageSize'

export function SettingsPage() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('company_settings.manage')

  const [loaded, setLoaded] = useState<CompanySettings | null>(null)
  const [form, setForm] = useState<CompanySettings | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let mounted = true
    getCompanySettings()
      .then((data) => {
        if (!mounted) return
        setLoaded(data)
        setForm(data)
      })
      .catch(() => {
        if (mounted) setLoadError(t('settingsPages.company.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const dirty = form !== null && loaded !== null && JSON.stringify(form) !== JSON.stringify(loaded)

  // Unsaved-changes protection: warn before the tab is closed/reloaded while dirty.
  useEffect(() => {
    if (!dirty) return
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [dirty])

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!form) return <p className="placeholder-text">{t('settingsPages.company.loading')}</p>

  function setField<K extends keyof CompanySettings>(key: K, value: CompanySettings[K]) {
    setForm((current) => (current ? { ...current, [key]: value } : current))
  }

  function text(key: keyof CompanySettings, label: string, opts?: { required?: boolean; maxLength?: number }): ReactNode {
    const value = (form![key] as string | null) ?? ''
    return (
      <FormField label={label} htmlFor={`f-${key}`} required={opts?.required}>
        <input
          id={`f-${key}`}
          type="text"
          maxLength={opts?.maxLength}
          value={value}
          disabled={!canManage || saving}
          onChange={(e) => setField(key, (e.target.value === '' ? null : e.target.value) as CompanySettings[typeof key])}
        />
      </FormField>
    )
  }

  function num(key: NumericField, label: string, opts?: { min?: number; max?: number; step?: number }): ReactNode {
    return (
      <FormField label={label} htmlFor={`f-${key}`}>
        <input
          id={`f-${key}`}
          type="number"
          min={opts?.min}
          max={opts?.max}
          step={opts?.step}
          value={form![key]}
          disabled={!canManage || saving}
          onChange={(e) => setField(key, (Number(e.target.value) || 0) as CompanySettings[NumericField])}
        />
      </FormField>
    )
  }

  async function handleSave() {
    if (!form) return
    if (form.defaultCurrency.trim().length !== 3) {
      showError(t('settingsPages.company.currencyLength'))
      return
    }
    setSaving(true)
    try {
      const updated = await updateCompanySettings(form)
      setLoaded(updated)
      setForm(updated)
      // The central date formatter follows the saved preference immediately.
      setDateFormatPreference(updated.dateFormat)
      showSuccess(t('settingsPages.company.saved'))
    } catch (err) {
      // Preserve the backend's specific validation message (e.g. an unknown country code).
      showError(describeApiError(err, t('settingsPages.company.saveFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  // De taalkiezer toont endoniemen (LANGUAGE_NAMES); een onbekende opgeslagen waarde blijft
  // zichtbaar als ruwe technische waarde zodat er niets stilzwijgend verspringt (§91).
  const knownLanguage = (LOCALES as readonly string[]).includes(form.defaultLanguage)

  return (
    <div>
      <UnsavedChangesGuard when={dirty && !saving} />
      <Breadcrumbs items={[{ label: t('navigation.menu.settings') }]} />
      <PageHeader
        title={t('settingsPages.company.title')}
        subtitle={canManage ? undefined : t('settingsPages.company.readOnlySubtitle')}
        action={
          canManage ? (
            <div className="settings-actions">
              {dirty && (
                <Button variant="secondary" onClick={() => setForm(loaded)} disabled={saving}>
                  {t('settingsPages.company.restore')}
                </Button>
              )}
              <Button onClick={handleSave} disabled={!dirty || saving}>
                {saving ? t('settingsPages.common.saving') : t('ui.actions.save')}
              </Button>
            </div>
          ) : undefined
        }
      />

      {dirty && (
        <div className="settings-dirty" role="status">
          {t('settingsPages.company.dirty')}
        </div>
      )}

      <div className="settings-sections">
        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.profile')}</h2>
          <div className="settings-grid">
            {text('companyLegalName', t('settingsPages.company.fields.legalName'))}
            {text('tradingName', t('settingsPages.company.fields.tradingName'))}
            {text('companyNumber', t('settingsPages.company.fields.companyNumber'))}
            {text('vatNumber', t('settingsPages.company.fields.vatNumber'))}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.registeredOffice')}</h2>
          <div className="settings-grid">
            {text('street', t('settingsPages.company.fields.street'))}
            {text('houseNumber', t('settingsPages.company.fields.houseNumber'))}
            {text('postalCode', t('settingsPages.company.fields.postalCode'))}
            {text('city', t('settingsPages.company.fields.city'))}
            <FormField label={t('settingsPages.company.fields.country')} htmlFor="s-country">
              <CountryCombobox
                id="s-country"
                value={form.countryCode}
                onChange={(code) => setField('countryCode', code)}
                disabled={!canManage || saving}
              />
            </FormField>
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.operationalAddress')}</h2>
          <div className="settings-grid">
            {text('operationalStreet', t('settingsPages.company.fields.street'))}
            {text('operationalHouseNumber', t('settingsPages.company.fields.houseNumber'))}
            {text('operationalPostalCode', t('settingsPages.company.fields.postalCode'))}
            {text('operationalCity', t('settingsPages.company.fields.city'))}
            <FormField label={t('settingsPages.company.fields.country')} htmlFor="s-op-country">
              <CountryCombobox
                id="s-op-country"
                value={form.operationalCountryCode}
                onChange={(code) => setField('operationalCountryCode', code)}
                disabled={!canManage || saving}
              />
            </FormField>
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.contact')}</h2>
          <div className="settings-grid">
            {text('email', t('settingsPages.company.fields.email'))}
            {text('phoneNumber', t('settingsPages.company.fields.phone'))}
            {text('website', t('settingsPages.company.fields.website'))}
          </div>
        </section>

        {/* §90: de regionale sectie is gesplitst in twee duidelijk gelabelde blokken —
            "Taal" (standaardtaal van het bedrijf) en "Regionale weergave" (datum/tijdzone/
            decimaalteken). Zelfde backendvelden, alleen betere groepering. */}
        <section className="settings-card">
          <h2>{t('settingsPages.company.language.section')}</h2>
          <p className="settings-hint">{t('settingsPages.company.language.hint')}</p>
          <div className="settings-grid">
            <FormField label={t('settingsPages.company.fields.defaultLanguage')} htmlFor="f-defaultLanguage" required>
              <select
                id="f-defaultLanguage"
                value={form.defaultLanguage}
                disabled={!canManage || saving}
                onChange={(e) => setField('defaultLanguage', e.target.value)}
              >
                {LOCALES.map((locale) => (
                  <option key={locale} value={locale}>
                    {LANGUAGE_NAMES[locale]}
                  </option>
                ))}
                {!knownLanguage && <option value={form.defaultLanguage}>{form.defaultLanguage}</option>}
              </select>
            </FormField>
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.regional.section')}</h2>
          <p className="settings-hint">{t('settingsPages.company.regional.hint')}</p>
          <div className="settings-grid">
            <FormField label={t('settingsPages.company.fields.dateFormat')} htmlFor="f-dateFormat" required>
              <select
                id="f-dateFormat"
                value={form.dateFormat}
                disabled={!canManage || saving}
                onChange={(e) => setField('dateFormat', e.target.value)}
              >
                {DATE_FORMAT_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {t('settingsPages.company.fields.dateFormatOption', {
                      format: option.toUpperCase(),
                      example: formatExample(option),
                    })}
                  </option>
                ))}
              </select>
            </FormField>
            {text('timezone', t('settingsPages.company.fields.timezone'), { required: true })}
            {text('decimalSeparator', t('settingsPages.company.fields.decimalSeparator'), { required: true, maxLength: 1 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.unitsCurrency')}</h2>
          <div className="settings-grid">
            {text('defaultCurrency', t('settingsPages.company.fields.currency'), { required: true, maxLength: 3 })}
            {text('defaultWeightUnit', t('settingsPages.company.fields.weightUnit'), { required: true })}
            {text('defaultDistanceUnit', t('settingsPages.company.fields.distanceUnit'), { required: true })}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.invoicing')}</h2>
          <div className="settings-grid">
            {text('iban', t('settingsPages.company.fields.iban'))}
            {text('invoiceEmail', t('settingsPages.company.fields.invoiceEmail'))}
            {num('paymentTermDays', t('settingsPages.company.fields.paymentTermDays'), { min: 0, max: 365 })}
            {num('defaultVatRatePercent', t('settingsPages.company.fields.defaultVatRate'), { min: 0, max: 100, step: 0.01 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.transportDefaults')}</h2>
          <div className="settings-grid">
            {num('defaultLoadingMinutes', t('settingsPages.company.fields.loadingMinutes'), { min: 0, max: 1440 })}
            {num('defaultUnloadingMinutes', t('settingsPages.company.fields.unloadingMinutes'), { min: 0, max: 1440 })}
            {num('qualificationExpiryWarningDays', t('settingsPages.company.fields.qualificationWarningDays'), { min: 0, max: 365 })}
            {num('defaultPageSize', t('settingsPages.company.fields.defaultPageSize'), { min: 5, max: 200 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.planningConflicts')}</h2>
          <div className="settings-grid">
            <FormField
              label={t('settingsPages.company.conflicts.trainingVsTrip')}
              htmlFor="set-training-severity"
              hint={t('settingsPages.company.conflicts.trainingHint')}
            >
              <select
                id="set-training-severity"
                value={form.trainingConflictSeverity}
                onChange={(e) => setField('trainingConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Information">{t('settingsPages.company.severity.information')}</option>
                <option value="Warning">{t('settingsPages.company.severity.warning')}</option>
                <option value="Blocking">{t('settingsPages.company.severity.blocking')}</option>
              </select>
            </FormField>
            <FormField
              label={t('settingsPages.company.conflicts.capacityExceeded')}
              htmlFor="set-capacity-severity"
              hint={t('settingsPages.company.conflicts.capacityHint')}
            >
              <select
                id="set-capacity-severity"
                value={form.capacityConflictSeverity}
                onChange={(e) => setField('capacityConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Warning">{t('settingsPages.company.severity.warning')}</option>
                <option value="Blocking">{t('settingsPages.company.severity.blocking')}</option>
              </select>
            </FormField>
            <FormField
              label={t('settingsPages.company.conflicts.shiftVsTrip')}
              htmlFor="set-shift-severity"
              hint={t('settingsPages.company.conflicts.shiftHint')}
            >
              <select
                id="set-shift-severity"
                value={form.shiftOverlapConflictSeverity}
                onChange={(e) => setField('shiftOverlapConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Information">{t('settingsPages.company.severity.information')}</option>
                <option value="Warning">{t('settingsPages.company.severity.warning')}</option>
                <option value="Blocking">{t('settingsPages.company.severity.blocking')}</option>
              </select>
            </FormField>
            <FormField
              label={t('settingsPages.company.conflicts.redelivery')}
              htmlFor="set-redelivery-mode"
              hint={t('settingsPages.company.conflicts.redeliveryHint')}
            >
              <select
                id="set-redelivery-mode"
                value={form.redeliveryMode}
                onChange={(e) => setField('redeliveryMode', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Manual">{t('settingsPages.company.redeliveryModes.manual')}</option>
                <option value="Propose">{t('settingsPages.company.redeliveryModes.propose')}</option>
                <option value="Automatic">{t('settingsPages.company.redeliveryModes.automatic')}</option>
              </select>
            </FormField>
            <FormField
              label={t('settingsPages.company.conflicts.etaNotify')}
              htmlFor="set-eta-shift-notify"
              hint={t('settingsPages.company.conflicts.etaNotifyHint')}
            >
              <input
                id="set-eta-shift-notify"
                type="number"
                min={1}
                max={720}
                value={form.etaShiftNotifyMinutes ?? ''}
                disabled={!canManage || saving}
                onChange={(e) => setField('etaShiftNotifyMinutes', e.target.value === '' ? null : Number(e.target.value))}
              />
            </FormField>
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.numbering')}</h2>
          <div className="settings-grid settings-grid-numbering">
            {text('employeeNumberPrefix', t('settingsPages.company.numbering.employeePrefix'), { maxLength: 20 })}
            {num('employeeNumberNextValue', t('settingsPages.company.numbering.employeeNext'), { min: 1 })}
            {text('customerNumberPrefix', t('settingsPages.company.numbering.customerPrefix'), { maxLength: 20 })}
            {num('customerNumberNextValue', t('settingsPages.company.numbering.customerNext'), { min: 1 })}
            {text('driverNumberPrefix', t('settingsPages.company.numbering.driverPrefix'), { maxLength: 20 })}
            {num('driverNumberNextValue', t('settingsPages.company.numbering.driverNext'), { min: 1 })}
            {text('orderNumberPrefix', t('settingsPages.company.numbering.orderPrefix'), { maxLength: 20 })}
            {num('orderNumberNextValue', t('settingsPages.company.numbering.orderNext'), { min: 1 })}
            {text('tripNumberPrefix', t('settingsPages.company.numbering.tripPrefix'), { maxLength: 20 })}
            {num('tripNumberNextValue', t('settingsPages.company.numbering.tripNext'), { min: 1 })}
            {text('invoiceNumberPrefix', t('settingsPages.company.numbering.invoicePrefix'), { maxLength: 20 })}
            {num('invoiceNumberNextValue', t('settingsPages.company.numbering.invoiceNext'), { min: 1 })}
            {text('vehicleNumberPrefix', t('settingsPages.company.numbering.vehiclePrefix'), { maxLength: 20 })}
            {num('vehicleNumberNextValue', t('settingsPages.company.numbering.vehicleNext'), { min: 1 })}
            {text('trailerNumberPrefix', t('settingsPages.company.numbering.trailerPrefix'), { maxLength: 20 })}
            {num('trailerNumberNextValue', t('settingsPages.company.numbering.trailerNext'), { min: 1 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>{t('settingsPages.company.sections.branding')}</h2>
          <div className="settings-grid">
            {text('logoReference', t('settingsPages.company.fields.logoReference'), { maxLength: 300 })}
          </div>
          <p className="settings-hint">{t('settingsPages.company.brandingHint')}</p>
        </section>
      </div>

      {canManage && (
        <FormActions dirty={dirty}>
          <Button variant="secondary" onClick={() => setForm(loaded)} disabled={!dirty || saving}>
            {t('settingsPages.company.restore')}
          </Button>
          <Button onClick={handleSave} disabled={!dirty || saving}>
            {saving ? t('settingsPages.common.saving') : t('ui.actions.save')}
          </Button>
        </FormActions>
      )}
    </div>
  )
}
