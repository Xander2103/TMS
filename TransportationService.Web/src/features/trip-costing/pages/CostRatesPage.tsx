import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { euro } from '../../invoices/types'
import {
  createCostRateSet,
  deleteCostRateSet,
  listCostRateSets,
  updateCostRateSet,
} from '../api/tripCostingApi'
import type { CostRateSet, CostRateSetInput } from '../types'
import '../components/trip-costing.css'

interface RateField {
  key: keyof CostRateSetInput
  label: string
  hint?: string
  step?: string
}

const RATE_FIELDS: RateField[] = [
  { key: 'fuelPricePerLitre', label: 'Brandstofprijs (€/l)', step: '0.001' },
  { key: 'defaultConsumptionLPer100Km', label: 'Standaardverbruik (l/100km)', hint: 'Gebruikt als het voertuig geen normverbruik heeft.', step: '0.1' },
  { key: 'vehicleCostPerKm', label: 'Voertuigkost (€/km)', step: '0.01' },
  { key: 'vehicleCostPerHour', label: 'Voertuigkost (€/uur)', step: '0.01' },
  { key: 'driverCostPerHour', label: 'Chauffeursloon (€/uur)', step: '0.01' },
  { key: 'employerCostMultiplier', label: 'Werkgeverslastenfactor', hint: 'Bijv. 1,35 = loon + 35% lasten.', step: '0.01' },
  { key: 'maintenanceCostPerKm', label: 'Onderhoud (€/km)', step: '0.001' },
  { key: 'depreciationPerDay', label: 'Afschrijving (€/dag)', step: '0.01' },
  { key: 'trailerCostPerDay', label: 'Oplegger (€/dag)', step: '0.01' },
  { key: 'equipmentCostPerDay', label: 'Uitrusting kraan/koeling (€/dag)', step: '0.01' },
  { key: 'defaultTollPerTrip', label: 'Standaard tol per rit (€)', step: '0.01' },
  { key: 'overtimeThresholdMinutesPerDay', label: 'Overurengrens (min/dag)', hint: '480 = 8 uur.', step: '1' },
  { key: 'overtimeRateMultiplier', label: 'Overurenfactor', hint: 'Bijv. 1,5 = 150% van het uurloon.', step: '0.01' },
  { key: 'waitingTimeCostPerHour', label: 'Wachttijd (€/uur)', step: '0.01' },
  { key: 'co2KgPerLitreDiesel', label: 'CO₂ diesel (kg/l)', step: '0.001' },
  { key: 'co2KgPerLitreOther', label: 'CO₂ benzine/overig (kg/l)', step: '0.001' },
]

const EMPTY_INPUT: CostRateSetInput = {
  effectiveFrom: '',
  name: null,
  fuelPricePerLitre: 0,
  defaultConsumptionLPer100Km: 0,
  vehicleCostPerKm: 0,
  vehicleCostPerHour: 0,
  driverCostPerHour: 0,
  employerCostMultiplier: 1.35,
  maintenanceCostPerKm: 0,
  depreciationPerDay: 0,
  trailerCostPerDay: 0,
  equipmentCostPerDay: 0,
  defaultTollPerTrip: 0,
  overtimeThresholdMinutesPerDay: 480,
  overtimeRateMultiplier: 1.5,
  waitingTimeCostPerHour: 0,
  co2KgPerLitreDiesel: 2.68,
  co2KgPerLitreOther: 2.31,
}

/**
 * Effective-dated cost rate cards. Historic trips keep their snapshots: a new card only
 * affects calculations from its effective date onwards.
 */
