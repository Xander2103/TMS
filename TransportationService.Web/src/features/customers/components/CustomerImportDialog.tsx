import { useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import {
  commitCustomerImport,
  downloadCustomerImportErrorWorkbook,
  downloadCustomerImportTemplate,
  previewCustomerImport,
} from '../api/customerImportApi'
import type { CustomerImportCommit, CustomerImportPreview, CustomerImportRowAction } from '../types'
import './customers.css'

interface CustomerImportDialogProps {
  onClose: () => void
  /** Invoked after a committed import so the caller can refresh the customer list. */
  onImported: () => void
}

const ACTION_PRESENTATION: Record<CustomerImportRowAction, { label: string; tone: BadgeTone }> = {
  Create: { label: 'Nieuw', tone: 'success' },
  Update: { label: 'Bijwerken', tone: 'info' },
  Error: { label: 'Fout', tone: 'danger' },
}

/** Excel customer import: template download, preview and (all-or-nothing) commit. */
export function CustomerImportDialog({ onClose, onImported }: CustomerImportDialogProps) {
  const [file, setFile] = useState<File | null>(null)
  const [allowUpdates, setAllowUpdates] = useState(false)
  const [allOrNothing, setAllOrNothing] = useState(true)
  const [preview, setPreview] = useState<CustomerImportPreview | null>(null)
  const [result, setResult] = useState<CustomerImportCommit | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const rows = result?.rows ?? preview?.rows ?? null

  function handleFileChange(nextFile: File | null) {
    setFile(nextFile)
    setPreview(null)
    setResult(null)
    setError(null)
  }

  async function handleTemplate() {
    setError(null)
    try {
      await downloadCustomerImportTemplate()
    } catch (err) {
      setError(describeApiError(err, 'Het sjabloon kon niet worden gedownload.').message)
    }
  }

  async function handlePreview() {
    if (!file) return
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      setPreview(await previewCustomerImport(file))
    } catch (err) {
      setError(describeApiError(err, 'Het voorbeeld kon niet worden gemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleCommit() {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const commit = await commitCustomerImport(file, { allOrNothing, allowUpdates })
      setResult(commit)
      if (!commit.committed) {
        setError('Import afgebroken: het bestand bevat fouten (alles-of-niets).')
      }
      if (commit.committed) {
        onImported()
      }
    } catch (err) {
      setError(describeApiError(err, 'De import is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Klanten importeren"
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
          <Button onClick={() => void handleCommit()} disabled={busy || !file || preview === null || result?.committed === true}>
            Importeren
          </Button>
        </>
      }
    >
      <div className="customer-import-dialog">
        <p className="customer-import-intro">
          Importeer klanten uit een Excel-bestand.{' '}
          <button type="button" className="customer-import-template-link" onClick={() => void handleTemplate()} disabled={busy}>
            Sjabloon downloaden
          </button>
        </p>

        <FormField label="Bestand (.xlsx)" htmlFor="cust-import-file">
          <input
            id="cust-import-file"
            type="file"
            accept=".xlsx"
            onChange={(e) => handleFileChange(e.target.files?.[0] ?? null)}
            disabled={busy}
          />
        </FormField>

        <div className="customer-import-options">
          <label className="customer-form-checkbox">
            <input
              type="checkbox"
              checked={allowUpdates}
              onChange={(e) => setAllowUpdates(e.target.checked)}
              disabled={busy}
            />
            Bestaande klanten bijwerken
          </label>
          <label className="customer-form-checkbox">
            <input
              type="checkbox"
              checked={allOrNothing}
              onChange={(e) => setAllOrNothing(e.target.checked)}
              disabled={busy}
            />
            Alles-of-niets
          </label>
          <p className="customer-import-hint">Bij fouten wordt niets geïmporteerd.</p>
        </div>

        {error && (
          <p className="customer-import-message customer-import-message-error" role="alert">
            {error}
          </p>
        )}

        {result ? (
          <div className="customer-import-summary">
            <p>
              {result.committed
                ? `Import klaar: ${result.created} aangemaakt, ${result.updated} bijgewerkt, ${result.failed} fouten.`
                : `Niets geïmporteerd: ${result.failed} fouten.`}
            </p>
            {result.errorWorkbookBase64 !== null && result.errorWorkbookBase64 !== '' && (
              <Button
                variant="secondary"
                onClick={() => downloadCustomerImportErrorWorkbook(result.errorWorkbookBase64 ?? '')}
              >
                Foutenbestand downloaden
              </Button>
            )}
          </div>
        ) : (
          preview && (
            <p className="customer-import-summary">
              <strong>{preview.totalRows}</strong> rijen · {preview.creates} nieuw · {preview.updates} bijwerken ·{' '}
              <strong className={preview.errors > 0 ? 'customer-import-danger' : undefined}>{preview.errors} fouten</strong>
            </p>
          )
        )}

        {rows && rows.length > 0 && (
          <div className="customer-import-table-wrapper">
            <table className="customer-import-table">
              <thead>
                <tr>
                  <th>Rij</th>
                  <th>Actie</th>
                  <th>Klantnummer</th>
                  <th>Naam</th>
                  <th>Meldingen</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => {
                  const presentation = ACTION_PRESENTATION[row.action]
                  return (
                    <tr key={row.rowNumber} className={row.action === 'Error' ? 'customer-import-row-error' : undefined}>
                      <td>{row.rowNumber}</td>
                      <td>
                        <Badge tone={presentation.tone}>{presentation.label}</Badge>
                      </td>
                      <td>{row.customerNumber ? <code>{row.customerNumber}</code> : '—'}</td>
                      <td>{row.name || '—'}</td>
                      <td>{row.messages.join('; ') || '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </Modal>
  )
}
