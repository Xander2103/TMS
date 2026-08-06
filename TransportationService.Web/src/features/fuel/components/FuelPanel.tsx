import { useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
import { searchTankCards } from '../../tank-cards/api/tankCardsApi'
import { maskCardNumber, type TankCard } from '../../tank-cards/types'
import { formatDate } from '../../../utils/dates'
import {
  createFuelTransaction,
  deleteFuelTransaction,
  getFuelOverview,
  updateFuelTransaction,
} from '../api/fuelApi'
import { FUEL_WARNING_LABELS, type FuelOverview, type FuelTransaction, type FuelTransactionInput } from '../types'
import './fuel.css'

interface FuelForm {
  driverId: string
  tankCardId: string
  transactionDate: string
  litres: string
  totalAmount: string
  odometerKm: string
  station: string
  fullTank: boolean
  notes: string
}

const EMPTY_FORM: FuelForm = {
  driverId: '',
  tankCardId: '',
  transactionDate: '',
  litres: '',
  totalAmount: '',
  odometerKm: '',
  station: '',
  fullTank: true,
  notes: '',
}

function euro(value: number): string {
  return value.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
}

interface FuelPanelProps {
  vehicleId: string
}

/** Fuel history for a vehicle: manual transactions, computed consumption and anomaly warnings. */
export function FuelPanel({ vehicleId }: FuelPanelProps) {
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [overview, setOverview] = useState<FuelOverview | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [drivers, setDrivers] = useState<DriverListItem[]>([])
  const [cards, setCards] = useState<TankCard[]>([])

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<FuelTransaction | null>(null)
  const [form, setForm] = useState<FuelForm>(EMPTY_FORM)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [deleteTarget, setDeleteTarget] = useState<FuelTransaction | null>(null)

  useEffect(() => {
    let mounted = true
    getFuelOverview(vehicleId)
      .then((data) => {
        if (!mounted) return
        setOverview(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Tankbeurten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [vehicleId, reloadToken])

  // Selector data for the editor; failures keep the selects usable as "Geen".
  useEffect(() => {
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setDrivers(data.items)
      })
      .catch(() => {})
    searchTankCards({ page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCards(data.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  function set<K extends keyof FuelForm>(key: K, value: FuelForm[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditing(null)
    setForm({ ...EMPTY_FORM, transactionDate: new Date().toISOString().slice(0, 10) })
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(transaction: FuelTransaction) {
    setEditing(transaction)
    setForm({
      driverId: transaction.driverId ?? '',
      tankCardId: transaction.tankCardId ?? '',
      transactionDate: transaction.transactionDate,
      litres: String(transaction.litres),
      totalAmount: String(transaction.totalAmount),
      odometerKm: transaction.odometerKm === null ? '' : String(transaction.odometerKm),
      station: transaction.station ?? '',
      fullTank: transaction.fullTank,
      notes: transaction.notes ?? '',
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.transactionDate) {
      setFormError('De datum is verplicht.')
      return
    }
    const litres = Number(form.litres)
    if (!form.litres || Number.isNaN(litres) || litres <= 0) {
      setFormError('Het aantal liter moet groter zijn dan nul.')
      return
    }
    const totalAmount = form.totalAmount === '' ? 0 : Number(form.totalAmount)
    if (Number.isNaN(totalAmount) || totalAmount < 0) {
      setFormError('Het bedrag kan niet negatief zijn.')
      return
    }
    const input: FuelTransactionInput = {
      driverId: form.driverId || null,
      tankCardId: form.tankCardId || null,
      transactionDate: form.transactionDate,
      litres,
      totalAmount,
      odometerKm: form.odometerKm === '' ? null : Number(form.odometerKm),
      station: form.station.trim() || null,
      fullTank: form.fullTank,
      notes: form.notes.trim() || null,
    }
    setSaving(true)
    try {
      if (editing) {
        await updateFuelTransaction(editing.id, input)
        showSuccess('Tankbeurt bijgewerkt.')
      } else {
        await createFuelTransaction(vehicleId, input)
        showSuccess('Tankbeurt geregistreerd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('De tankbeurt kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteFuelTransaction(deleteTarget.id)
      showSuccess('Tankbeurt verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('De tankbeurt kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  return (
    <section className="fuel">
      <div className="fuel-header">
        <h2>Brandstof</h2>
        {hasPermission('fuel.create') && (
          <Button variant="secondary" onClick={openCreate}>
            Tankbeurt registreren
          </Button>
        )}
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && overview === null && <p className="placeholder-text">Tankbeurten laden…</p>}
      {!loadError && overview !== null && overview.items.length === 0 && (
        <p className="placeholder-text">Nog geen tankbeurten geregistreerd.</p>
      )}

      {!loadError && overview !== null && overview.items.length > 0 && (
        <>
          <div className="fuel-summary">
            <span>
              Gem. verbruik:{' '}
              <strong>
                {overview.averageConsumptionLPer100Km !== null
                  ? `${overview.averageConsumptionLPer100Km.toLocaleString('nl-BE')} l/100km`
                  : '—'}
              </strong>
            </span>
            <span>
              Totaal getankt: <strong>{overview.totalLitres.toLocaleString('nl-BE')} l</strong>
            </span>
            <span>
              Totale kost: <strong>{euro(overview.totalAmount)}</strong>
            </span>
          </div>
          <table className="fuel-table">
            <thead>
              <tr>
                <th>Datum</th>
                <th>Liter</th>
                <th>Bedrag</th>
                <th>€/l</th>
                <th>Km-stand</th>
                <th>Verbruik</th>
                <th>Chauffeur</th>
                <th aria-label="Waarschuwingen" />
                <th aria-label="Acties" />
              </tr>
            </thead>
            <tbody>
              {overview.items.map((transaction) => (
                <tr key={transaction.id}>
                  <td>{transaction.transactionDate}</td>
                  <td>
                    {transaction.litres.toLocaleString('nl-BE')} l{!transaction.fullTank && <span className="fuel-partial"> (deels)</span>}
                  </td>
                  <td>{euro(transaction.totalAmount)}</td>
                  <td>{transaction.pricePerLitre !== null ? transaction.pricePerLitre.toLocaleString('nl-BE') : '—'}</td>
                  <td>{transaction.odometerKm !== null ? transaction.odometerKm.toLocaleString('nl-BE') : '—'}</td>
                  <td>
                    {transaction.consumptionLPer100Km !== null
                      ? `${transaction.consumptionLPer100Km.toLocaleString('nl-BE')} l/100km`
                      : '—'}
                  </td>
                  <td>{transaction.driverName ?? '—'}</td>
                  <td>
                    {transaction.warnings.length > 0 && (
                      <span title={transaction.warnings.map((w) => FUEL_WARNING_LABELS[w]).join('\n')}>
                        <Badge tone="warning">⚠ {transaction.warnings.length}</Badge>
                      </span>
                    )}
                  </td>
                  <td className="fuel-actions">
                    {hasPermission('fuel.edit') && (
                      <button type="button" className="fuel-link" onClick={() => openEdit(transaction)}>
                        Bewerken
                      </button>
                    )}
                    {hasPermission('fuel.delete') && (
                      <button type="button" className="fuel-link fuel-link-danger" onClick={() => setDeleteTarget(transaction)}>
                        Verwijderen
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      {editorOpen && (
        <Modal
          title={editing ? 'Tankbeurt bewerken' : 'Tankbeurt registreren'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="fuel-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="fuel-form" className="fuel-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="fuel-form-error" role="alert">
                {formError}
              </div>
            )}
            <div className="fuel-form-row">
              <FormField label="Datum" htmlFor="fu-date" required>
                <input
                  id="fu-date"
                  type="date"
                  value={form.transactionDate}
                  onChange={(e) => set('transactionDate', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Kilometerstand" htmlFor="fu-odo" hint="Nodig voor de verbruiksberekening">
                <input
                  id="fu-odo"
                  type="number"
                  min={0}
                  value={form.odometerKm}
                  onChange={(e) => set('odometerKm', e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <div className="fuel-form-row">
              <FormField label="Aantal liter" htmlFor="fu-litres" required>
                <input
                  id="fu-litres"
                  type="number"
                  min={0.01}
                  step="0.01"
                  value={form.litres}
                  onChange={(e) => set('litres', e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Totaalbedrag (€)" htmlFor="fu-amount">
                <input
                  id="fu-amount"
                  type="number"
                  min={0}
                  step="0.01"
                  value={form.totalAmount}
                  onChange={(e) => set('totalAmount', e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
            <label className="fuel-checkbox">
              <input
                type="checkbox"
                checked={form.fullTank}
                onChange={(e) => set('fullTank', e.target.checked)}
                disabled={saving}
              />
              Volledig volgetankt (nodig voor verbruiksberekening)
            </label>
            <div className="fuel-form-row">
              <FormField label="Chauffeur" htmlFor="fu-driver">
                <select id="fu-driver" value={form.driverId} onChange={(e) => set('driverId', e.target.value)} disabled={saving}>
                  <option value="">Geen</option>
                  {drivers.map((driver) => (
                    <option key={driver.id} value={driver.id}>
                      {driver.fullName} ({driver.driverNumber})
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Tankkaart" htmlFor="fu-card">
                <select id="fu-card" value={form.tankCardId} onChange={(e) => set('tankCardId', e.target.value)} disabled={saving}>
                  <option value="">Geen</option>
                  {cards.map((card) => (
                    <option key={card.id} value={card.id}>
                      {maskCardNumber(card.cardNumber)} ({card.provider})
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <FormField label="Tankstation" htmlFor="fu-station">
              <input
                id="fu-station"
                value={form.station}
                onChange={(e) => set('station', e.target.value)}
                disabled={saving}
                maxLength={200}
                placeholder="bv. Total Antwerpen"
              />
            </FormField>
            <FormField label="Notities" htmlFor="fu-notes">
              <textarea id="fu-notes" rows={2} value={form.notes} onChange={(e) => set('notes', e.target.value)} disabled={saving} />
            </FormField>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Tankbeurt verwijderen"
          message={`Weet je zeker dat je de tankbeurt van ${formatDate(deleteTarget.transactionDate)} (${deleteTarget.litres.toLocaleString('nl-BE')} l) wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </section>
  )
}
