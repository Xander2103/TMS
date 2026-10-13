import { useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { getLocationOptions } from '../../locations/api/locationsApi'
import { searchCustomers } from '../../customers/api/customersApi'
import {
  addLocationMapping,
  deleteLocationMapping,
  listMessages,
  listPartners,
  updatePartner,
  type EdiMessageRow,
  type EdiPartner,
  type EdiPartnerLocationMapping,
} from '../api/ediApi'
import { MessageDetailModal } from './MessageDetailModal'

/** "Mappings" tab: klantkoppeling + locatiemappings per partner, and the unresolved-mapping
 * queue (Failed/DeadLettered messages flagged mappingIssue) linking straight to the message. */
export function MappingsTab() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [partners, setPartners] = useState<EdiPartner[]>([])
  const [selectedId, setSelectedId] = useState<string>('')
  const [customers, setCustomers] = useState<SearchableSelectOption[]>([])
  const [locationOptions, setLocationOptions] = useState<SearchableSelectOption[]>([])
  const [reloadToken, setReloadToken] = useState(0)
  const [deleteTarget, setDeleteTarget] = useState<EdiPartnerLocationMapping | null>(null)
  const [newCode, setNewCode] = useState('')
  const [newLocationId, setNewLocationId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [queue, setQueue] = useState<EdiMessageRow[]>([])
  const [detailId, setDetailId] = useState<string | null>(null)

  useEffect(() => {
    listPartners().then((data) => {
      setPartners(data)
      if (!selectedId && data.length > 0) setSelectedId(data[0].id)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadToken])

  useEffect(() => {
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => setCustomers(data.items.map((c) => ({ value: c.id, label: `${c.name} (${c.customerNumber})` }))))
      .catch(() => {})
    getLocationOptions()
      .then((data) => setLocationOptions(data.map((l) => ({ value: l.id, label: `${l.name} (${l.code})` }))))
      .catch(() => {})
  }, [])

  useEffect(() => {
    listMessages({ mappingIssues: true, page: 1, pageSize: 20 })
      .then((data) => setQueue(data.items))
      .catch(() => {})
  }, [reloadToken])

  const selected = partners.find((p) => p.id === selectedId) ?? null

  async function changeCustomer(customerId: string | null) {
    if (!selected) return
    setBusy(true)
    try {
      await updatePartner(selected.id, {
        name: selected.name,
        customerId,
        externalCustomerIdentifier: selected.externalCustomerIdentifier,
        mappingProfile: selected.mappingProfile,
        isActive: selected.isActive,
        notes: selected.notes,
      })
      showSuccess(t('edi.mappings.customerUpdated'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.mappings.customerUpdateFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function addMapping(event: FormEvent) {
    event.preventDefault()
    if (!selected || !newCode.trim() || !newLocationId) {
      showError(t('edi.mappings.validation'))
      return
    }
    setBusy(true)
    try {
      await addLocationMapping(selected.id, { externalLocationCode: newCode.trim(), locationId: newLocationId })
      showSuccess(t('edi.mappings.added'))
      setNewCode('')
      setNewLocationId(null)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.mappings.addFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function removeMapping() {
    if (!selected || !deleteTarget) return
    setBusy(true)
    try {
      await deleteLocationMapping(selected.id, deleteTarget.id)
      showSuccess(t('edi.mappings.removed'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.mappings.removeFailed')))
    } finally {
      setBusy(false)
      setDeleteTarget(null)
    }
  }

  const columns: Column<EdiPartnerLocationMapping>[] = [
    { key: 'code', header: t('edi.mappings.codeHeader'), render: (m) => <code>{m.externalLocationCode}</code> },
    { key: 'location', header: t('edi.mappings.locationHeader'), render: (m) => m.locationName },
    {
      key: 'actions',
      header: t('edi.mappings.actionsHeader'),
      render: (m) => (
        <Button variant="ghost" onClick={() => setDeleteTarget(m)}>
          {t('edi.mappings.remove')}
        </Button>
      ),
    },
  ]

  return (
    <div>
      <FormField label={t('edi.mappings.partnerLabel')} htmlFor="edi-mappings-partner">
        <select id="edi-mappings-partner" value={selectedId} onChange={(e) => setSelectedId(e.target.value)}>
          {partners.length === 0 && <option value="">{t('edi.mappings.noPartners')}</option>}
          {partners.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} ({p.code})
            </option>
          ))}
        </select>
      </FormField>

      {selected && (
        <>
          <FormField label={t('edi.mappings.customerLabel')} htmlFor="edi-mappings-customer" hint={t('edi.mappings.customerHint')}>
            <SearchableSelect
              id="edi-mappings-customer"
              value={selected.customerId}
              onChange={(value) => void changeCustomer(value)}
              options={customers}
              placeholder={t('edi.mappings.customerPlaceholder')}
              disabled={busy}
              ariaLabel={t('edi.mappings.customerLabel')}
            />
          </FormField>

          <h3>{t('edi.mappings.locationsTitle')}</h3>
          <DataTable
            columns={columns}
            rows={selected.locations}
            rowKey={(m) => m.id}
            emptyMessage={t('edi.mappings.empty')}
          />

          <form className="edi-mapping-form" onSubmit={addMapping} noValidate>
            <FormField label={t('edi.mappings.externalCodeLabel')} htmlFor="edi-mapping-code">
              <input id="edi-mapping-code" value={newCode} maxLength={100} onChange={(e) => setNewCode(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label={t('edi.mappings.locationLabel')} htmlFor="edi-mapping-location">
              <SearchableSelect
                id="edi-mapping-location"
                value={newLocationId}
                onChange={setNewLocationId}
                options={locationOptions}
                placeholder={t('edi.mappings.locationPlaceholder')}
                disabled={busy}
                ariaLabel={t('edi.mappings.locationLabel')}
              />
            </FormField>
            <Button type="submit" variant="secondary" disabled={busy}>
              {t('edi.mappings.add')}
            </Button>
          </form>
        </>
      )}

      <section className="edi-section">
        <h3>{t('edi.mappings.queueTitle')}</h3>
        {queue.length === 0 && <p className="placeholder-text">{t('edi.mappings.queueEmpty')}</p>}
        {queue.length > 0 && (
          <ul className="edi-mapping-queue">
            {queue.map((m) => (
              <li key={m.id}>
                <code>{m.partnerCode}</code> — {m.externalReference ?? t('edi.mappings.noReference')} —{' '}
                <span title={m.errorDetail ?? undefined}>{m.errorDetail}</span>{' '}
                <Button variant="ghost" onClick={() => setDetailId(m.id)}>
                  {t('edi.mappings.view')}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {deleteTarget && (
        <ConfirmDialog
          title={t('edi.mappings.deleteTitle')}
          message={t('edi.mappings.deleteMessage', { code: deleteTarget.externalLocationCode })}
          confirmLabel={t('edi.mappings.deleteConfirm')}
          destructive
          busy={busy}
          onConfirm={() => void removeMapping()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {detailId && (
        <MessageDetailModal
          id={detailId}
          // Reachable only via the canManage-gated "Mappings" tab; edi.manage always covers replay.
          canRetry

          onClose={() => setDetailId(null)}
          onReplayed={() => {
            setDetailId(null)
            setReloadToken((token) => token + 1)
          }}
        />
      )}
    </div>
  )
}
