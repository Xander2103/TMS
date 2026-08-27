import { useEffect, useState } from 'react'
import { formatDateTime } from '../../../utils/dates'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  commitPricingImport,
  downloadAgreementExport,
  previewPricingImport,
  listPricingImportProfiles,
  listPricingImportHistory,
  type PricingImportProfile,
  type PricingImportRun,
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
  const { t } = useLocale()
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<PricingImportPreview | null>(null)
  const [applyRemovals, setApplyRemovals] = useState(false)
  const [mode, setMode] = useState<PricingImportMode>('UpdateAgreement')
  const [newName, setNewName] = useState(() => t('tarification.importDialog.newVersionDefault', { name: agreementName }))
  const [newEffectiveFrom, setNewEffectiveFrom] = useState(today())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [committed, setCommitted] = useState<PricingImportCommitResult | null>(null)
  // Sprint 4D/4F: read a customer's own layout through a saved mapping, and show what was
  // imported into this table before.
  const [profiles, setProfiles] = useState<PricingImportProfile[]>([])
  const [profileId, setProfileId] = useState<string>('')
  const [history, setHistory] = useState<PricingImportRun[]>([])

  useEffect(() => {
    void listPricingImportProfiles()
      .then((data) => setProfiles(data.filter((profile) => profile.isActive)))
      .catch(() => undefined)
  }, [])

  useEffect(() => {
    void listPricingImportHistory(agreementId)
      .then(setHistory)
      .catch(() => undefined)
    // Refresh once an import lands so the history reflects it immediately.
  }, [agreementId, committed])

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
      setError(localizeApiError(t, err, t('tarification.importDialog.downloadError')))
    }
  }

  async function handlePreview() {
    if (!file) return
    setBusy(true)
    setError(null)
    setCommitted(null)
    try {
      setPreview(await previewPricingImport(agreementId, file, profileId || null))
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.importDialog.previewError')))
    } finally {
      setBusy(false)
    }
  }

  async function handleCommit() {
    if (!file || !preview) return
    if (mode === 'DuplicateAsNewVersion' && (!newName.trim() || !newEffectiveFrom)) {
      setError(t('tarification.importDialog.nameDateRequired'))
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await commitPricingImport(agreementId, file, {
        mode,
        applyRemovals,
        profileId: profileId || null,
        newName: mode === 'DuplicateAsNewVersion' ? newName.trim() : null,
        newEffectiveFrom: mode === 'DuplicateAsNewVersion' ? newEffectiveFrom : null,
      })
      setCommitted(result)
      onImported(result)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.importDialog.commitError')))
    } finally {
      setBusy(false)
    }
  }

  const canCommit = preview !== null && preview.errors.length === 0 && !committed

  return (
    <Modal
      title={t('tarification.importDialog.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.close')}
          </Button>
          <Button onClick={() => void handlePreview()} disabled={busy || !file}>
            {t('tarification.common.preview')}
          </Button>
          <Button onClick={() => void handleCommit()} disabled={busy || !canCommit}>
            {t('tarification.importDialog.import')}
          </Button>
        </>
      }
    >
      <div className="pricing-import-dialog">
        <p className="customer-form-muted">
          {t('tarification.importDialog.intro')}{' '}
          <button type="button" className="customer-import-template-link" onClick={() => void handleDownload()} disabled={busy}>
            {t('tarification.importDialog.downloadCurrent')}
          </button>
        </p>

        <FormField label={t('tarification.importDialog.fileLabel')} htmlFor="pricing-import-file">
          <input
            id="pricing-import-file"
            type="file"
            accept=".xlsx"
            onChange={(e) => handleFileChange(e.target.files?.[0] ?? null)}
            disabled={busy}
          />
        </FormField>

        <FormField
          label={t('tarification.import.profileLabel')}
          htmlFor="pricing-import-profile"
          hint={t('tarification.import.profileHint')}
        >
          <select
            id="pricing-import-profile"
            value={profileId}
            onChange={(e) => {
              setProfileId(e.target.value)
              // The mapping decides how the file is read, so an existing preview is stale.
              setPreview(null)
            }}
            disabled={busy}
          >
            <option value="">{t('tarification.import.profileNone')}</option>
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>
                {profile.name}
              </option>
            ))}
          </select>
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
            {t('tarification.importDialog.modeUpdate')}
          </label>
          <label className="tof-checkbox">
            <input
              type="radio"
              name="pricing-import-mode"
              checked={mode === 'DuplicateAsNewVersion'}
              onChange={() => setMode('DuplicateAsNewVersion')}
              disabled={busy}
            />
            {t('tarification.importDialog.modeNewVersion')}
          </label>

          {mode === 'DuplicateAsNewVersion' && (
            <div className="issued-items-form-row">
              <FormField label={t('tarification.importDialog.newNameLabel')} htmlFor="pricing-import-new-name" required>
                <input
                  id="pricing-import-new-name"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  disabled={busy}
                  maxLength={200}
                />
              </FormField>
              <FormField label={t('tarification.common.effectiveDate')} htmlFor="pricing-import-new-from" required>
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
          {t('tarification.importDialog.applyRemovals')}
        </label>

        {error && (
          <p className="customer-import-message customer-import-message-error" role="alert">
            {error}
          </p>
        )}

        {committed && (
          <p className="pricing-import-summary" role="status">
            {t('tarification.importDialog.done', { added: committed.added, updated: committed.updated, removed: committed.removed })}
          </p>
        )}

        {!committed && !preview && (
          <details className="pricing-import-history">
            <summary>{t('tarification.import.historyTitle')}</summary>
            {history.length === 0 && <p className="placeholder-text">{t('tarification.import.historyEmpty')}</p>}
            {history.length > 0 && (
              <table className="issued-items-table">
                <thead>
                  <tr>
                    <th>{t('tarification.import.historyColumnWhen')}</th>
                    <th>{t('tarification.import.historyColumnFile')}</th>
                    <th>{t('tarification.import.historyColumnProfile')}</th>
                    <th>{t('tarification.import.historyColumnResult')}</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((run) => (
                    <tr key={run.id}>
                      <td>{formatDateTime(run.importedAt)}</td>
                      <td>{run.fileName}</td>
                      <td>{run.profileName ?? t('tarification.import.profileNone')}</td>
                      <td>
                        {t('tarification.import.historyResult', {
                          read: run.rowsRead,
                          created: run.created,
                          updated: run.updated,
                          failed: run.failed,
                        })}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </details>
        )}

        {!committed && preview && (
          <>
            {preview.alreadyImported && (
              <p className="customer-form-warning" role="status">
                {t('tarification.import.alreadyImported', {
                  when: preview.previousImportAt ? formatDateTime(preview.previousImportAt) : '—',
                  file: preview.previousImportFileName ?? '—',
                })}
              </p>
            )}
            <p className="pricing-import-summary">
              <strong>{preview.rowsFound}</strong> {t('tarification.importDialog.rowsFoundTail', { valid: preview.rowsValid })}{' '}
              {t('tarification.importDialog.warningCount', { count: preview.warnings.length })},{' '}
              <strong className={preview.errors.length > 0 ? 'customer-import-danger' : undefined}>
                {t('tarification.importDialog.errorCount', { count: preview.errors.length })}
              </strong>
            </p>
            <div className="pricing-import-badges">
              <ChangeBadge label={t('tarification.importDialog.badgeAdd')} tone="success" count={preview.added.length} />
              <ChangeBadge label={t('tarification.importDialog.badgeUpdate')} tone="info" count={preview.updated.length} />
              <ChangeBadge label={t('tarification.importDialog.badgeRemove')} tone="warning" count={preview.removed.length} />
            </div>

            {preview.errors.length > 0 && (
              <div className="pricing-import-table-wrapper customer-import-table-wrapper">
                <h4>{t('tarification.importDialog.headingErrors')}</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>{t('tarification.importDialog.colRow')}</th>
                      <th>{t('tarification.importDialog.colMessage')}</th>
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
                <h4>{t('tarification.importDialog.headingWarnings')}</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>{t('tarification.importDialog.colRow')}</th>
                      <th>{t('tarification.importDialog.colMessage')}</th>
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
                <h4>{t('tarification.importDialog.badgeAdd')}</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>{t('tarification.common.name')}</th>
                      <th>{t('tarification.importDialog.colDetails')}</th>
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
                <h4>{t('tarification.importDialog.badgeUpdate')}</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>{t('tarification.common.name')}</th>
                      <th>{t('tarification.importDialog.colFieldChanges')}</th>
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
                <h4>{t('tarification.importDialog.badgeRemove')}</h4>
                <table className="customer-import-table">
                  <thead>
                    <tr>
                      <th>{t('tarification.common.name')}</th>
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
