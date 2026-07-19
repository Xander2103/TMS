import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  bulkCreatePackages,
  cancelPackage,
  commitImport,
  createPackage,
  downloadErrorWorkbook,
  downloadImportTemplate,
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
      .catch(() => showError('De colli konden niet worden geladen.'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitCreate(event: FormEvent) {
    event.preventDefault()
    if (!description.trim()) {
      showError('Een omschrijving is verplicht.')
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
      showSuccess('Collo aangemaakt.')
      setCreateOpen(false)
      setDescription('')
      setExternalBarcode('')
      setCustomerReference('')
      reload()
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Het collo kon niet worden aangemaakt.')
    } finally {
      setBusy(false)
    }
  }

  async function submitBulk(event: FormEvent) {
    event.preventDefault()
    const count = Number(bulkCount)
    if (!bulkDescription.trim() || Number.isNaN(count) || count < 1) {
      showError('Aantal en omschrijving zijn verplicht.')
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
      showSuccess(`${result.packages.length} colli aangemaakt${result.pallet ? ' op pallet' : ''}.`)
      setBulkOpen(false)
      reload()
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Bulkaanmaak mislukt.')
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
      showError(err instanceof Error ? err.message : 'Voorbeeld mislukt.')
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
        showSuccess(`Import klaar: ${result.created} aangemaakt, ${result.updated} bijgewerkt, ${result.failed} fouten.`)
      } else {
        showError('Import afgebroken: het bestand bevat fouten (alles-of-niets).')
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
      showError(err instanceof Error ? err.message : 'Import mislukt.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="to-section pk-panel">
      <div className="pk-header">
        <h2>Colli ({packages?.length ?? '…'})</h2>
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
                    .then(() => { showSuccess('Etiketten gegenereerd.'); reload() })
                    .catch((err) => showError(err instanceof Error ? err.message : 'Afdrukken mislukt.'))
                    .finally(() => setBusy(false))
                }}
              >
                Etiketten (PDF)
              </Button>
            )}
            <Button variant="secondary" onClick={() => void downloadImportTemplate().catch(() => showError('Download mislukt.'))}>
              Sjabloon
            </Button>
            <Button variant="secondary" onClick={() => { setImportOpen(true); setImportPreview(null); setImportFile(null) }}>
              Importeren
            </Button>
            <Button variant="secondary" onClick={() => setBulkOpen(true)}>
              Bulk
            </Button>
            <Button onClick={() => setCreateOpen(true)}>Nieuw collo</Button>
          </span>
        )}
      </div>

      {packages === null && <p className="placeholder-text">Colli laden…</p>}
      {packages !== null && packages.length === 0 && (
        <p className="placeholder-text">
          Nog geen stukgevolgde colli. Goederenlijnen zonder colli blijven op aantal gescand.
        </p>
      )}
      {packages !== null && packages.length > 0 && (
        <div className="pk-table-wrap">
          <table className="pk-table">
            <thead>
              <tr>
                <th>Nummer</th>
                <th>Omschrijving</th>
                <th>Eenheid</th>
                <th>Status</th>
                <th>Barcode</th>
                <th>Referentie</th>
                <th>Kenmerken</th>
                {(canCancel || canRelabel) && <th aria-label="Acties" />}
              </tr>
            </thead>
            <tbody>
              {packages.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link to={`/packages/${item.id}`}>{item.packageNumber}</Link>
                    {item.parentPackageId && <span className="pk-child-marker" title="Onderdeel van een groep/pallet"> ⧉</span>}
                  </td>
                  <td>{item.description}</td>
                  <td>{UNIT_TYPE_LABELS[item.unitType]}</td>
                  <td>
                    <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{PACKAGE_STATUS_LABELS[item.status]}</Badge>
                    {item.exceptionState === 'Open' && <Badge tone="warning">⚠ afwijking</Badge>}
                  </td>
                  <td className="pk-mono">
                    {item.barcodeValue}
                    {item.externalBarcode && <div className="pk-external">ext: {item.externalBarcode}</div>}
                  </td>
                  <td>{item.customerReference ?? item.externalPackageReference ?? '—'}</td>
                  <td className="pk-flags">
                    {item.isMandatory && <span title="Verplicht collo">✔︎ verplicht</span>}
                    {item.isFragile && <span title="Breekbaar">🥛</span>}
                    {item.requiresTemperatureControl && <span title="Temperatuurgecontroleerd">❄️</span>}
                    {item.requiresSignature && <span title="Handtekening vereist">✍️</span>}
                  </td>
                  {(canCancel || canRelabel) && (
                    <td className="pk-row-actions">
                      {canRelabel && item.status !== 'Cancelled' && item.status !== 'Delivered' && (
                        <button type="button" className="pk-link" onClick={() => { setRelabelTarget(item); setActionReason('') }}>
                          Heretiketteren
                        </button>
                      )}
                      {canCancel && item.status !== 'Cancelled' && item.status !== 'Delivered' && (
                        <button type="button" className="pk-link pk-danger" onClick={() => { setCancelTarget(item); setActionReason('') }}>
                          Annuleren
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
          title="Nieuw collo"
          onClose={() => setCreateOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCreateOpen(false)} disabled={busy}>Annuleren</Button>
              <Button type="submit" form="pk-create-form" disabled={busy}>Aanmaken</Button>
            </>
          }
        >
          <form id="pk-create-form" className="pk-form" onSubmit={submitCreate} noValidate>
            <FormField label="Omschrijving" htmlFor="pk-description" required>
              <input id="pk-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="pk-form-row">
              <FormField label="Eenheid" htmlFor="pk-unit">
                <select id="pk-unit" value={unitType} onChange={(e) => setUnitType(e.target.value as PackageUnitType)} disabled={busy}>
                  {(Object.keys(UNIT_TYPE_LABELS) as PackageUnitType[]).map((type) => (
                    <option key={type} value={type}>{UNIT_TYPE_LABELS[type]}</option>
                  ))}
                </select>
              </FormField>
              <FormField label="Gewicht (kg)" htmlFor="pk-weight">
                <input id="pk-weight" inputMode="decimal" value={weight} onChange={(e) => setWeight(e.target.value)} disabled={busy} />
              </FormField>
            </div>
            <div className="pk-form-row">
              <FormField label="Externe barcode" htmlFor="pk-ext" hint="Optioneel; blijft naast de interne barcode bestaan.">
                <input id="pk-ext" value={externalBarcode} onChange={(e) => setExternalBarcode(e.target.value)} maxLength={100} disabled={busy} />
              </FormField>
              <FormField label="Klantreferentie" htmlFor="pk-ref">
                <input id="pk-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} maxLength={100} disabled={busy} />
              </FormField>
            </div>
            {unloadingStops.length > 1 && (
              <FormField label="Losstop" htmlFor="pk-stop">
                <select id="pk-stop" value={deliveryStopId} onChange={(e) => setDeliveryStopId(e.target.value)} disabled={busy}>
                  <option value="">Automatisch (losstop van de opdracht)</option>
                  {unloadingStops.map((stop) => (
                    <option key={stop.id} value={stop.id}>{stop.label}</option>
                  ))}
                </select>
              </FormField>
            )}
            <div className="pk-checks">
              <label><input type="checkbox" checked={isFragile} onChange={(e) => setIsFragile(e.target.checked)} disabled={busy} /> Breekbaar</label>
              <label><input type="checkbox" checked={requiresSignature} onChange={(e) => setRequiresSignature(e.target.checked)} disabled={busy} /> Handtekening vereist</label>
            </div>
          </form>
        </Modal>
      )}

      {bulkOpen && (
        <Modal
          title="Colli in bulk aanmaken"
          onClose={() => setBulkOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setBulkOpen(false)} disabled={busy}>Annuleren</Button>
              <Button type="submit" form="pk-bulk-form" disabled={busy}>Aanmaken</Button>
            </>
          }
        >
          <form id="pk-bulk-form" className="pk-form" onSubmit={submitBulk} noValidate>
            <div className="pk-form-row">
              <FormField label="Aantal colli" htmlFor="pk-bulk-count" required>
                <input id="pk-bulk-count" type="number" min={1} max={500} value={bulkCount} onChange={(e) => setBulkCount(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label="Referentievoorvoegsel" htmlFor="pk-bulk-prefix" hint="Geeft REF-1, REF-2, … per collo.">
                <input id="pk-bulk-prefix" value={bulkPrefix} onChange={(e) => setBulkPrefix(e.target.value)} maxLength={40} disabled={busy} />
              </FormField>
            </div>
            <FormField label="Gedeelde omschrijving" htmlFor="pk-bulk-description" required>
              <input id="pk-bulk-description" value={bulkDescription} onChange={(e) => setBulkDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="pk-checks">
              <label>
                <input type="checkbox" checked={bulkPallet} onChange={(e) => setBulkPallet(e.target.checked)} disabled={busy} />
                Groepeer op één pallet (parent-collo met palletbarcode)
              </label>
            </div>
          </form>
        </Modal>
      )}

      {importOpen && (
        <Modal
          title="Colli importeren uit Excel"
          onClose={() => setImportOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setImportOpen(false)} disabled={busy}>Sluiten</Button>
              {importPreview === null ? (
                <Button onClick={() => void runPreview()} disabled={busy || !importFile}>Voorbeeld</Button>
              ) : (
                <Button onClick={() => void runImport()} disabled={busy || importPreview.totalRows === 0}>
                  {importAllOrNothing ? 'Importeren (alles-of-niets)' : 'Importeren (geldige rijen)'}
                </Button>
              )}
            </>
          }
        >
          <div className="pk-form">
            <FormField label="Bestand (.xlsx)" htmlFor="pk-import-file">
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
                Alles-of-niets (één fout breekt de hele import af)
              </label>
              <label>
                <input type="checkbox" checked={importAllowUpdates} onChange={(e) => setImportAllowUpdates(e.target.checked)} disabled={busy} />
                Bestaande colli bijwerken via collonummer
              </label>
            </div>
            {importPreview && (
              <div className="pk-import-preview">
                <p>
                  <strong>{importPreview.totalRows}</strong> rijen · {importPreview.creates} nieuw ·{' '}
                  {importPreview.updates} bijwerken · <strong className={importPreview.errors > 0 ? 'pk-danger' : undefined}>{importPreview.errors} fouten</strong>
                </p>
                {importPreview.rows.filter((row) => row.action === 'Error').slice(0, 10).map((row) => (
                  <p key={row.rowNumber} className="pk-import-error">
                    Rij {row.rowNumber}: {row.messages.join('; ')}
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
            ? `Collo ${cancelTarget.packageNumber} annuleren`
            : `Collo ${relabelTarget!.packageNumber} heretiketteren`}
          onClose={() => { setCancelTarget(null); setRelabelTarget(null) }}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => { setCancelTarget(null); setRelabelTarget(null) }} disabled={busy}>
                Sluiten
              </Button>
              <Button
                variant={cancelTarget ? 'danger' : 'primary'}
                disabled={busy}
                onClick={async () => {
                  if (!actionReason.trim()) {
                    showError('Een reden is verplicht.')
                    return
                  }
                  setBusy(true)
                  try {
                    if (cancelTarget) {
                      await cancelPackage(cancelTarget.id, actionReason.trim())
                      showSuccess('Collo geannuleerd.')
                    } else {
                      await relabelPackage(relabelTarget!.id, actionReason.trim())
                      showSuccess('Nieuwe barcode toegekend.')
                    }
                    setCancelTarget(null)
                    setRelabelTarget(null)
                    reload()
                  } catch (err) {
                    showError(err instanceof Error ? err.message : 'De actie is mislukt.')
                  } finally {
                    setBusy(false)
                  }
                }}
              >
                {cancelTarget ? 'Annuleren' : 'Nieuwe barcode'}
              </Button>
            </>
          }
        >
          <div className="pk-form">
            <p>
              {cancelTarget
                ? 'Geef een reden op; annulering wordt vastgelegd in de historiek.'
                : 'De oude barcode blijft in de historiek en wordt gemarkeerd als vervangen.'}
            </p>
            <FormField label="Reden" htmlFor="pk-action-reason" required>
              <input id="pk-action-reason" value={actionReason} onChange={(e) => setActionReason(e.target.value)} maxLength={500} disabled={busy} />
            </FormField>
          </div>
        </Modal>
      )}
    </section>
  )
}
