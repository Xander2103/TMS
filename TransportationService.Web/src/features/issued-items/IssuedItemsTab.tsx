import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
import { useAuth } from '../auth/authContextValue'
import { useLocale } from '../../i18n/localeContext'
import { describeApiError } from '../../api/problemDetails'
import { formatDate } from '../../utils/dates'
import {
  deleteEmployeeIssuedItem,
  downloadIssuedItemsAcknowledgement,
  ISSUED_ITEM_STATUS_LABELS,
  ISSUED_ITEM_STATUSES,
  listEmployeeIssuedItems,
  listIssuedItemTemplates,
  RETURN_DISPOSITION_LABELS,
  saveEmployeeIssuedItem,
  type EmployeeIssuedItem,
  type EmployeeIssuedItemInput,
  type IssuedItemStatus,
  type IssuedItemTemplate,
  type ReturnDisposition,
} from './issuedItemsApi'
import {
  getTemplateDetail,
  parseNegativeStockPayload,
  type IssuedItemVariant,
  type NegativeStockPayload,
} from './inventoryApi'
import { NegativeStockConfirmModal } from './components/NegativeStockConfirmModal'
import { BulkIssueModal } from './components/BulkIssueModal'
import './issued-items.css'

const STATUS_TONE: Record<IssuedItemStatus, BadgeTone> = {
  NotIssued: 'neutral',
  Issued: 'success',
  Returned: 'info',
  Missing: 'danger',
  Damaged: 'warning',
}

function emptyForm(): EmployeeIssuedItemInput {
  return {
    templateId: null,
    name: '',
    category: '',
    status: 'Issued',
    issuedDate: new Date().toISOString().slice(0, 10),
    quantity: 1,
    serialNumber: null,
    notes: null,
    returnedDate: null,
    returnCondition: null,
    variantId: null,
    returnDisposition: null,
    restoreStock: null,
    overrideReason: null,
  }
}

