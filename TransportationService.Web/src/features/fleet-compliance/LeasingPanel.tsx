import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { useToast } from '../../components/ui/toastContext'
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
  return amount.toLocaleString('nl-BE', { style: 'currency', currency: currency || 'EUR' })
}

/** Leasing contracts for a vehicle or trailer. Financial amounts are gated on fleet_finance. */
export function LeasingPanel({ ownerType, ownerId }: { ownerType: LeasingOwnerType; ownerId: string }) {
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
        if (mounted) setLoadError('Leasingcontracten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [ownerType, ownerId, reloadToken])

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
      setFormError('De leasingmaatschappij is verplicht.')
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateLeasingContract(editingId, form)
        showSuccess('Leasingcontract bijgewerkt.')
      } else {
        await createLeasingContract(ownerType, ownerId, form)
        showSuccess('Leasingcontract toegevoegd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('Het contract kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteLeasingContract(deleteTarget.id)
      showSuccess('Leasingcontract verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het contract kon niet worden verwijderd.')
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
      showSuccess('Contract geüpload.')
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het bestand kon niet worden geüpload (max. 10 MB, pdf/jpg/png).')
    } finally {
      setUploadingId(null)
    }
  }

  return (
    <section className="fleet-compliance">
      <div className="fleet-compliance-header">
        <h2>Leasing</h2>
        {canManage && (
          <Button variant="secondary" onClick={openCreate}>
            Contract toevoegen
          </Button>
        )}
      </div>

      {!canViewFinance && (
        <p className="fleet-compliance-note">Bedragen zijn verborgen — je mist het recht om financiële gegevens te bekijken.</p>
      )}

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && items === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && items !== null && items.length === 0 && (
        <p className="placeholder-text">Nog geen leasingcontracten geregistreerd.</p>
      )}

      {!loadError && items !== null && items.length > 0 && (
        <table className="fleet-compliance-table">
          <thead>
            <tr>
              <th>Maatschappij</th>
              <th>Contractnr.</th>
              <th>Looptijd</th>
              <th>Maandbedrag</th>
              <th>Status</th>
              <th>Bestand</th>
              <th aria-label="Acties" />
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
                  <Badge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Actief' : 'Beëindigd'}</Badge>
                </td>
                <td className="fleet-compliance-file">
                  {item.hasAttachment ? (
                    <button type="button" className="fleet-compliance-link" onClick={() => downloadLeasingFile(item.id, item.fileName ?? 'contract').catch(() => showError('Downloaden mislukt.'))}>
                      Downloaden
                    </button>
                  ) : (
                    '—'
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => pickFile(item.id)} disabled={uploadingId === item.id}>
                      {uploadingId === item.id ? 'Bezig…' : item.hasAttachment ? 'Vervangen' : 'Uploaden'}
                    </button>
                  )}
                </td>
                <td className="fleet-compliance-actions">
                  {canManage && (
                    <button type="button" className="fleet-compliance-link" onClick={() => openEdit(item)}>
                      Bewerken
                    </button>
                  )}
                  {canManage && (
                    <button type="button" className="fleet-compliance-link fleet-compliance-link-danger" onClick={() => setDeleteTarget(item)}>
                      Verwijderen
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
          title={editingId ? 'Leasingcontract bewerken' : 'Leasingcontract toevoegen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="leasing-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
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
            <FormField label="Leasingmaatschappij" htmlFor="lf-company" required>
              <input id="lf-company" value={form.leasingCompany} onChange={(e) => set('leasingCompany', e.target.value)} disabled={saving} maxLength={150} />
            </FormField>
            <FormField label="Contractnummer" htmlFor="lf-number">
              <input id="lf-number" value={form.contractNumber ?? ''} onChange={(e) => set('contractNumber', e.target.value || null)} disabled={saving} maxLength={100} />
            </FormField>
            <div className="fleet-compliance-form-row">
              <FormField label="Startdatum" htmlFor="lf-start">
                <input id="lf-start" type="date" value={form.startDate ?? ''} onChange={(e) => set('startDate', e.target.value || null)} disabled={saving} />
              </FormField>
              <FormField label="Einddatum" htmlFor="lf-end">
                <input id="lf-end" type="date" value={form.endDate ?? ''} onChange={(e) => set('endDate', e.target.value || null)} disabled={saving} />
              </FormField>
            </div>
            {canViewFinance && (
              <>
                <div className="fleet-compliance-form-row">
                  <FormField label="Maandbedrag" htmlFor="lf-amount">
                    <input id="lf-amount" type="number" min={0} step="0.01" value={form.monthlyAmount ?? ''} onChange={(e) => set('monthlyAmount', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                  <FormField label="Munt" htmlFor="lf-currency">
                    <input id="lf-currency" value={form.currency ?? 'EUR'} onChange={(e) => set('currency', e.target.value || null)} disabled={saving} maxLength={3} />
                  </FormField>
                </div>
                <div className="fleet-compliance-form-row">
                  <FormField label="Km/jaar toegestaan" htmlFor="lf-km">
                    <input id="lf-km" type="number" min={0} value={form.kilometerAllowancePerYear ?? ''} onChange={(e) => set('kilometerAllowancePerYear', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                  <FormField label="Km bij einde contract" htmlFor="lf-endkm">
                    <input id="lf-endkm" type="number" min={0} value={form.endOfContractMileageKm ?? ''} onChange={(e) => set('endOfContractMileageKm', e.target.value === '' ? null : Number(e.target.value))} disabled={saving} />
                  </FormField>
                </div>
              </>
            )}
            <FormField label="Contactpersoon" htmlFor="lf-contact">
              <input id="lf-contact" value={form.contactPerson ?? ''} onChange={(e) => set('contactPerson', e.target.value || null)} disabled={saving} maxLength={150} />
            </FormField>
            <FormField label="Notities" htmlFor="lf-notes">
              <textarea id="lf-notes" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value || null)} disabled={saving} />
            </FormField>
            <label className="fleet-compliance-checkbox">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
              <span>Contract is actief</span>
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Contract verwijderen"
          message={`Weet je zeker dat je het leasingcontract van "${deleteTarget.leasingCompany}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      <input ref={fileInputRef} type="file" accept={ACCEPT} className="fleet-compliance-file-input" onChange={handleFileSelected} />
    </section>
  )
}
