import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  DIMENSION_BEHAVIOR_LABELS,
  UNIT_CATEGORY_LABELS,
  createUnitTypeMaster,
  listUnitTypeMaster,
  updateUnitTypeMaster,
  type UnitCategory,
  type UnitDimensionBehavior,
  type UnitTypeMaster,
} from '../api/pricingApi'
import { suggestUnitCode } from '../unitCodeSuggestion'

interface UnitDraft {
  unit: UnitTypeMaster | null
  code: string
  codeTouched: boolean
  name: string
  category: UnitCategory
  decimals: string
  symbol: string
  dimensionBehavior: UnitDimensionBehavior
  defaultLengthCm: string
  defaultWidthCm: string
  defaultHeightCm: string
  defaultWeightKg: string
  maxWeightKg: string
  defaultVolumeM3: string
  defaultLoadingMeters: string
  defaultPalletPlaces: string
  allowForOrderEntry: boolean
  allowForPricing: boolean
  isActive: boolean
  sortOrder: string
}

const dims = (unit: UnitTypeMaster): string => {
  if (unit.defaultLengthCm === null && unit.defaultWidthCm === null && unit.defaultHeightCm === null) return '—'
  const part = (v: number | null) => (v === null ? '?' : `${v}`)
  const base = `${part(unit.defaultLengthCm)} × ${part(unit.defaultWidthCm)}`
  return unit.defaultHeightCm === null ? `${base} cm` : `${base} × ${unit.defaultHeightCm} cm`
}

/**
 * Stamgegevens → Eenheden: full unit master data incl. physical defaults. Units are
 * configuration, never code — admins add company-specific units here without development.
 */