/** Employee "Bedrijfsmiddelen" checklist: stock-aware issue/return flow, PDF acknowledgement. */
export function IssuedItemsTab({ employeeId, employeeName }: { employeeId: string; employeeName?: string }) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('issued_items.manage')
  const canOverrideStock = hasPermission('inventory.override_negative_stock')

  const [items, setItems] = useState<EmployeeIssuedItem[] | null>(null)
  const [templates, setTemplates] = useState<IssuedItemTemplate[]>([])
  // Vertaalsleutels in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [bulkIssueOpen, setBulkIssueOpen] = useState(false)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<EmployeeIssuedItemInput>(emptyForm())
  const [variants, setVariants] = useState<IssuedItemVariant[]>([])
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<EmployeeIssuedItem | null>(null)
  // 409-bevestigingsflow: payload van de server + de payload die opnieuw verstuurd moet worden.
  const [negativeStock, setNegativeStock] = useState<{ payload: NegativeStockPayload; input: EmployeeIssuedItemInput } | null>(null)

  useEffect(() => {
    let mounted = true
    listEmployeeIssuedItems(employeeId)
      .then((data) => {
        if (!mounted) return
        setItems(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('issuedItems.tab.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, reloadToken])

  useEffect(() => {
    let mounted = true
    if (canManage) {
      listIssuedItemTemplates()
        .then((data) => {
          if (mounted) setTemplates(data)
        })
        .catch(() => {})
    }
    return () => {
      mounted = false
    }
  }, [canManage])

  const selectedTemplate = useMemo(
    () => templates.find((tpl) => tpl.id === form.templateId) ?? null,
    [templates, form.templateId],
  )
  const selectedVariant = useMemo(
    () => variants.find((v) => v.id === form.variantId) ?? null,
    [variants, form.variantId],
  )
  const stockTracked = selectedTemplate?.stockTrackingEnabled ?? false
  const availableStock = selectedTemplate?.variantsEnabled
    ? selectedVariant?.currentStock ?? null
    : stockTracked
      ? selectedTemplate?.totalAvailable ?? null
      : null
  const stockShortage =
    stockTracked &&
    form.status === 'Issued' &&
    !editingId &&
    availableStock !== null &&
    form.quantity > availableStock &&
    !(selectedTemplate?.allowNegativeStock ?? false)

  function set<K extends keyof EmployeeIssuedItemInput>(key: K, value: EmployeeIssuedItemInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function applyTemplate(templateId: string) {
    const template = templates.find((tpl) => tpl.id === templateId)
    setVariants([])
    if (!template) {
      setForm((f) => ({ ...f, templateId: null, variantId: null }))
      return
    }
    setForm((f) => ({
      ...f,
      templateId: template.id,
      name: template.name,
      category: template.category,
      quantity: template.defaultQuantity,
      variantId: null,
    }))
    if (template.variantsEnabled) {
      try {
        const detail = await getTemplateDetail(template.id)
        setVariants(detail.variants.filter((v) => v.isActive))
      } catch {
        /* variant list unavailable; backend still validates the choice */
      }
    }
  }

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm())
    setVariants([])
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(item: EmployeeIssuedItem) {
    setEditingId(item.id)
    setForm({
      templateId: item.templateId,
      name: item.name,
      category: item.category,
      status: item.status,
      issuedDate: item.issuedDate,
      quantity: item.quantity,
      serialNumber: item.serialNumber,
      notes: item.notes,
      returnedDate: item.returnedDate,
      returnCondition: item.returnCondition,
      variantId: item.variantId,
      returnDisposition: item.status === 'Returned' ? 'good' : null,
      restoreStock: null,
      overrideReason: null,
    })
    setVariants([])
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.name?.trim() || !form.category?.trim()) {
      setFormError(t('issuedItems.tab.nameCategoryRequired'))
      return
    }
    if (!editingId && selectedTemplate?.variantsEnabled && !form.variantId) {
      setFormError(t('issuedItems.tab.variantRequired'))
      return
    }
    if (selectedTemplate?.requiresSerialNumber && form.status === 'Issued' && !form.serialNumber?.trim()) {
      setFormError(t('issuedItems.tab.serialRequired'))
      return
    }
    const payload: EmployeeIssuedItemInput = {
      ...form,
      returnDisposition: form.status === 'Returned' ? (form.returnDisposition ?? 'good') : null,
      restoreStock: form.status === 'Returned' && form.returnDisposition === 'good' ? (form.restoreStock ?? true) : null,
    }
    setSaving(true)
    try {
      await saveEmployeeIssuedItem(employeeId, editingId, payload)
      showSuccess(editingId ? t('issuedItems.tab.updated') : t('issuedItems.tab.added'))
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      const conflict = parseNegativeStockPayload(err)
      if (conflict) {
        setNegativeStock({ payload: conflict, input: payload })
        return
      }
      setFormError(describeApiError(err, t('issuedItems.tab.saveFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  /** Verstuurt dezelfde save opnieuw, met de bevestigingsvelden uit de 409-payload. */
  async function handleNegativeStockConfirm(reason: string) {
    if (!negativeStock) return
    const retry: EmployeeIssuedItemInput = {
      ...negativeStock.input,
      confirmNegativeStock: true,
      expectedVersion: negativeStock.payload.version,
      overrideReason: reason.trim() === '' ? null : reason.trim(),
    }
    setSaving(true)
    try {
      await saveEmployeeIssuedItem(employeeId, editingId, retry)
      showSuccess(editingId ? t('issuedItems.tab.updated') : t('issuedItems.tab.added'))
      setNegativeStock(null)
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      // Nieuwe 409 (bv. versionMismatch): toon de nieuwe cijfers en laat opnieuw bevestigen.
      const conflict = parseNegativeStockPayload(err)
      if (conflict) {
        setNegativeStock({ payload: conflict, input: negativeStock.input })
        return
      }
      setNegativeStock(null)
      setFormError(describeApiError(err, t('issuedItems.tab.saveFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteEmployeeIssuedItem(employeeId, deleteTarget.id)
      showSuccess(t('issuedItems.tab.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('issuedItems.tab.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  async function handleDownload() {
    try {
      await downloadIssuedItemsAcknowledgement(employeeId)
    } catch {
      showError(t('issuedItems.tab.downloadFailed'))
    }
  }

  return (
    <section className="issued-items">
      <div className="issued-items-header">
        <h2>{t('issuedItems.tab.title')}</h2>
        <div className="issued-items-actions-top">
          {items !== null && items.length > 0 && (
            <Button variant="secondary" onClick={handleDownload}>
              {t('issuedItems.tab.receipt')}
            </Button>
          )}
          {canManage && (
            <Button variant="secondary" onClick={() => setBulkIssueOpen(true)}>
              {t('issuedItems.tab.bulkIssue')}
            </Button>
          )}
          {canManage && <Button onClick={openCreate}>{t('issuedItems.tab.add')}</Button>}
        </div>
      </div>

      {loadErrorKey && <p className="placeholder-text">{t(loadErrorKey)}</p>}
      {!loadErrorKey && items === null && <p className="placeholder-text">{t('issuedItems.tab.loading')}</p>}
      {!loadErrorKey && items !== null && items.length === 0 && (
        <p className="placeholder-text">{t('issuedItems.tab.empty')}</p>
      )}

      {!loadErrorKey && items !== null && items.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('issuedItems.tab.colItem')}</th>
              <th>{t('issuedItems.tab.colVariant')}</th>
              <th>{t('issuedItems.tab.colCategory')}</th>
              <th>{t('issuedItems.tab.colQty')}</th>
              <th>{t('issuedItems.tab.colSerial')}</th>
              <th>{t('issuedItems.tab.colIssued')}</th>
              <th>{t('issuedItems.tab.colReturned')}</th>
              <th>{t('issuedItems.tab.colIssuedBy')}</th>
              <th>{t('issuedItems.tab.colStatus')}</th>
              <th aria-label={t('issuedItems.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.name}</td>
                <td>{item.variantLabel ?? '—'}</td>
                <td>{item.category}</td>
                <td>{item.quantity}</td>
                <td>{item.serialNumber ?? '—'}</td>
                <td>{formatDate(item.issuedDate) || '—'}</td>
                <td>{formatDate(item.returnedDate) || '—'}</td>
                <td>{item.issuedByName ?? '—'}</td>
                <td>
                  <Badge tone={STATUS_TONE[item.status]}>{t(ISSUED_ITEM_STATUS_LABELS[item.status])}</Badge>
                </td>
                <td className="issued-items-row-actions">
                  {canManage && (
                    <button type="button" className="issued-items-link" onClick={() => openEdit(item)}>
                      {t('ui.actions.edit')}
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(item)}>
                      {t('ui.actions.delete')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <Modal
          title={editingId ? t('issuedItems.tab.editTitle') : t('issuedItems.tab.addTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="issued-item-form" disabled={saving}>
                {saving ? t('issuedItems.tab.saving') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="issued-item-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="issued-items-form-error" role="alert">
                {formError}
              </div>
            )}
            {!editingId && templates.length > 0 && (
              <FormField label={t('issuedItems.tab.fromTemplate')} htmlFor="ii-template" hint={t('issuedItems.tab.fromTemplateHint')}>
                <select id="ii-template" value={form.templateId ?? ''} onChange={(e) => applyTemplate(e.target.value)} disabled={saving}>
                  <option value="">{t('issuedItems.tab.customOption')}</option>
                  {templates.map((tpl) => (
                    <option key={tpl.id} value={tpl.id}>
                      {tpl.name} ({tpl.category})
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            {!editingId && selectedTemplate?.variantsEnabled && (
              <FormField label={t('issuedItems.tab.variantLabel')} htmlFor="ii-variant" required>
                <select id="ii-variant" value={form.variantId ?? ''} onChange={(e) => set('variantId', e.target.value || null)} disabled={saving}>
                  <option value="">{t('issuedItems.tab.chooseVariant')}</option>
                  {variants.map((variant) => (
                    <option key={variant.id} value={variant.id}>
                      {t('issuedItems.tab.variantOption', { label: variant.label, stock: variant.currentStock })}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            {editingId && form.variantId && (
              <p className="customer-form-muted">
                {t('issuedItems.tab.variantFixed', { label: items?.find((i) => i.id === editingId)?.variantLabel ?? '—' })}
              </p>
            )}
            {stockTracked && availableStock !== null && !editingId && (
              <p className={`issued-items-stock-preview${stockShortage ? ' issued-items-stock-preview-warning' : ''}`} role="status">
                {t('issuedItems.tab.availableStock', {
                  stock: `${availableStock}${selectedTemplate?.unit ? ` ${selectedTemplate.unit}` : ''}`,
                })}
                {stockShortage && ` ${t('issuedItems.tab.stockShortage')}`}
              </p>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('issuedItems.tab.itemName')} htmlFor="ii-name" required>
                <input id="ii-name" value={form.name ?? ''} onChange={(e) => set('name', e.target.value)} disabled={saving} maxLength={150} />
              </FormField>
              <FormField label={t('issuedItems.tab.category')} htmlFor="ii-cat" required>
                <input id="ii-cat" value={form.category ?? ''} onChange={(e) => set('category', e.target.value)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('issuedItems.tab.statusLabel')} htmlFor="ii-status" required>
                <select id="ii-status" value={form.status} onChange={(e) => set('status', e.target.value as IssuedItemStatus)} disabled={saving}>
                  {ISSUED_ITEM_STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {t(ISSUED_ITEM_STATUS_LABELS[s])}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={t('issuedItems.tab.quantity')} htmlFor="ii-qty">
                <input id="ii-qty" type="number" min={1} value={form.quantity} onChange={(e) => set('quantity', Number(e.target.value) || 1)} disabled={saving} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label={t('issuedItems.tab.issuedDate')} htmlFor="ii-date">
                <input id="ii-date" type="date" value={form.issuedDate ?? ''} onChange={(e) => set('issuedDate', e.target.value || null)} disabled={saving} />
              </FormField>
              <FormField label={t('issuedItems.tab.serialNumber')} htmlFor="ii-serial" required={selectedTemplate?.requiresSerialNumber && form.status === 'Issued'}>
                <input id="ii-serial" value={form.serialNumber ?? ''} onChange={(e) => set('serialNumber', e.target.value || null)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            {(form.status === 'Returned' || form.status === 'Damaged') && (
              <div className="issued-items-form-row">
                <FormField label={t('issuedItems.tab.returnedDate')} htmlFor="ii-retdate">
                  <input id="ii-retdate" type="date" value={form.returnedDate ?? ''} onChange={(e) => set('returnedDate', e.target.value || null)} disabled={saving} />
                </FormField>
                <FormField label={t('issuedItems.tab.returnCondition')} htmlFor="ii-retcond">
                  <input id="ii-retcond" value={form.returnCondition ?? ''} onChange={(e) => set('returnCondition', e.target.value || null)} disabled={saving} maxLength={150} />
                </FormField>
              </div>
            )}
            {form.status === 'Returned' && (
              <div className="issued-items-form-row">
                <FormField label={t('issuedItems.tab.dispositionLabel')} htmlFor="ii-disposition" hint={t('issuedItems.tab.dispositionHint')}>
                  <select
                    id="ii-disposition"
                    value={form.returnDisposition ?? 'good'}
                    onChange={(e) => set('returnDisposition', e.target.value as ReturnDisposition)}
                    disabled={saving}
                  >
                    {(Object.keys(RETURN_DISPOSITION_LABELS) as ReturnDisposition[]).map((disposition) => (
                      <option key={disposition} value={disposition}>
                        {t(RETURN_DISPOSITION_LABELS[disposition])}
                      </option>
                    ))}
                  </select>
                </FormField>
                {(form.returnDisposition ?? 'good') === 'good' && (
                  <label className="issued-items-checkbox">
                    <input
                      type="checkbox"
                      checked={form.restoreStock ?? true}
                      onChange={(e) => set('restoreStock', e.target.checked)}
                      disabled={saving}
                    />
                    <span>{t('issuedItems.tab.restoreStock')}</span>
                  </label>
                )}
              </div>
            )}
            <FormField label={t('issuedItems.tab.notes')} htmlFor="ii-notes">
              <textarea id="ii-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('issuedItems.tab.deleteTitle')}
          message={t('issuedItems.tab.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {negativeStock && (
        <NegativeStockConfirmModal
          payload={negativeStock.payload}
          kind="issue"
          employeeName={employeeName}
          storageLocation={selectedTemplate?.storageLocation}
          canConfirm={canOverrideStock}
          busy={saving}
          onConfirm={(reason) => void handleNegativeStockConfirm(reason)}
          onCancel={() => setNegativeStock(null)}
        />
      )}

      {bulkIssueOpen && (
        <BulkIssueModal
          employeeId={employeeId}
          employeeName={employeeName}
          templates={templates}
          canOverrideStock={canOverrideStock}
          onClose={() => setBulkIssueOpen(false)}
          onItemIssued={() => setReloadToken((token) => token + 1)}
          onCompleted={(message) => {
            showSuccess(message)
            setBulkIssueOpen(false)
            setReloadToken((token) => token + 1)
          }}
        />
      )}
    </section>
  )
}
