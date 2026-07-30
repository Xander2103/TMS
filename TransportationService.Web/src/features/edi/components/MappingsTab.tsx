import { useEffect, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
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
      showSuccess('Klantkoppeling bijgewerkt.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De klantkoppeling kon niet worden bijgewerkt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function addMapping(event: FormEvent) {
    event.preventDefault()
    if (!selected || !newCode.trim() || !newLocationId) {
      showError('Externe code en locatie zijn verplicht.')
      return
    }
    setBusy(true)
    try {
      await addLocationMapping(selected.id, { externalLocationCode: newCode.trim(), locationId: newLocationId })
      showSuccess('Locatiemapping toegevoegd.')
      setNewCode('')
      setNewLocationId(null)
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De locatiemapping kon niet worden toegevoegd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function removeMapping() {
    if (!selected || !deleteTarget) return
    setBusy(true)
    try {
      await deleteLocationMapping(selected.id, deleteTarget.id)
      showSuccess('Locatiemapping verwijderd.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(describeApiError(err, 'De locatiemapping kon niet worden verwijderd.').message)
    } finally {
      setBusy(false)
      setDeleteTarget(null)
    }
  }

  const columns: Column<EdiPartnerLocationMapping>[] = [
    { key: 'code', header: 'Externe code', render: (m) => <code>{m.externalLocationCode}</code> },
    { key: 'location', header: 'Locatie', render: (m) => m.locationName },
    {
      key: 'actions',
      header: 'Acties',
      render: (m) => (
        <Button variant="ghost" onClick={() => setDeleteTarget(m)}>
          Verwijderen
        </Button>
      ),
    },
  ]

  return (
    <div>
      <FormField label="Handelspartner" htmlFor="edi-mappings-partner">
        <select id="edi-mappings-partner" value={selectedId} onChange={(e) => setSelectedId(e.target.value)}>
          {partners.length === 0 && <option value="">Geen partners</option>}
          {partners.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} ({p.code})
            </option>
          ))}
        </select>
      </FormField>

      {selected && (
        <>
          <FormField label="Klantkoppeling" htmlFor="edi-mappings-customer" hint="Onze klant waarvoor deze partner orders aanlevert.">
            <SearchableSelect
              id="edi-mappings-customer"
              value={selected.customerId}
              onChange={(value) => void changeCustomer(value)}
              options={customers}
              placeholder="— Geen klant gekoppeld —"
              disabled={busy}
              ariaLabel="Klantkoppeling"
            />
          </FormField>

          <h3>Locatiemappings</h3>
          <DataTable
            columns={columns}
            rows={selected.locations}
            rowKey={(m) => m.id}
            emptyMessage="Nog geen locatiemappings voor deze partner."
          />

          <form className="edi-mapping-form" onSubmit={addMapping} noValidate>
            <FormField label="Externe locatiecode" htmlFor="edi-mapping-code">
              <input id="edi-mapping-code" value={newCode} maxLength={100} onChange={(e) => setNewCode(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label="Locatie" htmlFor="edi-mapping-location">
              <SearchableSelect
                id="edi-mapping-location"
                value={newLocationId}
                onChange={setNewLocationId}
                options={locationOptions}
                placeholder="— Selecteer locatie —"
                disabled={busy}
                ariaLabel="Locatie"
              />
            </FormField>
            <Button type="submit" variant="secondary" disabled={busy}>
              + Mapping toevoegen
            </Button>
          </form>
        </>
      )}

      <section className="edi-section">
        <h3>Openstaande mappingproblemen</h3>
        {queue.length === 0 && <p className="placeholder-text">Geen openstaande mappingproblemen.</p>}
        {queue.length > 0 && (
          <ul className="edi-mapping-queue">
            {queue.map((m) => (
              <li key={m.id}>
                <code>{m.partnerCode}</code> — {m.externalReference ?? '(geen referentie)'} —{' '}
                <span title={m.errorDetail ?? undefined}>{m.errorDetail}</span>{' '}
                <Button variant="ghost" onClick={() => setDetailId(m.id)}>
                  Bekijken
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {deleteTarget && (
        <ConfirmDialog
          title="Locatiemapping verwijderen"
          message={`Mapping voor '${deleteTarget.externalLocationCode}' verwijderen?`}
          confirmLabel="Verwijderen"
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
            setReloadToken((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}