export function CostRatesPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('trip_costs.manage')

  const [sets, setSets] = useState<CostRateSet[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<CostRateSet | null>(null)
  const [form, setForm] = useState<CostRateSetInput>(EMPTY_INPUT)
  const [busy, setBusy] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<CostRateSet | null>(null)

  useEffect(() => {
    let mounted = true
    listCostRateSets()
      .then((data) => {
        if (!mounted) return
        setSets(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De tarieven konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [reloadToken])

  function openCreate() {
    setEditing(null)
    setForm({ ...EMPTY_INPUT, effectiveFrom: new Date().toISOString().slice(0, 10) })
    setEditorOpen(true)
  }

  function openEdit(set: CostRateSet) {
    setEditing(set)
    const input: CostRateSetInput & { id?: string } = { ...set }
    delete input.id
    setForm(input)
    setEditorOpen(true)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!form.effectiveFrom) {
      showError('Een ingangsdatum is verplicht.')
      return
    }
    setBusy(true)
    try {
      if (editing) {
        await updateCostRateSet(editing.id, form)
        showSuccess('Tarievenset bijgewerkt.')
      } else {
        await createCostRateSet(form)
        showSuccess('Tarievenset aangemaakt.')
      }
      setEditorOpen(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De tarievenset kon niet worden opgeslagen.')
    } finally {
      setBusy(false)
    }
  }

  function setNumber(key: keyof CostRateSetInput, raw: string) {
    const value = Number(raw.replace(',', '.'))
    setForm((current) => ({ ...current, [key]: Number.isNaN(value) ? 0 : value }))
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Kostentarieven' }]} />
      <PageHeader
        title="Kostentarieven"
        subtitle="Tarievensets met ingangsdatum voeden de ritkostencalculatie. Historische ritten behouden hun berekende snapshots."
        action={canManage && <Button onClick={openCreate}>Nieuwe tarievenset</Button>}
      />

      {sets === null && <p className="placeholder-text">Tarieven laden…</p>}
      {sets !== null && sets.length === 0 && (
        <p className="placeholder-text">
          Nog geen tarievenset. Zonder tarieven worden er geen ritkosten berekend.
        </p>
      )}
      {sets !== null && sets.length > 0 && (
        <table className="tc-table">
          <thead>
            <tr>
              <th>Ingangsdatum</th>
              <th>Naam</th>
              <th className="tc-num">Brandstof €/l</th>
              <th className="tc-num">Chauffeur €/u</th>
              <th className="tc-num">Voertuig €/km</th>
              <th className="tc-num">Voertuig €/u</th>
              <th className="tc-num">Afschrijving €/dag</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {sets.map((set) => (
              <tr key={set.id}>
                <td>{set.effectiveFrom}</td>
                <td>{set.name ?? '—'}</td>
                <td className="tc-num">{set.fuelPricePerLitre.toLocaleString('nl-BE', { minimumFractionDigits: 3 })}</td>
                <td className="tc-num">{euro(set.driverCostPerHour)}</td>
                <td className="tc-num">{euro(set.vehicleCostPerKm)}</td>
                <td className="tc-num">{euro(set.vehicleCostPerHour)}</td>
                <td className="tc-num">{euro(set.depreciationPerDay)}</td>
                {canManage && (
                  <td className="tc-row-actions">
                    <button type="button" className="tc-link-button" onClick={() => openEdit(set)}>
                      Bewerken
                    </button>
                    <button type="button" className="tc-link-button tc-danger" onClick={() => setDeleteTarget(set)}>
                      Verwijderen
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <Modal
          title={editing ? `Tarievenset bewerken — vanaf ${editing.effectiveFrom}` : 'Nieuwe tarievenset'}
          onClose={() => setEditorOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="cr-form" disabled={busy}>
                {busy ? 'Bezig…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="cr-form" className="tc-form" onSubmit={submit} noValidate>
            <div className="tc-form-row">
              <FormField label="Ingangsdatum" htmlFor="cr-effective" required hint="Ritten vanaf deze datum gebruiken deze tarieven.">
                <input
                  id="cr-effective"
                  type="date"
                  value={form.effectiveFrom}
                  onChange={(e) => setForm((current) => ({ ...current, effectiveFrom: e.target.value }))}
                  disabled={busy}
                />
              </FormField>
              <FormField label="Naam" htmlFor="cr-name">
                <input
                  id="cr-name"
                  value={form.name ?? ''}
                  onChange={(e) => setForm((current) => ({ ...current, name: e.target.value || null }))}
                  maxLength={100}
                  disabled={busy}
                />
              </FormField>
            </div>
            <div className="tc-form-row">
              {RATE_FIELDS.map((field) => (
                <FormField key={field.key} label={field.label} htmlFor={`cr-${field.key}`} hint={field.hint}>
                  <input
                    id={`cr-${field.key}`}
                    type="number"
                    step={field.step}
                    min="0"
                    value={String(form[field.key] ?? 0)}
                    onChange={(e) => setNumber(field.key, e.target.value)}
                    disabled={busy}
                  />
                </FormField>
              ))}
            </div>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Tarievenset verwijderen"
          message={`Tarievenset vanaf ${deleteTarget.effectiveFrom} verwijderen? Reeds berekende ritkosten blijven ongewijzigd.`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteCostRateSet(deleteTarget.id)
              showSuccess('Tarievenset verwijderd.')
              setDeleteTarget(null)
              setReloadToken((token) => token + 1)
            } catch {
              showError('De tarievenset kon niet worden verwijderd.')
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
