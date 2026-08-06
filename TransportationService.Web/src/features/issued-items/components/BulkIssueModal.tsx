import { useMemo, useRef, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import {
  saveEmployeeIssuedItem,
  type EmployeeIssuedItemInput,
  type IssuedItemTemplate,
} from '../issuedItemsApi'
import { getTemplateDetail, parseNegativeStockPayload, type IssuedItemVariant, type NegativeStockPayload } from '../inventoryApi'
import { NegativeStockConfirmModal } from './NegativeStockConfirmModal'

interface RowState {
  checked: boolean
  variantId: string | null
  quantity: number
  serialNumber: string
}

interface PendingNegativeStock {
  payload: NegativeStockPayload
  templateName: string
  storageLocation: string | null
  retryPayload: EmployeeIssuedItemInput
}

/** Outcome of a single template's save attempt once any negative-stock prompt is resolved. */
type ItemOutcome = 'issued' | 'skipped' | 'stopped'

interface BulkIssueModalProps {
  employeeId: string
  employeeName?: string
  /** Active templates (as returned by listIssuedItemTemplates), grouped here per category. */
  templates: IssuedItemTemplate[]
  canOverrideStock: boolean
  onClose: () => void
  /** Fired after every individual item is created, so the parent can reload the list behind the modal. */
  onItemIssued: () => void
  /** Fired once the whole batch is done (all selections processed); parent shows the toast and closes. */
  onCompleted: (message: string) => void
}

function defaultRow(template: IssuedItemTemplate, checked: boolean, previous?: RowState): RowState {
  return {
    checked,
    variantId: previous?.variantId ?? null,
    quantity: previous?.quantity ?? template.defaultQuantity ?? 1,
    serialNumber: previous?.serialNumber ?? '',
  }
}

/** Bulk-issue flow: pick multiple templates from a category checklist and issue them in one go. */
export function BulkIssueModal({
  employeeId,
  employeeName,
  templates,
  canOverrideStock,
  onClose,
  onItemIssued,
  onCompleted,
}: BulkIssueModalProps) {
  const [rows, setRows] = useState<Record<string, RowState>>({})
  const [variantsByTemplate, setVariantsByTemplate] = useState<Record<string, IssuedItemVariant[]>>({})
  const [variantsLoading, setVariantsLoading] = useState<Record<string, boolean>>({})
  const [issuedDate, setIssuedDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  // busy spans the whole batch (from the first save to the last) so the submit button and
  // inputs stay disabled across an in-flight negative-stock confirmation, not just around it.
  const [busy, setBusy] = useState(false)
  // Tracks only the retry request inside the negative-stock modal; kept separate from `busy` so
  // its own confirm/cancel buttons stay usable while the batch otherwise waits on the user.
  const [confirmSaving, setConfirmSaving] = useState(false)
  const [negativeStock, setNegativeStock] = useState<PendingNegativeStock | null>(null)
  // Resolves the in-flight submit loop once the user confirms or cancels the negative-stock prompt.
  const negativeStockResolverRef = useRef<((outcome: ItemOutcome) => void) | null>(null)

  const groups = useMemo(() => {
    const map = new Map<string, IssuedItemTemplate[]>()
    for (const template of templates) {
      const list = map.get(template.category) ?? []
      list.push(template)
      map.set(template.category, list)
    }
    return Array.from(map.entries())
  }, [templates])

  const selectedRows = useMemo(
    () =>
      templates
        .map((template) => ({ template, row: rows[template.id] }))
        .filter((entry): entry is { template: IssuedItemTemplate; row: RowState } => entry.row?.checked === true),
    [templates, rows],
  )
  const selectedCount = selectedRows.length

  async function loadVariants(template: IssuedItemTemplate) {
    setVariantsLoading((prev) => ({ ...prev, [template.id]: true }))
    try {
      const detail = await getTemplateDetail(template.id)
      setVariantsByTemplate((prev) => ({ ...prev, [template.id]: detail.variants.filter((v) => v.isActive) }))
    } catch {
      /* variant list unavailable; the backend still validates the choice on submit */
    } finally {
      setVariantsLoading((prev) => ({ ...prev, [template.id]: false }))
    }
  }

  function toggleTemplate(template: IssuedItemTemplate) {
    const wasChecked = rows[template.id]?.checked ?? false
    setRows((prev) => ({ ...prev, [template.id]: defaultRow(template, !wasChecked, prev[template.id]) }))
    if (!wasChecked && template.variantsEnabled && !variantsByTemplate[template.id] && !variantsLoading[template.id]) {
      void loadVariants(template)
    }
  }

  function updateRow(templateId: string, patch: Partial<RowState>) {
    setRows((prev) => {
      const current = prev[templateId]
      if (!current) return prev
      return { ...prev, [templateId]: { ...current, ...patch } }
    })
  }

  function buildPayload(template: IssuedItemTemplate, row: RowState): EmployeeIssuedItemInput {
    return {
      templateId: template.id,
      name: template.name,
      category: template.category,
      status: 'Issued',
      issuedDate: issuedDate || null,
      quantity: row.quantity,
      serialNumber: row.serialNumber.trim() || null,
      notes: notes.trim() || null,
      returnedDate: null,
      returnCondition: null,
      variantId: row.variantId,
      returnDisposition: null,
      restoreStock: null,
      overrideReason: null,
    }
  }

  /** Opens the shared negative-stock modal and suspends the submit loop until the user decides. */
  function promptNegativeStock(payload: NegativeStockPayload, template: IssuedItemTemplate, retryPayload: EmployeeIssuedItemInput): Promise<ItemOutcome> {
    return new Promise((resolve) => {
      negativeStockResolverRef.current = resolve
      setNegativeStock({ payload, templateName: template.name, storageLocation: template.storageLocation, retryPayload })
    })
  }

  async function handleNegativeStockConfirm(reason: string) {
    if (!negativeStock) return
    const retry: EmployeeIssuedItemInput = {
      ...negativeStock.retryPayload,
      confirmNegativeStock: true,
      expectedVersion: negativeStock.payload.version,
      overrideReason: reason.trim() === '' ? null : reason.trim(),
    }
    setConfirmSaving(true)
    try {
      await saveEmployeeIssuedItem(employeeId, null, retry)
      onItemIssued()
      setNegativeStock(null)
      negativeStockResolverRef.current?.('issued')
    } catch (err) {
      const conflict = parseNegativeStockPayload(err)
      if (conflict) {
        // Voorraad wijzigde intussen (versionMismatch): toon de nieuwe cijfers, blijf wachten op de gebruiker.
        setNegativeStock({ ...negativeStock, payload: conflict, retryPayload: retry })
        return
      }
      setNegativeStock(null)
      setError(`${negativeStock.templateName}: ${describeApiError(err, 'Het bedrijfsmiddel kon niet worden uitgegeven.').message}`)
      negativeStockResolverRef.current?.('stopped')
    } finally {
      setConfirmSaving(false)
    }
  }

  function handleNegativeStockCancel() {
    setNegativeStock(null)
    negativeStockResolverRef.current?.('skipped')
  }

  async function handleSubmit() {
    if (busy) return // a batch is already in flight; ignore a second click/invocation
    setError(null)
    if (selectedRows.length === 0) return

    for (const { template, row } of selectedRows) {
      if (template.variantsEnabled && !row.variantId) {
        setError('Kies een variant.')
        return
      }
      if (template.requiresSerialNumber && !row.serialNumber.trim()) {
        setError('Een serienummer is verplicht voor dit middel.')
        return
      }
    }

    setBusy(true)
    let issuedCount = 0
    const skipped: string[] = []

    for (const { template, row } of selectedRows) {
      const payload = buildPayload(template, row)
      try {
        await saveEmployeeIssuedItem(employeeId, null, payload)
        onItemIssued()
        issuedCount += 1
      } catch (err) {
        const conflict = parseNegativeStockPayload(err)
        if (conflict) {
          const outcome = await promptNegativeStock(conflict, template, payload)
          if (outcome === 'issued') {
            issuedCount += 1
            continue
          }
          if (outcome === 'skipped') {
            skipped.push(template.name)
            continue
          }
          // 'stopped': error is already set by the confirm handler; keep already-created items.
          setBusy(false)
          return
        }

        setError(`${template.name}: ${describeApiError(err, 'Het bedrijfsmiddel kon niet worden uitgegeven.').message}`)
        setBusy(false)
        return
      }
    }

    setBusy(false)
    const message =
      skipped.length > 0 ? `${issuedCount} uitgegeven, ${skipped.length} overgeslagen` : `${issuedCount} middelen uitgegeven`
    onCompleted(message)
  }

  return (
    <>
      <Modal
        title="Meerdere middelen uitgeven"
        onClose={onClose}
        busy={busy}
        footer={
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Annuleren
            </Button>
            <Button onClick={() => void handleSubmit()} disabled={busy || selectedCount === 0}>
              {busy ? 'Uitgeven…' : `Uitgeven (${selectedCount})`}
            </Button>
          </>
        }
      >
        <div className="bulk-issue-form">
          {error && (
            <div className="issued-items-form-error" role="alert">
              {error}
            </div>
          )}
          <div className="bulk-issue-shared-fields">
            <FormField label="Uitgiftedatum" htmlFor="bulk-issue-date">
              <input
                id="bulk-issue-date"
                type="date"
                value={issuedDate}
                onChange={(e) => setIssuedDate(e.target.value)}
                disabled={busy}
              />
            </FormField>
            <FormField label="Opmerking" htmlFor="bulk-issue-notes">
              <textarea id="bulk-issue-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} />
            </FormField>
          </div>

          {templates.length === 0 && <p className="placeholder-text">Geen actieve sjablonen beschikbaar.</p>}

          {groups.map(([category, categoryTemplates]) => (
            <fieldset className="bulk-issue-category" key={category}>
              <legend>{category}</legend>
              {categoryTemplates.map((template) => {
                const row = rows[template.id]
                const variants = variantsByTemplate[template.id] ?? []
                return (
                  <div className="bulk-issue-row" key={template.id}>
                    <label className="bulk-issue-row-checkbox">
                      <input
                        type="checkbox"
                        checked={row?.checked ?? false}
                        onChange={() => toggleTemplate(template)}
                        disabled={busy}
                      />
                      <span>{template.name}</span>
                    </label>
                    {row?.checked && (
                      <div className="bulk-issue-row-fields">
                        {template.variantsEnabled && (
                          <FormField label="Variant" htmlFor={`bulk-variant-${template.id}`} required>
                            <select
                              id={`bulk-variant-${template.id}`}
                              value={row.variantId ?? ''}
                              onChange={(e) => updateRow(template.id, { variantId: e.target.value || null })}
                              disabled={busy}
                            >
                              <option value="">— Kies variant —</option>
                              {variants.map((variant) => (
                                <option key={variant.id} value={variant.id}>
                                  {variant.label} — voorraad: {variant.currentStock}
                                </option>
                              ))}
                            </select>
                          </FormField>
                        )}
                        <FormField label="Aantal" htmlFor={`bulk-qty-${template.id}`}>
                          <input
                            id={`bulk-qty-${template.id}`}
                            type="number"
                            min={1}
                            value={row.quantity}
                            onChange={(e) => updateRow(template.id, { quantity: Number(e.target.value) || 1 })}
                            disabled={busy}
                          />
                        </FormField>
                        <FormField label="Serienummer" htmlFor={`bulk-serial-${template.id}`}>
                          <input
                            id={`bulk-serial-${template.id}`}
                            value={row.serialNumber}
                            onChange={(e) => updateRow(template.id, { serialNumber: e.target.value })}
                            disabled={busy}
                            maxLength={100}
                          />
                        </FormField>
                      </div>
                    )}
                  </div>
                )
              })}
            </fieldset>
          ))}
        </div>
      </Modal>

      {negativeStock && (
        <NegativeStockConfirmModal
          payload={negativeStock.payload}
          kind="issue"
          employeeName={employeeName}
          storageLocation={negativeStock.storageLocation}
          canConfirm={canOverrideStock}
          busy={confirmSaving}
          onConfirm={(reason) => void handleNegativeStockConfirm(reason)}
          onCancel={handleNegativeStockCancel}
        />
      )}
    </>
  )
}
