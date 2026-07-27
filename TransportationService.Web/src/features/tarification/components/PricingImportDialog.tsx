import { useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import {
  commitPricingImport,
  downloadAgreementExport,
  previewPricingImport,
  type PricingImportCommitResult,
  type PricingImportMode,
  type PricingImportPreview,
  type PricingImportRuleChange,
} from '../api/pricingImportApi'
import './pricingImportDialog.css'

interface PricingImportDialogProps {
  agreementId: string
  agreementName: string
  onClose: () => void
  /** Invoked after a committed import; the caller decides whether to reload or navigate (DuplicateAsNewVersion lands on a new agreement id). */
  onImported: (result: PricingImportCommitResult) => void
}

const today = () => new Date().toISOString().slice(0, 10)

function ChangeBadge({ label, tone, count }: { label: string; tone: BadgeTone; count: number }) {
  return (
    <Badge tone={tone}>
      {label}: {count}
    </Badge>
  )
}

function RuleChangeRow({ change }: { change: PricingImportRuleChange }) {
  return (
    <tr>
      <td>{change.name}</td>
      <td>
        {change.summary ?? (change.fieldChanges && change.fieldChanges.length > 0 ? change.fieldChanges.join('; ') : '—')}
      </td>
    </tr>
  )
}

/**
 * Rate-table Excel import: preview (counts banner, fouten/waarschuwingen, toevoegen/wijzigen/
 * verwijderen) then a validated commit. Mode controls whether the file lands on this table
 * ("Deze tabel bijwerken") or on a freshly created next version ("Als nieuwe versie
 * importeren") — the latter never touches the source table.
 */
export function PricingImportDialog({ agreementId, agreementName, onClose, onImported }: PricingImportDialogProps) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<PricingImportPreview | null>(null)
  const [applyRemovals, setApplyRemovals] = useState(false)
  const [mode, setMode] = useState<PricingImportMode>('UpdateAgreement')
  const [newName, setNewName] = useState(`${agreementName} (nieuwe versie)`)
  const [newEffectiveFrom, setNewEffectiveFrom] = useState(today())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [committed, setCommitted] = useState<PricingImportCommitResult | null>(null)

  function handleFileChange(nextFile: File | null) {
    setFile(nextFile)
    setPreview(null)
    setCommitted(null)
    setError(null)
  }

  async function handleDownload() {
    setError(null)
    try {
      await downloadAgreementExport(agreementId, agreementName)
    } catch (err) {
      setError(describeApiError(err, 'De tabel kon niet worden gedownload.').message)
    }
  }

  async function handlePreview() {
    if (!file) return
    setBusy(true)
    setError(null)
    setCommitted(null)
    try {
      setPreview(await previewPricingImport(agreementId, file))
    } catch (err) {
      setError(describeApiError(err, 'Het voorbeeld kon niet worden gemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleCommit() {
    if (!file || !preview) return
    if (mode === 'DuplicateAsNewVersion' && (!newName.trim() || !newEffectiveFrom)) {
      setError('Kies een naam en een ingangsdatum voor de nieuwe versie.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await commitPricingImport(agreementId, file, {
        mode,
        applyRemovals,
        newName: mode === 'DuplicateAsNewVersion' ? newName.trim() : null,
        newEffectiveFrom: mode === 'DuplicateAsNewVersion' ? newEffectiveFrom : null,
      })
      setCommitted(result)
      onImported(result)
    } catch (err) {
      setError(describeApiError(err, 'De import is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  const canCommit = preview !== null && preview.errors.length === 0 && !committed

  return (
    <Modal
      title="Tarieventabel importeren"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Sluiten
          </Button>
          <Button onClick={() => void handlePreview()} disabled={busy || !file}>
            Voorbeeld
          </Button>
          <Button onClick={() => void handleCommit()} disabled={busy || !canCommit}>
            Importeren
          </Button>
        </>
      }
    >
      <div className="pricing-import-dialog">
        <p className="customer-form-muted">
          Importeer regels uit een Excel-bestand.{' '}
          <button type="button" className="customer-import-template-link" onClick={() => void handleDownload()} disabled={busy}>
            Huidige tabel downloaden
          </button>
        </p>

        <FormField label="Bestand (.xlsx)" htmlFor="pricing-import-file">
          <input
            id="pricing-import-file"
            type="file"
            accept=".xlsx"
            onChange={(e) => handleFileChange(e.target.files?.[0] ?? null)}
            disabled={busy}
          />
        </FormField>

        <fieldset className="pricing-import-mode">
          <label className="tof-checkbox">
            <input
              type="radio"
              name="pricing-import-mode"
              checked={mode === 'UpdateAgreement'}
              onChange={() => setMode('UpdateAgreement')}
              disabled={busy}
            />
            Deze tabel bijwerken
          </label>
          <label className="tof-checkbox">
            <input
              type="radio"
              name="pricing-import-mode"
              checked={mode === 'DuplicateAsNewVersion'}
              onChange={() => setMode('DuplicateAsNewVersion')}
              disabled={busy}
            />
            Als nieuwe versie importeren
          </label>

          {mode === 'DuplicateAsNewVersion' && (
            <div className="issued-items-form-row">
              <FormField label="Naam nieuwe versie" htmlFor="pricing-import-new-name" required>
                <input
                  id="pricing-import-new-name"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  disabled={busy}
                  maxLength={200}
                />
              </FormField>
              <FormField label="Ingangsdatum" htmlFor="pricing-import-new-from" required>
                <input
                  id="pricing-import-new-from"
                  type="date"
                  value={newEffectiveFrom}
                  onChange={(e) => setNewEffectiveFrom(e.target.value)}
                  disabled={busy}
                />
              </FormField>
            </div>
          )}
        </fieldset>

        <label className="tof-checkbox">
          <input type="checkbox" checked={applyRemovals} onChange={(e) => setApplyRemovals(e.target.checked)} disabled={busy} />
          Verwijderingen toepassen (regels die niet meer in het bestand staan, verwijderen)
        </label>

        {error && (
          <p className="customer-import-message customer-import-message-error" role="alert">
            {error}
          </p>
        )}

        {committed && (
          <p className="pricing-import-summary" role="status">
            Import klaar: {committed.added} toegevoegd, {committed.updated} gewijzigd, {committed.removed} verwijderd.
          </p>
        )}

        {!committed && preview && (
          <>
            <p className="pricing-import-summary">
              <strong>{preview.rowsFound}</strong> rijen gevonden — {preview.rowsValid} geldig,{' '}
              {preview.warnings.length} waarschuwing{preview.warnings.length === 1 ? '' : 'en'},{' '}
              <strong className={preview.errors.length > 0 ? 'customer-import-danger' : undefined}>
                {preview.errors.length} fout{preview.errors.length === 1 ? '' : 'en'}
              </strong>
            </p>
            <div className="pricing-import-badges">
              <ChangeBadge label="Toevoegen" tone="success" count={preview.added.length} />
              <ChangeBadge label="Wijzigen" tone="info" count={preview.updated.length} />
              <ChangeBadge label="Verwijderen" tone="warning" count={preview.removed.length} />
            </div>

            {preview.errors.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>Fouten</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>Rij</th>
                      <th>Melding</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.errors.map((e, index) => (
                      <tr key={`${e.row}-${index}`} className="customer-import-row-error">
                        <td>{e.row}</td>
                        <td>{e.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {preview.warnings.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>Waarschuwingen</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>Rij</th>
                      <th>Melding</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.warnings.map((w, index) => (
                      <tr key={`${w.row}-${index}`}>
                        <td>{w.row}</td>
                        <td>{w.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {preview.added.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>Toevoegen</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>Naam</th>
                      <th>Details</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.added.map((change, index) => (
                      <RuleChangeRow key={`${change.name}-${index}`} change={change} />
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {preview.updated.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>Wijzigen</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>Naam</th>
                      <th>Veldwijzigingen</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.updated.map((change, index) => (
                      <RuleChangeRow key={`${change.ruleId ?? change.name}-${index}`} change={change} />
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {preview.removed.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>Verwijderen</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>Naam</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.removed.map((change, index) => (
                      <tr key={`${change.ruleId ?? change.name}-${index}`}>
                        <td>{change.name}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}
