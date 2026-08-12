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
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { getCompanySettings, updateCompanySettings } from '../api/settingsApi'
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
        if (mounted) setLoadError('Instellingen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
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
  if (!form) return <p className="placeholder-text">Instellingen laden…</p>

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
      showError('Valutacode moet uit 3 letters bestaan (bv. EUR).')
      return
    }
    setSaving(true)
    try {
      const updated = await updateCompanySettings(form)
      setLoaded(updated)
      setForm(updated)
      showSuccess('Bedrijfsinstellingen opgeslagen.')
    } catch (err) {
      // Preserve the backend's specific validation message (e.g. an unknown country code).
      showError(describeApiError(err, 'Instellingen konden niet worden opgeslagen.').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <UnsavedChangesGuard when={dirty && !saving} />
      <Breadcrumbs items={[{ label: 'Instellingen' }]} />
      <PageHeader
        title="Bedrijfsinstellingen"
        subtitle={canManage ? undefined : 'Je hebt alleen leesrechten voor deze instellingen.'}
        action={
          canManage ? (
            <div className="settings-actions">
              {dirty && <Button variant="secondary" onClick={() => setForm(loaded)} disabled={saving}>Herstellen</Button>}
              <Button onClick={handleSave} disabled={!dirty || saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          ) : undefined
        }
      />

      {dirty && (
        <div className="settings-dirty" role="status">
          Je hebt niet-opgeslagen wijzigingen.
        </div>
      )}

      <div className="settings-sections">
        <section className="settings-card">
          <h2>Bedrijfsprofiel</h2>
          <div className="settings-grid">
            {text('companyLegalName', 'Juridische naam')}
            {text('tradingName', 'Handelsnaam')}
            {text('companyNumber', 'Ondernemingsnummer')}
            {text('vatNumber', 'BTW-nummer')}
          </div>
        </section>

        <section className="settings-card">
          <h2>Maatschappelijke zetel</h2>
          <div className="settings-grid">
            {text('street', 'Straat')}
            {text('houseNumber', 'Nummer')}
            {text('postalCode', 'Postcode')}
            {text('city', 'Plaats')}
            <FormField label="Land" htmlFor="s-country">
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
          <h2>Operationeel adres</h2>
          <div className="settings-grid">
            {text('operationalStreet', 'Straat')}
            {text('operationalHouseNumber', 'Nummer')}
            {text('operationalPostalCode', 'Postcode')}
            {text('operationalCity', 'Plaats')}
            <FormField label="Land" htmlFor="s-op-country">
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
          <h2>Contact</h2>
          <div className="settings-grid">
            {text('email', 'E-mail')}
            {text('phoneNumber', 'Telefoon')}
            {text('website', 'Website')}
          </div>
        </section>

        <section className="settings-card">
          <h2>Lokalisatie &amp; notatie</h2>
          <div className="settings-grid">
            {text('defaultLanguage', 'Standaardtaal', { required: true, maxLength: 10 })}
            {text('timezone', 'Tijdzone', { required: true })}
            {text('defaultCurrency', 'Valuta (ISO)', { required: true, maxLength: 3 })}
            {text('dateFormat', 'Datumnotatie', { required: true })}
            {text('decimalSeparator', 'Decimaalteken', { required: true, maxLength: 1 })}
            {text('defaultWeightUnit', 'Gewichtseenheid', { required: true })}
            {text('defaultDistanceUnit', 'Afstandseenheid', { required: true })}
          </div>
        </section>

        <section className="settings-card">
          <h2>Facturatie</h2>
          <div className="settings-grid">
            {text('iban', 'IBAN')}
            {text('invoiceEmail', 'Facturatie-e-mail')}
            {num('paymentTermDays', 'Betaaltermijn (dagen)', { min: 0, max: 365 })}
            {num('defaultVatRatePercent', 'Standaard BTW (%)', { min: 0, max: 100, step: 0.01 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>Transportstandaarden</h2>
          <div className="settings-grid">
            {num('defaultLoadingMinutes', 'Laadtijd (min)', { min: 0, max: 1440 })}
            {num('defaultUnloadingMinutes', 'Lostijd (min)', { min: 0, max: 1440 })}
            {num('qualificationExpiryWarningDays', 'Waarschuwing kwalificatie (dagen)', { min: 0, max: 365 })}
            {num('defaultPageSize', 'Standaard paginagrootte', { min: 5, max: 200 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>Planningsconflicten</h2>
          <div className="settings-grid">
            <FormField label="Opleiding vs. rit" htmlFor="set-training-severity" hint="Hoe zwaar telt een opleiding die met een rit botst?">
              <select
                id="set-training-severity"
                value={form.trainingConflictSeverity}
                onChange={(e) => setField('trainingConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Information">Ter info</option>
                <option value="Warning">Waarschuwing</option>
                <option value="Blocking">Blokkerend</option>
              </select>
            </FormField>
            <FormField
              label="Capaciteit overschreden"
              htmlFor="set-capacity-severity"
              hint="Wat gebeurt er als de lading het laadvermogen of volume van voertuig/oplegger overschrijdt?"
            >
              <select
                id="set-capacity-severity"
                value={form.capacityConflictSeverity}
                onChange={(e) => setField('capacityConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Warning">Waarschuwing</option>
                <option value="Blocking">Blokkerend</option>
              </select>
            </FormField>
            <FormField label="Shift vs. rit" htmlFor="set-shift-severity" hint="Hoe zwaar telt een gewone shift die met een rit overlapt?">
              <select
                id="set-shift-severity"
                value={form.shiftOverlapConflictSeverity}
                onChange={(e) => setField('shiftOverlapConflictSeverity', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Information">Ter info</option>
                <option value="Warning">Waarschuwing</option>
                <option value="Blocking">Blokkerend</option>
              </select>
            </FormField>
            <FormField
              label="Herlevering bij mislukte stop"
              htmlFor="set-redelivery-mode"
              hint="Wat gebeurt er nadat een stop als mislukt is gemeld?"
            >
              <select
                id="set-redelivery-mode"
                value={form.redeliveryMode}
                onChange={(e) => setField('redeliveryMode', e.target.value)}
                disabled={!canManage || saving}
              >
                <option value="Manual">Handmatig (enkel incident)</option>
                <option value="Propose">Voorstellen aan dispatch</option>
                <option value="Automatic">Automatisch aanmaken (volgende werkdag)</option>
              </select>
            </FormField>
            <FormField
              label="Klantmelding bij ETA-verschuiving (minuten)"
              htmlFor="set-eta-shift-notify"
              hint="Leeg = geen drempelmeldingen."
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
          <h2>Nummering</h2>
          <div className="settings-grid settings-grid-numbering">
            {text('employeeNumberPrefix', 'Personeel prefix', { maxLength: 20 })}
            {num('employeeNumberNextValue', 'Personeel volgnr', { min: 1 })}
            {text('customerNumberPrefix', 'Klant prefix', { maxLength: 20 })}
            {num('customerNumberNextValue', 'Klant volgnr', { min: 1 })}
            {text('driverNumberPrefix', 'Chauffeur prefix', { maxLength: 20 })}
            {num('driverNumberNextValue', 'Chauffeur volgnr', { min: 1 })}
            {text('orderNumberPrefix', 'Order prefix', { maxLength: 20 })}
            {num('orderNumberNextValue', 'Order volgnr', { min: 1 })}
            {text('tripNumberPrefix', 'Rit prefix', { maxLength: 20 })}
            {num('tripNumberNextValue', 'Rit volgnr', { min: 1 })}
            {text('invoiceNumberPrefix', 'Factuur prefix', { maxLength: 20 })}
            {num('invoiceNumberNextValue', 'Factuur volgnr', { min: 1 })}
            {text('vehicleNumberPrefix', 'Voertuig prefix', { maxLength: 20 })}
            {num('vehicleNumberNextValue', 'Voertuig volgnr', { min: 1 })}
            {text('trailerNumberPrefix', 'Oplegger prefix', { maxLength: 20 })}
            {num('trailerNumberNextValue', 'Oplegger volgnr', { min: 1 })}
          </div>
        </section>

        <section className="settings-card">
          <h2>Branding</h2>
          <div className="settings-grid">
            {text('logoReference', 'Logo-referentie', { maxLength: 300 })}
          </div>
          <p className="settings-hint">
            Verwijzing naar een logobestand. Uploaden van bestanden komt in een latere fase.
          </p>
        </section>
      </div>

      {canManage && (
        <FormActions dirty={dirty}>
          <Button variant="secondary" onClick={() => setForm(loaded)} disabled={!dirty || saving}>
            Herstellen
          </Button>
          <Button onClick={handleSave} disabled={!dirty || saving}>
            {saving ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </FormActions>
      )}
    </div>
  )
}
