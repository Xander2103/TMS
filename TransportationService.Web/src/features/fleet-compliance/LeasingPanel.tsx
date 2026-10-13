import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
import { useLocale } from '../../i18n/localeContext'
import { formatCurrency } from '../../utils/numbers'
import { useAuth } from '../auth/authContextValue'
import {
  createLeasingContract,
  deleteLeasingContract,
  downloadLeasingFile,
  listLeasingContracts,
  updateLeasingContract,
  uploadLeasingFile,
  type LeasingContract,
  type LeasingContractInput,
  type LeasingOwnerType,
} from './leasingApi'
import './fleet-compliance.css'

const ACCEPT = '.pdf,.jpg,.jpeg,.png'

function emptyForm(): LeasingContractInput {
  return {
    leasingCompany: '',
    contractNumber: null,
    startDate: null,
    endDate: null,
    monthlyAmount: null,
    currency: 'EUR',
    kilometerAllowancePerYear: null,
    endOfContractMileageKm: null,
    contactPerson: null,
    notes: null,
    isActive: true,
  }
}

function money(amount: number | null, currency: string): string {
  if (amount === null) return '—'
  return formatCurrency(amount, !currency || currency === 'EUR' ? '€' : currency)
}

/** Leasing contracts for a vehicle or trailer. Financial amounts are gated on fleet_finance. */
export function LeasingPanel({ ownerType, ownerId }: { ownerType: LeasingOwnerType; ownerId: string }) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('fleet_finance.manage')
  const canViewFinance = hasPermission('fleet_finance.view')

  const [items, setItems] = useState<LeasingContract[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<LeasingContractInput>(emptyForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<LeasingContract | null>(null)

  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const [uploadTargetId, setUploadTargetId] = useState<string | null>(null)
  const [uploadingId, setUploadingId] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listLeasingContracts(ownerType, ownerId)
      .then((data) => {
        if (!mounted) return
        setItems(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('fleet.leasing.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken, t])

  function set<K extends keyof LeasingContractInput>(key: K, value: LeasingContractInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm())
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(item: LeasingContract) {
    setEditingId(item.id)
    setForm({
      leasingCompany: item.leasingCompany,
      contractNumber: item.contractNumber,
      startDate: item.startDate,
      endDate: item.endDate,
      monthlyAmount: item.monthlyAmount,
      currency: item.currency,
      kilometerAllowancePerYear: item.kilometerAllowancePerYear,
      endOfContractMileageKm: item.endOfContractMileageKm,
      contactPerson: item.contactPerson,
      notes: item.notes,
      isActive: item.isActive,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.leasingCompany.trim()) {
      setFormError(t('fleet.leasing.companyRequired'))
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateLeasingContract(editingId, form)
        showSuccess(t('fleet.leasing.updated'))
      } else {
        await createLeasingContract(ownerType, ownerId, form)
        showSuccess(t('fleet.leasing.created'))
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch {
      setFormError(t('fleet.leasing.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteLeasingContract(deleteTarget.id)
      showSuccess(t('fleet.leasing.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('fleet.leasing.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  function pickFile(id: string) {
    setUploadTargetId(id)
    fileInputRef.current?.click()
  }

  async function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file || !uploadTargetId) return
    const id = uploadTargetId
    setUploadTargetId(null)
    setUploadingId(id)
    try {
      await uploadLeasingFile(id, file)
      showSuccess(t('fleet.leasing.uploaded'))
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('fleet.leasing.uploadFailed'))
    } finally {
      setUploadingId(null)
    }
  }

  return (
    <section className="fleet-compliance">
      <div className="fleet-compliance-header">
        <h2>{t('fleet.leasing.title')}</h2>
        {canManage && (
          <Button variant="secondary" onClick={openCreate}>
            {t('fleet.leasing.add')}
          </Button>
        )}
      </div>

      {!canViewFinance && (
        <p className="fleet-compliance-note">{t('fleet.leasing.financeHidden')}</p>
      )}

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && items === null && <p className="placeholder-text">{t('fleet.common.loading')}</p>}
      {!loadError && items !== null && items.length === 0 && (
        <p className="placeholder-text">{t('fleet.leasing.empty')}</p>
      )}

      {!loadError && items !== null && items.length > 0 && (
        <table className="fleet-compliance-table">
          <thead>
            <tr>
              <th>{t('fleet.leasing.colCompany')}</th>
              <th>{t('fleet.leasing.colNumber')}</th>
              <th>{t('fleet.leasing.colTerm')}</th>
              <th>{t('fleet.leasing.colMonthly')}</th>
              <th>{t('fleet.leasing.colStatus')}</th>
              <th>{t('fleet.leasing.colFile')}</th>
              <th aria-label={t('fleet.common.actions')} />
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.leasingCompany}</td>
                <td>{item.contractNumber ?? '—'}</td>
                <td>
                  {item.startDate ?? '…'} — {item.endDate ?? '…'}
                </td>
                <td>{canViewFinance ? money(item.monthlyAmount, item.currency) : '•••'}</td>
                <td>
                  <Badge tone={item.isActive ? 'success' : 'neutral'}>
                    {item.isActive ? t('ui.statusBadges.active') : t('fleet.leasing.ended')}
                  </Badge>
                </td>
                <td className="fleet-compliance-file">
                  {item.hasAttachment ? (
                    <button type="button" className="fleet-compliance-link" onClick={() => downloadLeasingFile(item.id, item.fileName ?? 'contract').catch(() => showError(t('fleet.common.downloadFailed')))}>
                      {t('fleet.common.download')}
                    </button>
                  ) : (
                    '—'
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => pickFile(item.id)} disabled={uploadingId === item.id}>
                      {uploadingId === item.id ? t('fleet.common.busy') : item.hasAttachment ? t('fleet.common.replace') : t('fleet.common.upload')}
                    </button>
                  )}
                </td>
                <td className="fleet-compliance-actions">
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => openEdit(item)}>
                      {t('ui.actions.edit')}
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link fleet-compliance-link-danger" onClick={() => setDeleteTarget(item)}>
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
          title={editingId ? t('fleet.leasing.editTitle') : t('fleet.leasing.addTitle')}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="leasing-form" disabled={saving}>
                {saving ? t('fleet.common.saving') : t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="leasing-form" className="fleet-compliance-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="fleet-compliance-form-error" role="alert">
                {formError}
              </div>
            )}
            <FormField label={t('fleet.leasing.company')} htmlFor="lf-company" required>
              <input id="lf-company" value={form.leasingCompany} onChange={(e) => set('leasingCompany', e.target.value)} disabled={saving} maxLength={150} />
            </FormField>
            <FormField label={t('fleet.leasing.contractNumber')} htmlFor="lf-number">
              <input id="lf-number" value={form.contractNumber ?? ''} onChange={(e) => set('contractNumber', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <div className="fleet-compliance-form-row">
              <FormField label={t('fleet.leasing.startDate')} htmlFor="lf-start">
                <input id="lf-start" type="date" value={form.startDate ?? ''} onChange={(e) => set('startDate', e.target.value || null)} disabled={saving} />
              </FormField>
              <FormField label={t('fleet.leasing.endDate')} htmlFor="lf-end">
                <input id="lf-end" type="date" value={form.endDate ?? ''} onChange={(e) => set('endDate', e.target.value || null)} disabled={saving} />
              </FormField>
            </div>
            {canViewFinance && (
              <>
                <div className="fleet-compliance-form-row">
                  <FormField label={t('fleet.leasing.monthlyAmount')} htmlFor="lf-amount">
                    <input id="lf-amount" type="number" min={0} step="0.01" value={form.monthlyAmount ?? ''} onChange={(e) => set('monthlyAmount', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                  <FormField label={t('fleet.leasing.currency')} htmlFor="lf-currency">
                    <input id="lf-currency" value={form.currency ?? 'EUR'} onChange={(e) => set('currency', e.target.value || null)} disabled={saving} maxLength={3} />
                  </FormField>
                </div>
                <div className="fleet-compliance-form-row">
                  <FormField label={t('fleet.leasing.kmPerYear')} htmlFor="lf-km">
                    <input id="lf-km" type="number" min={0} value={form.kilometerAllowancePerYear ?? ''} onChange={(e) => set('kilometerAllowancePerYear', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                  <FormField label={t('fleet.leasing.kmAtEnd')} htmlFor="lf-endkm">
                    <input id="lf-endkm" type="number" min={0} value={form.endOfContractMileageKm ?? ''} onChange={(e) => set('endOfContractMileageKm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                </div>
              </>
            )}
            <FormField label={t('fleet.leasing.contactPerson')} htmlFor="lf-contact">
              <input id="lf-contact" value={form.contactPerson ?? ''} onChange={(e) => set('contactPerson', e.target.value || null)} disabled={saving} maxLength={150} />
            </FormField>
            <FormField label={t('fleet.leasing.notes')} htmlFor="lf-notes">
              <textarea id="lf-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
            <label className="fleet-compliance-checkbox">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
              <span>{t('fleet.leasing.isActive')}</span>
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('fleet.leasing.deleteTitle')}
          message={t('fleet.leasing.deleteMessage', { company: deleteTarget.leasingCompany })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      <input ref={fileInputRef} type="file" accept={ACCEPT} className="fleet-compliance-file-input" onChange={handleFileSelected} />
    </section>
  )
}
