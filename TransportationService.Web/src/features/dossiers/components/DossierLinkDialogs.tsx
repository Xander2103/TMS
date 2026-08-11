import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import { addDossierRelation, linkDossierOrder, listDossiers } from '../api/dossiersApi'
import { DOSSIER_RELATION_LABELS, type DossierDetail, type DossierRelationType } from '../types'

interface DialogProps {
  dossier: DossierDetail
  onClose: () => void
  onUpdated: (dossier: DossierDetail) => void
}

/** Compat: bestaande opdracht handmatig aan het dossier koppelen (Meer ▾). */
export function LinkOrderDialog({ dossier, onClose, onUpdated }: DialogProps) {
  const [options, setOptions] = useState<SearchableSelectOption[]>([])
  const [orderId, setOrderId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    searchTransportOrders({ page: 1, pageSize: 100, customerId: dossier.customerId ?? undefined })
      .then((result) => {
        const linked = new Set(dossier.orders.map((o) => o.orderId))
        setOptions(
          result.items
            .filter((o) => !linked.has(o.id))
            .map((o) => ({ value: o.id, label: o.orderNumber, description: o.customerName })),
        )
      })
      .catch(() => setOptions([]))
  }, [dossier])

  async function submit() {
    if (!orderId) return
    setBusy(true)
    setError(null)
    try {
      onUpdated(await linkDossierOrder(dossier.id, orderId))
      onClose()
    } catch (err) {
      setError(describeApiError(err, 'De opdracht kon niet gekoppeld worden.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Opdracht koppelen"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button disabled={busy || !orderId} onClick={() => void submit()}>
            Koppelen
          </Button>
        </>
      }
    >
      <ValidationSummary message={error} />
      <FormField label="Transportopdracht" htmlFor="link-order" required>
        <SearchableSelect
          id="link-order"
          value={orderId}
          onChange={setOrderId}
          options={options}
          emptyMessage="Geen (verdere) opdrachten gevonden"
        />
      </FormField>
      {dossier.customerId && <p className="placeholder-text">Gefilterd op de klant van dit dossier.</p>}
    </Modal>
  )
}

/** Compat: dossierrelatie toevoegen (Meer ▾). */
export function AddRelationDialog({ dossier, onClose, onUpdated }: DialogProps) {
  const [options, setOptions] = useState<SearchableSelectOption[]>([])
  const [target, setTarget] = useState<string | null>(null)
  const [relationType, setRelationType] = useState<DossierRelationType>('FollowUp')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    listDossiers()
      .then((all) =>
        setOptions(
          all.filter((d) => d.id !== dossier.id).map((d) => ({ value: d.id, label: d.dossierNumber, description: d.title })),
        ),
      )
      .catch(() => setOptions([]))
  }, [dossier])

  async function submit() {
    if (!target) return
    setBusy(true)
    setError(null)
    try {
      onUpdated(await addDossierRelation(dossier.id, { targetDossierId: target, relationType, notes: notes || null }))
      onClose()
    } catch (err) {
      setError(describeApiError(err, 'De relatie kon niet toegevoegd worden.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Dossierrelatie toevoegen"
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button disabled={busy || !target} onClick={() => void submit()}>
            Toevoegen
          </Button>
        </>
      }
    >
      <ValidationSummary message={error} />
      <FormField label="Dossier" htmlFor="relation-target" required>
        <SearchableSelect id="relation-target" value={target} onChange={setTarget} options={options} />
      </FormField>
      <FormField label="Relatietype" htmlFor="relation-type" required>
        <select
          id="relation-type"
          value={relationType}
          onChange={(event) => setRelationType(event.target.value as DossierRelationType)}
        >
          {Object.entries(DOSSIER_RELATION_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </FormField>
      <FormField label="Notitie" htmlFor="relation-notes">
        <input id="relation-notes" value={notes} onChange={(event) => setNotes(event.target.value)} maxLength={1000} />
      </FormField>
    </Modal>
  )
}
