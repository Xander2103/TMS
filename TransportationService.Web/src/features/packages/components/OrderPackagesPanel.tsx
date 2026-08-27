import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { localizeApiError } from '../../../api/problemDetails'
import {
  bulkCreatePackages,
  cancelPackage,
  commitImport,
  createPackage,
  downloadErrorWorkbook,
  downloadImportTemplate,
  generatePackages,
  listOrderPackages,
  previewImport,
  printLabels,
  relabelPackage,
} from '../api/packagesApi'
import {
  PACKAGE_STATUS_LABELS,
  PACKAGE_STATUS_TONE,
  UNIT_TYPE_LABELS,
  type ImportPreview,
  type Package,
  type PackageUnitType,
} from '../types'
import './packages.css'

interface StopOption {
  id: string
  label: string
}

interface OrderPackagesPanelProps {
  orderId: string
  unloadingStops: StopOption[]
}

/** Colli (tracked packages) on the order detail: list, create, bulk, Excel import, cancel, relabel. */
export function OrderPackagesPanel({ orderId, unloadingStops }: OrderPackagesPanelProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canCreate = hasPermission('packages.create') || hasPermission('packages.manage')
  const canCancel = hasPermission('packages.cancel') || hasPermission('packages.manage')
  const canRelabel = hasPermission('packages.relabel')

  const [packages, setPackages] = useState<Package[] | null>(null)
  const [busy, setBusy] = useState(false)

  const [createOpen, setCreateOpen] = useState(false)
  const [description, setDescription] = useState('')
  const [unitType, setUnitType] = useState<PackageUnitType>('Colli')
  const [externalBarcode, setExternalBarcode] = useState('')
  const [customerReference, setCustomerReference] = useState('')
  const [weight, setWeight] = useState('')
  const [deliveryStopId, setDeliveryStopId] = useState('')
  const [isFragile, setIsFragile] = useState(false)
  const [requiresSignature, setRequiresSignature] = useState(false)

  const [bulkOpen, setBulkOpen] = useState(false)
  const [bulkCount, setBulkCount] = useState('4')
  const [bulkDescription, setBulkDescription] = useState('')
  const [bulkPrefix, setBulkPrefix] = useState('')
  const [bulkPallet, setBulkPallet] = useState(false)

  const [importOpen, setImportOpen] = useState(false)
  const [importFile, setImportFile] = useState<File | null>(null)
  const [importPreview, setImportPreview] = useState<ImportPreview | null>(null)
  const [importAllOrNothing, setImportAllOrNothing] = useState(true)
  const [importAllowUpdates, setImportAllowUpdates] = useState(false)

  const [cancelTarget, setCancelTarget] = useState<Package | null>(null)
  const [actionReason, setActionReason] = useState('')
  const [relabelTarget, setRelabelTarget] = useState<Package | null>(null)

  const reload = useCallback(() => {
    listOrderPackages(orderId)
      .then(setPackages)
      .catch(() => showError(t('packages.panel.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitCreate(event: FormEvent) {
    event.preventDefault()
    if (!description.trim()) {
      showError(t('packages.panel.descriptionRequired'))
      return
    }
    setBusy(true)
    try {
      await createPackage(orderId, {
        description: description.trim(),
        quantity: 1,
        unitType,
        externalBarcode: externalBarcode.trim() || null,
        customerReference: customerReference.trim() || null,
        weightKg: weight.trim() === '' ? null : Number(weight.replace(',', '.')),
        deliveryStopId: deliveryStopId || null,
        isMandatory: true,
        isFragile,
        requiresTemperatureControl: false,
        requiresSignature,
      })
      showSuccess(t('packages.panel.created'))
      setCreateOpen(false)
      setDescription('')
      setExternalBarcode('')
      setCustomerReference('')
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('packages.panel.createFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function submitBulk(event: FormEvent) {
    event.preventDefault()
    const count = Number(bulkCount)
    if (!bulkDescription.trim() || Number.isNaN(count) || count < 1) {
      showError(t('packages.panel.bulkValidation'))
      return
    }
    setBusy(true)
    try {
      const result = await bulkCreatePackages(orderId, {
        count,
        description: bulkDescription.trim(),
        unitType: 'Colli',
        weightKg: null,
        referencePrefix: bulkPrefix.trim() || null,
        groupOnPallet: bulkPallet,
        deliveryStopId: deliveryStopId || null,
      })
      showSuccess(t(result.pallet ? 'packages.panel.bulkCreatedOnPallet' : 'packages.panel.bulkCreated', {
        count: result.packages.length,
      }))
      setBulkOpen(false)
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('packages.panel.bulkFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function runPreview() {
    if (!importFile) return
    setBusy(true)
    try {
      setImportPreview(await previewImport(orderId, importFile))
    } catch (err) {
      showError(localizeApiError(t, err, t('packages.panel.previewFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function runImport() {
    if (!importFile) return
    setBusy(true)
    try {
      const result = await commitImport(orderId, importFile, importAllOrNothing, importAllowUpdates)
      if (result.committed) {
        showSuccess(t('packages.panel.importDone', {
          created: result.created,
          updated: result.updated,
          failed: result.failed,
        }))
      } else {
        showError(t('packages.panel.importAborted'))
      }
      if (result.errorWorkbookBase64) {
        downloadErrorWorkbook(result.errorWorkbookBase64)
      }
      if (result.committed) {
        setImportOpen(false)
        setImportFile(null)
        setImportPreview(null)
        reload()
      }
    } catch (err) {
      showError(localizeApiError(t, err, t('packages.panel.importFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="to-section pk-panel">
      <div className="pk-header">
        <h2>{t('packages.panel.title', { count: packages?.length ?? '…' })}</h2>
        {canCreate && (
          <span className="pk-actions">
            {packages !== null && packages.some((p) => p.status !== 'Cancelled') && (
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => {
                  const printable = packages.filter((p) => p.status !== 'Cancelled').map((p) => p.id)
                  setBusy(true)
                  void printLabels(printable, 'Thermal100x150', null)
                    .then(() => { showSuccess(t('packages.panel.labelsGenerated')); reload() })
                    .catch((err) => showError(localizeApiError(t, err, t('packages.panel.printFailed'))))
                    .finally(() => setBusy(false))
                }}
              >
                {t('packages.panel.labelsPdf')}
              </Button>
            )}
            <Button variant="secondary" onClick={() => void downloadImportTemplate().catch(() => showError(t('packages.panel.templateDownloadFailed')))}>
              {t('packages.panel.template')}
            </Button>
            <Button variant="secondary" onClick={() => { setImportOpen(true); setImportPreview(null); setImportFile(null) }}>
              {t('packages.panel.import')}
            </Button>
            <Button variant="secondary" onClick={() => setBulkOpen(true)}>
              {t('packages.panel.bulk')}
            </Button>
            <Button
              variant="secondary"
              disabled={busy}
              onClick={() => {
                setBusy(true)
                void generatePackages(orderId)
                  .then((result) => {
                    const parts = [
                      result.created > 0 ? t('packages.panel.generatedCreated', { count: result.created }) : null,
                      result.cancelled > 0 ? t('packages.panel.generatedCancelled', { count: result.cancelled }) : null,
                      result.created === 0 && result.cancelled === 0 ? t('packages.panel.generatedNoop') : null,
                    ].filter(Boolean)
                    if (result.message && result.requiresAttention > 0) {
                      showError(result.message)
                    } else {
                      showSuccess(t('packages.panel.generated', { parts: parts.join(', ') }))
                    }
                    reload()
                  })
                  .catch((err) => showError(localizeApiError(t, err, t('packages.panel.generateFailed'))))
                  .finally(() => setBusy(false))
              }}
            >
              {t('packages.panel.generate')}
            </Button>
            <Button onClick={() => setCreateOpen(true)}>{t('packages.panel.newPackage')}</Button>
          </span>
        )}
      </div>

      {packages === null && <p className="placeholder-text">{t('packages.panel.loading')}</p>}
      {packages !== null && packages.length === 0 && (
        <p className="placeholder-text">{t('packages.panel.empty')}</p>
      )}
      {packages !== null && packages.length > 0 && (
        <div className="pk-table-wrap">
          <table className="pk-table">
            <thead>
              <tr>
                <th>{t('packages.panel.numberHeader')}</th>
                <th>{t('packages.panel.descriptionHeader')}</th>
                <th>{t('packages.panel.unitHeader')}</th>
                <th>{t('packages.panel.statusHeader')}</th>
                <th>{t('packages.panel.barcodeHeader')}</th>
                <th>{t('packages.panel.referenceHeader')}</th>
                <th>{t('packages.panel.flagsHeader')}</th>
                {(canCancel || canRelabel) && <th aria-label={t('packages.panel.actionsAria')} />}
              </tr>
            </thead>
            <tbody>
              {packages.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link to={`/packages/${item.id}`}>{item.packageNumber}</Link>
                    {item.parentPackageId && <span className="pk-child-marker" title={t('packages.panel.childMarkerTitle')}> ⧉</span>}
                  </td>
                  <td>{item.description}</td>
                  <td>{t(UNIT_TYPE_LABELS[item.unitType])}</td>
                  <td>
                    <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>
                    {item.exceptionState === 'Open' && <Badge tone="warning">{t('packages.panel.exceptionOpen')}</Badge>}
                  </td>
                  <td className="pk-mono">
                    {item.barcodeValue}
                    {item.externalBarcode && <div className="pk-external">{t('packages.panel.externalBarcode', { barcode: item.externalBarcode })}</div>}
                  </td>
                  <td>{item.customerReference ?? item.externalPackageReference ?? '—'}</td>
                  <td className="pk-flags">
                    {item.isMandatory && <span title={t('packages.panel.mandatoryTitle')}>{t('packages.panel.mandatoryFlag')}</span>}
                    {item.isFragile && <span title={t('packages.panel.fragileTitle')}>🥛</span>}
                    {item.requiresTemperatureControl && <span title={t('packages.panel.temperatureTitle')}>❄️</span>}
                    {item.requiresSignature && <span title={t('packages.panel.signatureTitle')}>✍️</span>}
                  </td>
                  {(canCancel || canRelabel) && (
                    <td className="pk-row-actions">
                      {canRelabel && item.status !== 'Cancelled' && item.status !== 'Delivered' && (
                        <button type="button" className="pk-link" onClick={() => { setRelabelTarget(item); setActionReason('') }}>
                          {t('packages.panel.relabel')}
                        </button>
                      )}
                      {canCancel && item.status !== 'Cancelled' && item.status !== 'Delivered' && (
                        <button type="button" className="pk-link pk-danger" onClick={() => { setCancelTarget(item); setActionReason('') }}>
                          {t('packages.panel.cancel')}
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {createOpen && (
        <Modal
          title={t('packages.panel.createTitle')}
          onClose={() => setCreateOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCreateOpen(false)} disabled={busy}>{t('packages.panel.cancel')}</Button>
              <Button type="submit" form="pk-create-form" disabled={busy}>{t('packages.panel.createSubmit')}</Button>
            </>
          }
        >
          <form id="pk-create-form" className="pk-form" onSubmit={submitCreate} noValidate>
            <FormField label={t('packages.panel.descriptionLabel')} htmlFor="pk-description" required>
              <input id="pk-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="pk-form-row">
              <FormField label={t('packages.panel.unitLabel')} htmlFor="pk-unit">
                <select id="pk-unit" value={unitType} onChange={(e) => setUnitType(e.target.value as PackageUnitType)} disabled={busy}>
                  {(Object.keys(UNIT_TYPE_LABELS) as PackageUnitType[]).map((type) => (
                    <option key={type} value={type}>{t(UNIT_TYPE_LABELS[type])}</option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('packages.panel.weightLabel')} htmlFor="pk-weight">
                <input id="pk-weight" inputMode="decimal" value={weight} onChange={(e) => setWeight(e.target.value)} disabled={busy} />
              </FormField>
            </div>
            <div className="pk-form-row">
              <FormField label={t('packages.panel.externalBarcodeLabel')} htmlFor="pk-ext" hint={t('packages.panel.externalBarcodeHint')}>
                <input id="pk-ext" value={externalBarcode} onChange={(e) => setExternalBarcode(e.target.value)} maxLength={100} disabled={busy} />
              </FormField>
              <FormField label={t('packages.panel.customerReferenceLabel')} htmlFor="pk-ref">
                <input id="pk-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} maxLength={100} disabled={busy} />
              </FormField>
            </div>
            {unloadingStops.length > 1 && (
              <FormField label={t('packages.panel.deliveryStopLabel')} htmlFor="pk-stop">
                <select id="pk-stop" value={deliveryStopId} onChange={(e) => setDeliveryStopId(e.target.value)} disabled={busy}>
                  <option value="">{t('packages.panel.deliveryStopAuto')}</option>
                  {unloadingStops.map((stop) => (
                    <option key={stop.id} value={stop.id}>{stop.label}</option>
                  ))}
                </select>
              </FormField>
            )}
            <div className="pk-checks">
              <label><input type="checkbox" checked={isFragile} onChange={(e) => setIsFragile(e.target.checked)} disabled={busy} /> {t('packages.panel.fragileCheck')}</label>
              <label><input type="checkbox" checked={requiresSignature} onChange={(e) => setRequiresSignature(e.target.checked)} disabled={busy} /> {t('packages.panel.signatureCheck')}</label>
            </div>
          </form>
        </Modal>
      )}

      {bulkOpen && (
        <Modal
          title={t('packages.panel.bulkTitle')}
          onClose={() => setBulkOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setBulkOpen(false)} disabled={busy}>{t('packages.panel.cancel')}</Button>
              <Button type="submit" form="pk-bulk-form" disabled={busy}>{t('packages.panel.createSubmit')}</Button>
            </>
          }
        >
          <form id="pk-bulk-form" className="pk-form" onSubmit={submitBulk} noValidate>
            <div className="pk-form-row">
              <FormField label={t('packages.panel.bulkCountLabel')} htmlFor="pk-bulk-count" required>
                <input id="pk-bulk-count" type="number" min={1} max={500} value={bulkCount} onChange={(e) => setBulkCount(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label={t('packages.panel.bulkPrefixLabel')} htmlFor="pk-bulk-prefix" hint={t('packages.panel.bulkPrefixHint')}>
                <input id="pk-bulk-prefix" value={bulkPrefix} onChange={(e) => setBulkPrefix(e.target.value)} maxLength={40} disabled={busy} />
              </FormField>
            </div>
            <FormField label={t('packages.panel.bulkDescriptionLabel')} htmlFor="pk-bulk-description" required>
              <input id="pk-bulk-description" value={bulkDescription} onChange={(e) => setBulkDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="pk-checks">
              <label>
                <input type="checkbox" checked={bulkPallet} onChange={(e) => setBulkPallet(e.target.checked)} disabled={busy} />
                {t('packages.panel.bulkPalletCheck')}
              </label>
            </div>
          </form>
        </Modal>
      )}

      {importOpen && (
        <Modal
          title={t('packages.panel.importTitle')}
          onClose={() => setImportOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setImportOpen(false)} disabled={busy}>{t('packages.panel.close')}</Button>
              {importPreview === null ? (
                <Button onClick={() => void runPreview()} disabled={busy || !importFile}>{t('packages.panel.preview')}</Button>
              ) : (
                <Button onClick={() => void runImport()} disabled={busy || importPreview.totalRows === 0}>
                  {importAllOrNothing ? t('packages.panel.importAllOrNothing') : t('packages.panel.importValidRows')}
                </Button>
              )}
            </>
          }
        >
          <div className="pk-form">
            <FormField label={t('packages.panel.fileLabel')} htmlFor="pk-import-file">
              <input
                id="pk-import-file"
                type="file"
                accept=".xlsx"
                onChange={(e) => { setImportFile(e.target.files?.[0] ?? null); setImportPreview(null) }}
                disabled={busy}
              />
            </FormField>
            <div className="pk-checks">
              <label>
                <input type="checkbox" checked={importAllOrNothing} onChange={(e) => setImportAllOrNothing(e.target.checked)} disabled={busy} />
                {t('packages.panel.allOrNothingCheck')}
              </label>
              <label>
                <input type="checkbox" checked={importAllowUpdates} onChange={(e) => setImportAllowUpdates(e.target.checked)} disabled={busy} />
                {t('packages.panel.allowUpdatesCheck')}
              </label>
            </div>
            {importPreview && (
              <div className="pk-import-preview">
                <p>
                  <strong>{importPreview.totalRows}</strong> {t('packages.panel.previewRows')} ·{' '}
                  {t('packages.panel.previewNew', { count: importPreview.creates })} ·{' '}
                  {t('packages.panel.previewUpdates', { count: importPreview.updates })} ·{' '}
                  <strong className={importPreview.errors > 0 ? 'pk-danger' : undefined}>
                    {t('packages.panel.previewErrors', { count: importPreview.errors })}
                  </strong>
                </p>
                {importPreview.rows.filter((row) => row.action === 'Error').slice(0, 10).map((row) => (
                  <p key={row.rowNumber} className="pk-import-error">
                    {t('packages.panel.importErrorRow', { row: row.rowNumber, messages: row.messages.join('; ') })}
                  </p>
                ))}
              </div>
            )}
          </div>
        </Modal>
      )}

      {(cancelTarget ?? relabelTarget) && (
        <Modal
          title={cancelTarget
            ? t('packages.panel.cancelTitle', { number: cancelTarget.packageNumber })
            : t('packages.panel.relabelTitle', { number: relabelTarget!.packageNumber })}
          onClose={() => { setCancelTarget(null); setRelabelTarget(null) }}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => { setCancelTarget(null); setRelabelTarget(null) }} disabled={busy}>
                {t('packages.panel.close')}
              </Button>
              <Button
                variant={cancelTarget ? 'danger' : 'primary'}
                disabled={busy}
                onClick={async () => {
                  if (!actionReason.trim()) {
                    showError(t('packages.panel.reasonRequired'))
                    return
                  }
                  setBusy(true)
                  try {
                    if (cancelTarget) {
                      await cancelPackage(cancelTarget.id, actionReason.trim())
                      showSuccess(t('packages.panel.cancelled'))
                    } else {
                      await relabelPackage(relabelTarget!.id, actionReason.trim())
                      showSuccess(t('packages.panel.newBarcodeAssigned'))
                    }
                    setCancelTarget(null)
                    setRelabelTarget(null)
                    reload()
                  } catch (err) {
                    showError(localizeApiError(t, err, t('packages.panel.actionFailed')))
                  } finally {
                    setBusy(false)
                  }
                }}
              >
                {cancelTarget ? t('packages.panel.cancelConfirm') : t('packages.panel.newBarcode')}
              </Button>
            </>
          }
        >
          <div className="pk-form">
            <p>
              {cancelTarget
                ? t('packages.panel.cancelExplanation')
                : t('packages.panel.relabelExplanation')}
            </p>
            <FormField label={t('packages.panel.reasonLabel')} htmlFor="pk-action-reason" required>
              <input id="pk-action-reason" value={actionReason} onChange={(e) => setActionReason(e.target.value)} maxLength={500} disabled={busy} />
            </FormField>
          </div>
        </Modal>
      )}
    </section>
  )
}
