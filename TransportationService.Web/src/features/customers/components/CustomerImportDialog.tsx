import { useState } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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

/** Vertaalsleutel + badge-toon per rij-actie (toon blijft op de code gekeyd). */
const ACTION_PRESENTATION: Record<CustomerImportRowAction, { labelKey: string; tone: BadgeTone }> = {
  Create: { labelKey: 'customers.import.actionCreate', tone: 'success' },
  Update: { labelKey: 'customers.import.actionUpdate', tone: 'info' },
  Error: { labelKey: 'customers.import.actionError', tone: 'danger' },
}

/** Excel customer import: template download, preview and (all-or-nothing) commit. */
export function CustomerImportDialog({ onClose, onImported }: CustomerImportDialogProps) {
  const { t } = useLocale()
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
      setError(describeApiError(err, t('customers.import.templateFailed')).message)
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
      setError(describeApiError(err, t('customers.import.previewFailed')).message)
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
        setError(t('customers.import.abortedAllOrNothing'))
      }
      if (commit.committed) {
        onImported()
      }
    } catch (err) {
      setError(describeApiError(err, t('customers.import.commitFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('customers.import.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.close')}
          </Button>
          <Button onClick={() => void handlePreview()} disabled={busy || !file}>
            {t('customers.import.previewAction')}
          </Button>
          <Button onClick={() => void handleCommit()} disabled={busy || !file || preview === null || result?.committed === true}>
            {t('customers.import.importAction')}
          </Button>
        </>
      }
    >
      <div className="customer-import-dialog">
        <p className="customer-import-intro">
          {t('customers.import.intro')}{' '}
          <button type="button" className="customer-import-template-link" onClick={() => void handleTemplate()} disabled={busy}>
            {t('customers.import.templateDownload')}
          </button>
        </p>

        <FormField label={t('customers.import.fileField')} htmlFor="cust-import-file">
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
            {t('customers.import.allowUpdates')}
          </label>
          <label className="customer-form-checkbox">
            <input
              type="checkbox"
              checked={allOrNothing}
              onChange={(e) => setAllOrNothing(e.target.checked)}
              disabled={busy}
            />
            {t('customers.import.allOrNothing')}
          </label>
          <p className="customer-import-hint">{t('customers.import.allOrNothingHint')}</p>
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
                ? t('customers.import.doneSummary', {
                    created: result.created,
                    updated: result.updated,
                    failed: result.failed,
                  })
                : t('customers.import.nothingImported', { failed: result.failed })}
            </p>
            {result.errorWorkbookBase64 !== null && result.errorWorkbookBase64 !== '' && (
              <Button
                variant="secondary"
                onClick={() => downloadCustomerImportErrorWorkbook(result.errorWorkbookBase64 ?? '')}
              >
                {t('customers.import.errorWorkbookDownload')}
              </Button>
            )}
          </div>
        ) : (
          preview && (
            <p className="customer-import-summary">
              <strong>{preview.totalRows}</strong> {t('customers.import.previewSummaryRows')} · {preview.creates}{' '}
              {t('customers.import.previewSummaryNew')} · {preview.updates} {t('customers.import.previewSummaryUpdate')} ·{' '}
              <strong className={preview.errors > 0 ? 'customer-import-danger' : undefined}>
                {preview.errors} {t('customers.import.previewSummaryErrors')}
              </strong>
            </p>
          )
        )}

        {rows && rows.length > 0 && (
          <div className="customer-import-table-wrapper">
            <table className="customer-import-table">
              <thead>
                <tr>
                  <th>{t('customers.import.columnRow')}</th>
                  <th>{t('customers.import.columnAction')}</th>
                  <th>{t('customers.fields.customerNumber')}</th>
                  <th>{t('customers.fields.name')}</th>
                  <th>{t('customers.import.columnMessages')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => {
                  const presentation = ACTION_PRESENTATION[row.action]
                  return (
                    <tr key={row.rowNumber} className={row.action === 'Error' ? 'customer-import-row-error' : undefined}>
                      <td>{row.rowNumber}</td>
                      <td>
                        <Badge tone={presentation.tone}>{t(presentation.labelKey)}</Badge>
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
