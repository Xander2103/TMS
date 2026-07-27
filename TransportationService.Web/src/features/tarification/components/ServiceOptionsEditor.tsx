import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { SURCHARGE_KIND_LABELS, type SurchargeKind } from '../types'
import { formatServiceValue } from '../serviceValueFormat'
import {
  createServiceOption,
  deleteServiceOption,
  listServiceOptions,
  listUnitTypeSettings,
  updateServiceOption,
  type ServiceOption,
  type UnitTypeSettings,
} from '../api/pricingApi'
import { listWarehouses } from '../../warehousing/api/warehousingApi'
import type { Warehouse } from '../../warehousing/types'

interface OptionDraft {
  option: ServiceOption | null
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: string
  description: string
  invoiceDescription: string
  selectableInOrders: boolean
  isActive: boolean
  unitTypeId: string
  autoApply: boolean
  onlyForAdr: boolean
  warehouseIds: string[]
}

const VALUE_LABEL_BY_KIND: Partial<Record<SurchargeKind, string>> = {
  Percent: 'Standaard (%)',
  PerHour: 'Standaardprijs per uur (€)',
  PerStop: 'Standaardprijs per stop (€)',
  PerUnit: 'Standaardprijs per eenheid (€)',
  PerOrderLine: 'Standaardprijs per orderlijn (€)',
  PerKg: 'Standaardprijs per kg (€)',
  PerM3: 'Standaardprijs per m³ (€)',
  PerLdm: 'Standaardprijs per laadmeter (€)',
  PerDay: 'Standaardprijs per dag (€)',
  PerPalletDay: 'Standaardprijs per pallet/dag (€)',
}

/**
 * Global admin configuration of services & surcharges (spec §5/6): the defaults every
 * transport order starts from. Customer-specific overrides live on the customer's
 * "Tarieven & toeslagen" tab; nothing here is hardcoded in the order form.
 */
