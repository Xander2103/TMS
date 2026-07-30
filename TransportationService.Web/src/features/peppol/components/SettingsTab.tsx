import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { listLegalEntities } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntity } from '../../legal-entities/types'
import {
  getPeppolSettings,
  savePeppolSettings,
  testPeppolConnection,
  type PeppolEnvironment,
  type PeppolSettings,
  type PeppolTestConnectionResult,
} from '../api/peppolApi'

interface SettingsTabProps {
  /** Invoervelden bewerken en opslaan (peppol.configure). */
  canConfigure: boolean
  /** "Verbinding testen" (peppol.validate). */
  canValidate: boolean
}

/** "Configuratie": Peppol-instellingen per eigen bedrijf + verbindingstest. */
export function SettingsTab({ canConfigure, canValidate }: SettingsTabProps) {
  const { showSuccess, showError } = useToast()
  const [entities, setEntities] = useState<LegalEntity[]>([])
  const [entityId, setEntityId] = useState('')
  const [settings, setSettings] = useState<PeppolSettings | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [enabled, setEnabled] = useState(false)
  const [environment, setEnvironment] = useState<PeppolEnvironment>('Sandbox')
  const [providerKey, setProviderKey] = useState('sandbox')
  const [emailFallback, setEmailFallback] = useState('')
  const [defaultNote, setDefaultNote] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [busy, setBusy] = useState(false)

  const [testBusy, setTestBusy] = useState(false)
  const [testResult, setTestResult] = useState<PeppolTestConnectionResult | null>(null)

  useEffect(() => {
    let mounted = true
    listLegalEntities()
      .then((rows) => {
        if (!mounted) return
        const active = rows.filter((row) => row.isActive)
        setEntities(active)
        if (active.length > 0) setEntityId((current) => current || active[0].id)
      })
      .catch(() => {
        if (mounted) setLoadError('De eigen bedrijven konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  /** Wisselen van bedrijf reset de transient status; de effect-hook laadt daarna de instellingen. */
  function selectEntity(id: string) {
    setEntityId(id)
    setSettings(null)
    setTestResult(null)
    setFieldErrors({})
  }

  useEffect(() => {
    if (!entityId) return
    let mounted = true
    getPeppolSettings(entityId)
      .then((data) => {
        if (!mounted) return
        setSettings(data)
        setEnabled(data.enabled)
        setEnvironment(data.environment)
        setProviderKey(data.providerKey || 'sandbox')
        setEmailFallback(data.invoiceEmailFallback ?? '')
        setDefaultNote(data.defaultInvoiceNote ?? '')
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De Peppol-instellingen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [entityId])

  const entity = entities.find((row) => row.id === entityId) ?? null

  async function handleSave() {
    if (!entityId) return
    setBusy(true)
    setFieldErrors({})
    try {
      const updated = await savePeppolSettings(entityId, {
        enabled,
        environment,
        providerKey,
        invoiceEmailFallback: emailFallback.trim() || null,
        defaultInvoiceNote: defaultNote.trim() || null,
      })
      setSettings(updated)
      showSuccess('Peppol-instellingen opgeslagen.')
    } catch (err) {
      const { message, fieldErrors: errors } = describeApiError(err, 'De instellingen konden niet worden opgeslagen.')
      setFieldErrors(errors)
      showError(message)
    } finally {
      setBusy(false)
    }
  }

  async function handleTestConnection() {
    if (!entityId) return
    setTestBusy(true)
    setTestResult(null)
    try {
      setTestResult(await testPeppolConnection(entityId))
    } catch (err) {
      showError(describeApiError(err, 'De verbindingstest kon niet worden uitgevoerd.').message)
    } finally {
      setTestBusy(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>

  return (
    <div className="peppol-settings">
      <FormField label="Eigen bedrijf" htmlFor="peppol-entity">
        <select id="peppol-entity" value={entityId} onChange={(e) => selectEntity(e.target.value)}>
          {entities.map((row) => (
            <option key={row.id} value={row.id}>
              {row.tradingName ?? row.legalName}
            </option>
          ))}
        </select>
      </FormField>

      {entity && (
        <p className="peppol-entity-facts">
          <Badge tone={entity.peppolId ? 'success' : 'warning'}>
            {entity.peppolId ? `Peppol-ID: ${entity.peppolId}` : 'Peppol-ID ontbreekt'}
          </Badge>{' '}
          <Badge tone={entity.vatNumber ? 'success' : 'warning'}>
            {entity.vatNumber ? `BTW: ${entity.vatNumber}` : 'BTW-nummer ontbreekt'}
          </Badge>{' '}
          <Badge tone={entity.iban ? 'success' : 'warning'}>{entity.iban ? `IBAN: ${entity.iban}` : 'IBAN ontbreekt'}</Badge>
          <span className="peppol-entity-facts-hint">
            Deze gegevens beheer je via <Link to="/settings/legal-entities">Instellingen → Eigen bedrijven</Link>.
          </span>
        </p>
      )}

      {settings === null && entityId && <p className="placeholder-text">Instellingen laden…</p>}

      {settings !== null && (
        <div className="peppol-settings-form">
          <label className="peppol-enabled-toggle">
            <input
              type="checkbox"
              checked={enabled}
              onChange={(e) => setEnabled(e.target.checked)}
              disabled={!canConfigure || busy}
            />
            Actief
          </label>

          <FormField label="Omgeving" htmlFor="peppol-environment" error={getFieldError(fieldErrors, 'environment')}>
            <select
              id="peppol-environment"
              value={environment}
              onChange={(e) => setEnvironment(e.target.value as PeppolEnvironment)}
              disabled={!canConfigure || busy}
            >
              <option value="Sandbox">Sandbox</option>
              <option value="Live">Live</option>
            </select>
          </FormField>

          <FormField label="Provider" htmlFor="peppol-provider" error={getFieldError(fieldErrors, 'providerKey')}>
            <select
              id="peppol-provider"
              value={providerKey}
              onChange={(e) => setProviderKey(e.target.value)}
              disabled={!canConfigure || busy}
            >
              <option value="sandbox">Sandbox (test-omgeving)</option>
            </select>
          </FormField>

          <FormField
            label="E-mailterugval"
            htmlFor="peppol-email-fallback"
            hint="E-mailadres waarop de factuur wordt afgeleverd als de klant niet via Peppol bereikbaar is."
            error={getFieldError(fieldErrors, 'invoiceEmailFallback')}
          >
            <input
              id="peppol-email-fallback"
              type="email"
              value={emailFallback}
              onChange={(e) => setEmailFallback(e.target.value)}
              disabled={!canConfigure || busy}
              maxLength={200}
            />
          </FormField>

          <FormField
            label="Standaardnotitie"
            htmlFor="peppol-default-note"
            error={getFieldError(fieldErrors, 'defaultInvoiceNote')}
          >
            <textarea
              id="peppol-default-note"
              rows={3}
              value={defaultNote}
              onChange={(e) => setDefaultNote(e.target.value)}
              disabled={!canConfigure || busy}
              maxLength={1000}
            />
          </FormField>

          <div className="peppol-settings-actions">
            {canConfigure && (
              <Button onClick={() => void handleSave()} disabled={busy}>
                {busy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            )}
            {canValidate && (
              <Button variant="secondary" onClick={() => void handleTestConnection()} disabled={testBusy}>
                {testBusy ? 'Testen…' : 'Verbinding testen'}
              </Button>
            )}
          </div>

          {testResult && (
            <div className="peppol-test-result" role="status">
              {testResult.found ? (
                <>
                  <Badge tone="success">Gevonden</Badge>
                  {testResult.providerReference && (
                    <span>
                      {' '}
                      Provider-referentie: <code>{testResult.providerReference}</code>
                    </span>
                  )}
                  {testResult.supportedDocumentTypes.length > 0 && (
                    <p className="peppol-test-doctypes">
                      Ondersteunde documenttypes: {testResult.supportedDocumentTypes.join(', ')}
                    </p>
                  )}
                </>
              ) : (
                <>
                  <Badge tone="danger">Niet gevonden</Badge>
                  {testResult.error && <span> {testResult.error}</span>}
                </>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