export function UnitTypeMasterEditor() {
  const { hasPermission } = useAuth()
  const { showSuccess } = useToast()
  const canView =
    hasPermission('unit_types.view') || hasPermission('unit_types.manage')
    || hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('unit_types.manage') || hasPermission('tariffs.manage')

  const [units, setUnits] = useState<UnitTypeMaster[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<UnitDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listUnitTypeMaster()
      .then((data) => {
        setUnits(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De eenheden konden niet worden geladen.'))
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">Je hebt geen rechten om eenheden te bekijken.</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (units === null) return <p className="placeholder-text">Eenheden laden…</p>

  function openDraft(unit: UnitTypeMaster | null) {
    setDraftError(null)
    setDraft(
      unit
        ? {
            unit,
            code: unit.code,
            codeTouched: true, // never re-suggest for an existing unit
            name: unit.name,
            category: unit.category,
            decimals: String(unit.decimals),
            symbol: unit.symbol ?? '',
            dimensionBehavior: unit.dimensionBehavior,
            defaultLengthCm: unit.defaultLengthCm !== null ? String(unit.defaultLengthCm) : '',
            defaultWidthCm: unit.defaultWidthCm !== null ? String(unit.defaultWidthCm) : '',
            defaultHeightCm: unit.defaultHeightCm !== null ? String(unit.defaultHeightCm) : '',
            defaultWeightKg: unit.defaultWeightKg !== null ? String(unit.defaultWeightKg) : '',
            maxWeightKg: unit.maxWeightKg !== null ? String(unit.maxWeightKg) : '',
            defaultVolumeM3: unit.defaultVolumeM3 !== null ? String(unit.defaultVolumeM3) : '',
            defaultLoadingMeters: unit.defaultLoadingMeters !== null ? String(unit.defaultLoadingMeters) : '',
            defaultPalletPlaces: unit.defaultPalletPlaces !== null ? String(unit.defaultPalletPlaces) : '',
            allowForOrderEntry: unit.allowForOrderEntry,
            allowForPricing: unit.allowForPricing,
            isActive: unit.isActive,
            sortOrder: String(unit.sortOrder),
          }
        : {
            unit: null,
            code: '',
            codeTouched: false,
            name: '',
            category: 'Packaging',
            decimals: '0',
            symbol: '',
            dimensionBehavior: 'Variable',
            defaultLengthCm: '',
            defaultWidthCm: '',
            defaultHeightCm: '',
            defaultWeightKg: '',
            maxWeightKg: '',
            defaultVolumeM3: '',
            defaultLoadingMeters: '',
            defaultPalletPlaces: '',
            allowForOrderEntry: true,
            allowForPricing: true,
            isActive: true,
            sortOrder: String((units ?? []).length),
          },
    )
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    const num = (raw: string) => (raw.trim() === '' ? null : Number(raw))
    setBusy(true)
    try {
      const input = {
        code: draft.code.trim().toUpperCase(),
        name: draft.name.trim(),
        description: null,
        isActive: draft.isActive,
        sortOrder: Number(draft.sortOrder) || 0,
        allowForOrderEntry: draft.allowForOrderEntry,
        allowForPricing: draft.allowForPricing,
        category: draft.category,
        decimals: Number(draft.decimals) || 0,
        symbol: draft.symbol.trim() || null,
        dimensionBehavior: draft.dimensionBehavior,
        defaultLengthCm: num(draft.defaultLengthCm),
        defaultWidthCm: num(draft.defaultWidthCm),
        defaultHeightCm: num(draft.defaultHeightCm),
        defaultWeightKg: num(draft.defaultWeightKg),
        maxWeightKg: num(draft.maxWeightKg),
        defaultVolumeM3: num(draft.defaultVolumeM3),
        defaultLoadingMeters: num(draft.defaultLoadingMeters),
        defaultPalletPlaces: num(draft.defaultPalletPlaces),
      }
      if (draft.unit) {
        await updateUnitTypeMaster(draft.unit.id, input)
        showSuccess('Eenheid bijgewerkt.')
      } else {
        await createUnitTypeMaster(input)
        showSuccess('Eenheid toegevoegd.')
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De eenheid kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section>
      <div className="customer-panel-header">
        <h3>Eenheden</h3>
        {canManage && <Button onClick={() => openDraft(null)}>+ Eenheid</Button>}
      </div>
      <p className="customer-form-muted">
        Eenheden zijn volledig configureerbaar: code, categorie, afmetingsgedrag en fysieke standaardwaarden komen uit
        deze stamgegevens — nooit uit vaste waardes in de software.
      </p>
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>Code</th>
            <th>Naam</th>
            <th>Categorie</th>
            <th>Afmetingen</th>
            <th>Gedrag</th>
            <th>Order</th>
            <th>Tarief</th>
            <th>Actief</th>
            {canManage && <th aria-label="Acties" />}
          </tr>
        </thead>
        <tbody>
          {units.map((unit) => (
            <tr key={unit.id}>
              <td>{unit.code}</td>
              <td>
                {unit.name}
                {unit.symbol && <span className="customer-form-muted"> ({unit.symbol})</span>}
              </td>
              <td>{UNIT_CATEGORY_LABELS[unit.category]}</td>
              <td>{dims(unit)}</td>
              <td>{DIMENSION_BEHAVIOR_LABELS[unit.dimensionBehavior]}</td>
              <td>{unit.allowForOrderEntry ? 'Ja' : 'Nee'}</td>
              <td>{unit.allowForPricing ? 'Ja' : 'Nee'}</td>
              <td>{unit.isActive ? 'Ja' : <Badge tone="neutral">Inactief</Badge>}</td>
              {canManage && (
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openDraft(unit)}>
                    Bewerken
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {draft && (
        <Modal
          title={draft.unit ? `Eenheid bewerken — ${draft.unit.name}` : 'Eenheid toevoegen'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="unit-master-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="unit-master-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Naam" htmlFor="um-name" required>
                <input
                  id="um-name"
                  value={draft.name}
                  maxLength={150}
                  onChange={(e) =>
                    setDraft((d) =>
                      d
                        ? {
                            ...d,
                            name: e.target.value,
                            // Suggestion only while creating and while the code was not touched.
                            code: !d.unit && !d.codeTouched ? suggestUnitCode(e.target.value) : d.code,
                          }
                        : d,
                    )
                  }
                />
              </FormField>
              <FormField label="Code" htmlFor="um-code" required hint="Voorstel is aanpasbaar (bv. eigen boekhoud- of EDI-code).">
                <input
                  id="um-code"
                  value={draft.code}
                  maxLength={20}
                  onChange={(e) => setDraft((d) => (d ? { ...d, code: e.target.value.toUpperCase(), codeTouched: true } : d))}
                />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Categorie" htmlFor="um-category">
                <select id="um-category" value={draft.category} onChange={(e) => setDraft((d) => (d ? { ...d, category: e.target.value as UnitCategory } : d))}>
                  {Object.entries(UNIT_CATEGORY_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Symbool" htmlFor="um-symbol" hint="Bv. kg, ldm.">
                <input id="um-symbol" value={draft.symbol} maxLength={20} onChange={(e) => setDraft((d) => (d ? { ...d, symbol: e.target.value } : d))} />
              </FormField>
              <FormField label="Decimalen" htmlFor="um-decimals">
                <input id="um-decimals" type="number" min={0} max={4} value={draft.decimals} onChange={(e) => setDraft((d) => (d ? { ...d, decimals: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Afmetingsgedrag" htmlFor="um-behavior" hint="Vast = niet aanpasbaar per order; standaard = vooraf ingevuld.">
                <select
                  id="um-behavior"
                  value={draft.dimensionBehavior}
                  onChange={(e) => setDraft((d) => (d ? { ...d, dimensionBehavior: e.target.value as UnitDimensionBehavior } : d))}
                >
                  {Object.entries(DIMENSION_BEHAVIOR_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Sorteervolgorde" htmlFor="um-sort">
                <input id="um-sort" type="number" value={draft.sortOrder} onChange={(e) => setDraft((d) => (d ? { ...d, sortOrder: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Lengte (cm)" htmlFor="um-length">
                <input id="um-length" type="number" step="0.1" value={draft.defaultLengthCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultLengthCm: e.target.value } : d))} />
              </FormField>
              <FormField label="Breedte (cm)" htmlFor="um-width">
                <input id="um-width" type="number" step="0.1" value={draft.defaultWidthCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultWidthCm: e.target.value } : d))} />
              </FormField>
              <FormField label="Hoogte (cm)" htmlFor="um-height">
                <input id="um-height" type="number" step="0.1" value={draft.defaultHeightCm} onChange={(e) => setDraft((d) => (d ? { ...d, defaultHeightCm: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Standaardgewicht (kg)" htmlFor="um-weight">
                <input id="um-weight" type="number" step="0.1" value={draft.defaultWeightKg} onChange={(e) => setDraft((d) => (d ? { ...d, defaultWeightKg: e.target.value } : d))} />
              </FormField>
              <FormField label="Max. gewicht (kg)" htmlFor="um-maxweight">
                <input id="um-maxweight" type="number" step="0.1" value={draft.maxWeightKg} onChange={(e) => setDraft((d) => (d ? { ...d, maxWeightKg: e.target.value } : d))} />
              </FormField>
              <FormField label="Volume (m³)" htmlFor="um-volume">
                <input id="um-volume" type="number" step="0.001" value={draft.defaultVolumeM3} onChange={(e) => setDraft((d) => (d ? { ...d, defaultVolumeM3: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Laadmeters" htmlFor="um-ldm">
                <input id="um-ldm" type="number" step="0.01" value={draft.defaultLoadingMeters} onChange={(e) => setDraft((d) => (d ? { ...d, defaultLoadingMeters: e.target.value } : d))} />
              </FormField>
              <FormField label="Palletplaatsen" htmlFor="um-places">
                <input id="um-places" type="number" step="0.5" value={draft.defaultPalletPlaces} onChange={(e) => setDraft((d) => (d ? { ...d, defaultPalletPlaces: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.allowForOrderEntry} onChange={(e) => setDraft((d) => (d ? { ...d, allowForOrderEntry: e.target.checked } : d))} />
                Bruikbaar bij orderinvoer
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.allowForPricing} onChange={(e) => setDraft((d) => (d ? { ...d, allowForPricing: e.target.checked } : d))} />
                Bruikbaar als tariefeenheid
              </label>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
                Actief
              </label>
            </div>
          </form>
        </Modal>
      )}
    </section>
  )
}