export function ServiceOptionsEditor() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [options, setOptions] = useState<ServiceOption[] | null>(null)
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<OptionDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ServiceOption | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listServiceOptions(true)
      .then((data) => {
        setOptions(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De diensten konden niet worden geladen.'))
    listUnitTypeSettings()
      .then(setUnits)
      .catch(() => {})
    // Warehouses feed the optional warehouse condition; unavailable (no permission) is fine.
    listWarehouses()
      .then(setWarehouses)
      .catch(() => {})
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">Je hebt geen rechten om diensten te bekijken.</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (options === null) return <p className="placeholder-text">Diensten laden…</p>

  function openDraft(option: ServiceOption | null) {
    setDraftError(null)
    setDraft(
      option
        ? {
            option,
            code: option.code,
            name: option.name,
            kind: option.kind,
            defaultValue: String(option.defaultValue),
            description: option.description ?? '',
            invoiceDescription: option.invoiceDescription ?? '',
            selectableInOrders: option.selectableInOrders,
            isActive: option.isActive,
            unitTypeId: option.unitTypeId ?? '',
            autoApply: option.autoApply,
            onlyForAdr: option.onlyForAdr,
            warehouseIds: option.warehouseIds ?? [],
          }
        : {
            option: null,
            code: '',
            name: '',
            kind: 'Fixed',
            defaultValue: '0',
            description: '',
            invoiceDescription: '',
            selectableInOrders: true,
            isActive: true,
            unitTypeId: '',
            autoApply: false,
            onlyForAdr: false,
            warehouseIds: [],
          },
    )
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    setBusy(true)
    try {
      const input = {
        code: draft.code.trim(),
        name: draft.name.trim(),
        kind: draft.kind,
        defaultValue: Number(draft.defaultValue) || 0,
        isActive: draft.isActive,
        sortOrder: draft.option?.sortOrder ?? options?.length ?? 0,
        description: draft.description.trim() || null,
        invoiceDescription: draft.invoiceDescription.trim() || null,
        selectableInOrders: draft.selectableInOrders,
        unitTypeId: draft.kind === 'PerUnit' ? draft.unitTypeId || null : null,
        autoApply: draft.autoApply,
        onlyForAdr: draft.onlyForAdr,
        warehouseIds: draft.warehouseIds,
      }
      if (draft.option) {
        await updateServiceOption(draft.option.id, input)
        showSuccess('Dienst bijgewerkt.')
      } else {
        await createServiceOption(input)
        showSuccess('Dienst toegevoegd.')
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De dienst kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  const valueLabel = draft ? (VALUE_LABEL_BY_KIND[draft.kind] ?? 'Standaardprijs (€)') : 'Standaardprijs (€)'
  const activeUnits = units.filter((u) => u.isActive)

  return (
    <div>
      {canManage && (
        <div className="tof-documents-toolbar">
          <Button onClick={() => openDraft(null)}>+ Dienst</Button>
        </div>
      )}
      <table className="issued-items-table">
        <thead>
          <tr>
            <th>Naam</th>
            <th>Berekeningswijze</th>
            <th>Standaardprijs</th>
            <th>In orders</th>
            <th>Status</th>
            {canManage && <th aria-label="Acties" />}
          </tr>
        </thead>
        <tbody>
          {options.map((option) => (
            <tr key={option.id}>
              <td>
                {option.name}
                {option.description && <div className="customer-form-muted">{option.description}</div>}
              </td>
              <td>{SURCHARGE_KIND_LABELS[option.kind]}</td>
              <td>
                {formatServiceValue(option.kind, option.defaultValue, option.unitTypeName)}
                {option.autoApply && <Badge tone="info">Automatisch</Badge>}
                {option.onlyForAdr && <Badge tone="warning">Alleen bij ADR</Badge>}
                {(option.warehouseNames?.length ?? 0) > 0 && (
                  <Badge tone="warning">Magazijn: {option.warehouseNames!.join(', ')}</Badge>
                )}
              </td>
              <td>{option.selectableInOrders ? 'Ja' : 'Nee'}</td>
              <td>
                <Badge tone={option.isActive ? 'success' : 'neutral'}>{option.isActive ? 'Actief' : 'Inactief'}</Badge>
              </td>
              {canManage && (
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openDraft(option)}>
                    Bewerken
                  </button>
                  <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(option)}>
                    Verwijderen
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {draft && (
        <Modal
          title={draft.option ? `Dienst bewerken — ${draft.option.name}` : 'Dienst toevoegen'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="service-option-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="service-option-form" className="issued-items-form" onSubmit={submit} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Code" htmlFor="opt-code" required hint="bv. VOOR10">
                <input id="opt-code" value={draft.code} onChange={(e) => setDraft((d) => (d ? { ...d, code: e.target.value } : d))} maxLength={50} />
              </FormField>
              <FormField label="Naam" htmlFor="opt-name" required>
                <input id="opt-name" value={draft.name} onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Berekeningswijze" htmlFor="opt-kind" hint="Vast bedrag, percentage, per uur/stop, per eenheid/orderlijn, per kg/m³/laadmeter of per dag/pallet-dag.">
                <select id="opt-kind" value={draft.kind} onChange={(e) => setDraft((d) => (d ? { ...d, kind: e.target.value as SurchargeKind } : d))}>
                  {Object.entries(SURCHARGE_KIND_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={valueLabel} htmlFor="opt-value">
                <input id="opt-value" type="number" step="0.01" value={draft.defaultValue} onChange={(e) => setDraft((d) => (d ? { ...d, defaultValue: e.target.value } : d))} />
              </FormField>
            </div>
            {draft.kind === 'PerUnit' && (
              <FormField label="Eenheid" htmlFor="opt-unit" required hint="De eenheid waarvan het aantal op de order deze service telt (bv. Colli, Pallet).">
                <select id="opt-unit" value={draft.unitTypeId} onChange={(e) => setDraft((d) => (d ? { ...d, unitTypeId: e.target.value } : d))}>
                  <option value="">— Kies eenheid —</option>
                  {activeUnits.map((unit) => (
                    <option key={unit.id} value={unit.id}>
                      {unit.name}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            <FormField label="Omschrijving (intern)" htmlFor="opt-desc">
              <input id="opt-desc" value={draft.description} onChange={(e) => setDraft((d) => (d ? { ...d, description: e.target.value } : d))} maxLength={1000} />
            </FormField>
            <FormField label="Factuuromschrijving" htmlFor="opt-invoice" hint="Leeg = de naam van de dienst.">
              <input id="opt-invoice" value={draft.invoiceDescription} onChange={(e) => setDraft((d) => (d ? { ...d, invoiceDescription: e.target.value } : d))} maxLength={300} />
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.selectableInOrders} onChange={(e) => setDraft((d) => (d ? { ...d, selectableInOrders: e.target.checked } : d))} />
              Selecteerbaar in transportorders
            </label>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.autoApply} onChange={(e) => setDraft((d) => (d ? { ...d, autoApply: e.target.checked } : d))} />
              Automatisch toepassen (contractdienst, geen selectie nodig)
            </label>
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.onlyForAdr} onChange={(e) => setDraft((d) => (d ? { ...d, onlyForAdr: e.target.checked } : d))} />
              Alleen bij ADR
            </label>
            {warehouses.length > 0 && (
              <FormField
                label="Alleen voor magazijnen"
                hint="Niets aangevinkt = alle orders. Meerdere magazijnen: één ervan volstaat (of); samen met 'Alleen bij ADR' moeten beide voorwaarden gelden (en)."
              >
                <div>
                  {warehouses.filter((w) => w.isActive || draft.warehouseIds.includes(w.id)).map((warehouse) => (
                    <label key={warehouse.id} className="tof-checkbox">
                      <input
                        type="checkbox"
                        checked={draft.warehouseIds.includes(warehouse.id)}
                        onChange={(e) =>
                          setDraft((d) =>
                            d
                              ? {
                                  ...d,
                                  warehouseIds: e.target.checked
                                    ? [...d.warehouseIds, warehouse.id]
                                    : d.warehouseIds.filter((id) => id !== warehouse.id),
                                }
                              : d,
                          )
                        }
                      />
                      {warehouse.name}
                    </label>
                  ))}
                </div>
              </FormField>
            )}
            <label className="tof-checkbox">
              <input type="checkbox" checked={draft.isActive} onChange={(e) => setDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              Actief
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Dienst verwijderen"
          message={`Weet je zeker dat je "${deleteTarget.name}" wilt verwijderen? Bestaande orders behouden hun snapshot.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await deleteServiceOption(target.id)
              showSuccess('Dienst verwijderd.')
              reload()
            } catch (err) {
              showError(describeApiError(err, 'De dienst kon niet worden verwijderd.').message)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
