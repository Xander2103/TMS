import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { SURCHARGE_KIND_LABELS, type SurchargeKind } from '../types'
import {
  createPricingZone,
  createServiceOption,
  deletePricingZone,
  deleteServiceOption,
  listPricingZones,
  listServiceOptions,
  listUnitTypeSettings,
  saveUnitTypeSettings,
  updatePricingZone,
  updateServiceOption,
  type PricingZone,
  type ServiceOption,
  type UnitTypeSettings,
} from '../api/pricingApi'

type TabId = 'zones' | 'diensten' | 'eenheden'

interface ZoneDraft {
  zone: PricingZone | null
  code: string
  name: string
  areas: { countryCode: string; from: string; to: string }[]
}

interface OptionDraft {
  option: ServiceOption | null
  code: string
  name: string
  kind: SurchargeKind
  defaultValue: string
  isActive: boolean
}

/**
 * Master data behind the pricing engine: delivery zones, delivery services/supplements and
 * which units may be used for order entry / price agreements. Customer-specific rules live
 * on the customer's "Tarieven & toeslagen" tab.
 */
export function PricingSettingsPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('tariffs.manage')

  const [tab, setTab] = useState<TabId>('zones')
  const [zones, setZones] = useState<PricingZone[]>([])
  const [options, setOptions] = useState<ServiceOption[]>([])
  const [units, setUnits] = useState<UnitTypeSettings[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)

  const [zoneDraft, setZoneDraft] = useState<ZoneDraft | null>(null)
  const [optionDraft, setOptionDraft] = useState<OptionDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteZone, setDeleteZone] = useState<PricingZone | null>(null)
  const [deleteOption, setDeleteOption] = useState<ServiceOption | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    Promise.all([listPricingZones(), listServiceOptions(true), listUnitTypeSettings()])
      .then(([zoneData, optionData, unitData]) => {
        setZones(zoneData)
        setOptions(optionData)
        setUnits(unitData)
        setLoadError(null)
      })
      .catch(() => setLoadError('De prijsinstellingen konden niet worden geladen.'))
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitZone(event: FormEvent) {
    event.preventDefault()
    if (!zoneDraft) return
    setBusy(true)
    try {
      const input = {
        code: zoneDraft.code.trim(),
        name: zoneDraft.name.trim(),
        isActive: true,
        sortOrder: zoneDraft.zone?.sortOrder ?? zones.length,
        areas: zoneDraft.areas
          .filter((a) => a.from.trim() && a.to.trim())
          .map((a) => ({ countryCode: a.countryCode.trim() || 'BE', postalCodeFrom: a.from.trim(), postalCodeTo: a.to.trim() })),
      }
      if (zoneDraft.zone) {
        await updatePricingZone(zoneDraft.zone.id, input)
        showSuccess('Zone bijgewerkt.')
      } else {
        await createPricingZone(input)
        showSuccess('Zone toegevoegd.')
      }
      setZoneDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De zone kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitOption(event: FormEvent) {
    event.preventDefault()
    if (!optionDraft) return
    setBusy(true)
    try {
      const input = {
        code: optionDraft.code.trim(),
        name: optionDraft.name.trim(),
        kind: optionDraft.kind,
        defaultValue: Number(optionDraft.defaultValue) || 0,
        isActive: optionDraft.isActive,
        sortOrder: optionDraft.option?.sortOrder ?? options.length,
      }
      if (optionDraft.option) {
        await updateServiceOption(optionDraft.option.id, input)
        showSuccess('Dienst bijgewerkt.')
      } else {
        await createServiceOption(input)
        showSuccess('Dienst toegevoegd.')
      }
      setOptionDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De dienst kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function toggleUnitFlag(unit: UnitTypeSettings, flag: 'allowForOrderEntry' | 'allowForPricing') {
    try {
      const saved = await saveUnitTypeSettings(unit.id, {
        allowForOrderEntry: flag === 'allowForOrderEntry' ? !unit.allowForOrderEntry : unit.allowForOrderEntry,
        allowForPricing: flag === 'allowForPricing' ? !unit.allowForPricing : unit.allowForPricing,
      })
      setUnits((rows) => rows.map((row) => (row.id === saved.id ? saved : row)))
    } catch (err) {
      showError(describeApiError(err, 'De instelling kon niet worden opgeslagen.').message)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Prijsinstellingen' }]} />
      <PageHeader
        title="Prijsinstellingen"
        subtitle="Zones, diensten/toeslagen en eenheden voor de automatische prijsberekening. Klantspecifieke prijsregels beheer je op de klantfiche."
      />
      <Tabs
        tabs={[
          { id: 'zones', label: 'Zones', badge: zones.length || undefined },
          { id: 'diensten', label: 'Diensten & toeslagen', badge: options.length || undefined },
          { id: 'eenheden', label: 'Eenheden' },
        ]}
        activeId={tab}
        onChange={(next) => setTab(next as TabId)}
      />

      {tab === 'zones' && (
        <TabPanel tabId="zones">
          {canManage && (
            <div className="tof-documents-toolbar">
              <Button onClick={() => { setDraftError(null); setZoneDraft({ zone: null, code: '', name: '', areas: [{ countryCode: 'BE', from: '', to: '' }] }) }}>
                + Zone
              </Button>
            </div>
          )}
          {zones.length === 0 && <p className="placeholder-text">Nog geen zones. Zones koppelen postcodereeksen aan een tariefgebied.</p>}
          {zones.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>Code</th>
                  <th>Naam</th>
                  <th>Postcodereeksen</th>
                  {canManage && <th aria-label="Acties" />}
                </tr>
              </thead>
              <tbody>
                {zones.map((zone) => (
                  <tr key={zone.id}>
                    <td>{zone.code}</td>
                    <td>{zone.name}</td>
                    <td>{zone.areas.map((a) => `${a.countryCode} ${a.postalCodeFrom}–${a.postalCodeTo}`).join(', ') || '—'}</td>
                    {canManage && (
                      <td className="issued-items-row-actions">
                        <button
                          type="button"
                          className="issued-items-link"
                          onClick={() => {
                            setDraftError(null)
                            setZoneDraft({
                              zone,
                              code: zone.code,
                              name: zone.name,
                              areas: zone.areas.map((a) => ({ countryCode: a.countryCode, from: a.postalCodeFrom, to: a.postalCodeTo })),
                            })
                          }}
                        >
                          Bewerken
                        </button>
                        <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteZone(zone)}>
                          Verwijderen
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TabPanel>
      )}

      {tab === 'diensten' && (
        <TabPanel tabId="diensten">
          {canManage && (
            <div className="tof-documents-toolbar">
              <Button onClick={() => { setDraftError(null); setOptionDraft({ option: null, code: '', name: '', kind: 'Fixed', defaultValue: '0', isActive: true }) }}>
                + Dienst
              </Button>
            </div>
          )}
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Naam</th>
                <th>Soort</th>
                <th>Standaardprijs</th>
                <th>Status</th>
                {canManage && <th aria-label="Acties" />}
              </tr>
            </thead>
            <tbody>
              {options.map((option) => (
                <tr key={option.id}>
                  <td>{option.name}</td>
                  <td>{SURCHARGE_KIND_LABELS[option.kind]}</td>
                  <td>{option.kind === 'Percent' ? `${option.defaultValue}%` : `€ ${option.defaultValue.toFixed(2)}`}</td>
                  <td>
                    <Badge tone={option.isActive ? 'success' : 'neutral'}>{option.isActive ? 'Actief' : 'Inactief'}</Badge>
                  </td>
                  {canManage && (
                    <td className="issued-items-row-actions">
                      <button
                        type="button"
                        className="issued-items-link"
                        onClick={() => {
                          setDraftError(null)
                          setOptionDraft({
                            option,
                            code: option.code,
                            name: option.name,
                            kind: option.kind,
                            defaultValue: String(option.defaultValue),
                            isActive: option.isActive,
                          })
                        }}
                      >
                        Bewerken
                      </button>
                      <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteOption(option)}>
                        Verwijderen
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </TabPanel>
      )}

      {tab === 'eenheden' && (
        <TabPanel tabId="eenheden">
          <p className="customer-form-muted">
            Bepaal per eenheid of ze kiesbaar is bij orderinvoer en of er prijsafspraken op gemaakt kunnen worden.
          </p>
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Eenheid</th>
                <th>Code</th>
                <th>Orderinvoer</th>
                <th>Prijsafspraken</th>
              </tr>
            </thead>
            <tbody>
              {units.map((unit) => (
                <tr key={unit.id}>
                  <td>{unit.name}</td>
                  <td>{unit.code}</td>
                  <td>
                    <input
                      aria-label={`${unit.name} beschikbaar bij orderinvoer`}
                      type="checkbox"
                      checked={unit.allowForOrderEntry}
                      onChange={() => void toggleUnitFlag(unit, 'allowForOrderEntry')}
                      disabled={!canManage}
                    />
                  </td>
                  <td>
                    <input
                      aria-label={`${unit.name} beschikbaar voor prijsafspraken`}
                      type="checkbox"
                      checked={unit.allowForPricing}
                      onChange={() => void toggleUnitFlag(unit, 'allowForPricing')}
                      disabled={!canManage}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </TabPanel>
      )}

      {zoneDraft && (
        <Modal
          title={zoneDraft.zone ? `Zone bewerken — ${zoneDraft.zone.code}` : 'Zone toevoegen'}
          onClose={() => setZoneDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setZoneDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="zone-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="zone-form" className="issued-items-form" onSubmit={submitZone} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Code" htmlFor="zone-code" required hint="bv. Z1">
                <input id="zone-code" value={zoneDraft.code} onChange={(e) => setZoneDraft((d) => (d ? { ...d, code: e.target.value } : d))} maxLength={30} />
              </FormField>
              <FormField label="Naam" htmlFor="zone-name" required>
                <input id="zone-name" value={zoneDraft.name} onChange={(e) => setZoneDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={150} />
              </FormField>
            </div>
            <fieldset className="issued-items-generate-dimension">
              <legend>Postcodereeksen</legend>
              {zoneDraft.areas.map((area, index) => (
                <div key={index} className="issued-items-form-row customer-rule-bracket">
                  <input aria-label={`Reeks ${index + 1} land`} placeholder="BE" maxLength={2} value={area.countryCode}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, countryCode: e.target.value.toUpperCase() } : a)) } : d))} />
                  <input aria-label={`Reeks ${index + 1} van`} placeholder="van (bv. 3000)" value={area.from}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, from: e.target.value } : a)) } : d))} />
                  <input aria-label={`Reeks ${index + 1} tot`} placeholder="tot (bv. 3999)" value={area.to}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, to: e.target.value } : a)) } : d))} />
                  <Button variant="ghost" onClick={() => setZoneDraft((d) => (d ? { ...d, areas: d.areas.filter((_, i) => i !== index) } : d))}>
                    Verwijderen
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={() => setZoneDraft((d) => (d ? { ...d, areas: [...d.areas, { countryCode: 'BE', from: '', to: '' }] } : d))}>
                + Reeks
              </Button>
            </fieldset>
          </form>
        </Modal>
      )}

      {optionDraft && (
        <Modal
          title={optionDraft.option ? `Dienst bewerken — ${optionDraft.option.name}` : 'Dienst toevoegen'}
          onClose={() => setOptionDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOptionDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="option-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="option-form" className="issued-items-form" onSubmit={submitOption} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Code" htmlFor="opt-code" required hint="bv. VOOR8">
                <input id="opt-code" value={optionDraft.code} onChange={(e) => setOptionDraft((d) => (d ? { ...d, code: e.target.value } : d))} maxLength={50} />
              </FormField>
              <FormField label="Naam" htmlFor="opt-name" required>
                <input id="opt-name" value={optionDraft.name} onChange={(e) => setOptionDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={200} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Soort" htmlFor="opt-kind">
                <select id="opt-kind" value={optionDraft.kind} onChange={(e) => setOptionDraft((d) => (d ? { ...d, kind: e.target.value as SurchargeKind } : d))}>
                  {Object.entries(SURCHARGE_KIND_LABELS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label={optionDraft.kind === 'Percent' ? 'Standaard (%)' : 'Standaardprijs (€)'} htmlFor="opt-value">
                <input id="opt-value" type="number" step="0.01" value={optionDraft.defaultValue} onChange={(e) => setOptionDraft((d) => (d ? { ...d, defaultValue: e.target.value } : d))} />
              </FormField>
            </div>
            <label className="tof-checkbox">
              <input type="checkbox" checked={optionDraft.isActive} onChange={(e) => setOptionDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              Actief
            </label>
          </form>
        </Modal>
      )}

      {deleteZone && (
        <ConfirmDialog
          title="Zone verwijderen"
          message={`Weet je zeker dat je zone "${deleteZone.code}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={async () => {
            const target = deleteZone
            setDeleteZone(null)
            try {
              await deletePricingZone(target.id)
              showSuccess('Zone verwijderd.')
              reload()
            } catch (err) {
              showError(describeApiError(err, 'De zone kon niet worden verwijderd.').message)
            }
          }}
          onCancel={() => setDeleteZone(null)}
        />
      )}

      {deleteOption && (
        <ConfirmDialog
          title="Dienst verwijderen"
          message={`Weet je zeker dat je "${deleteOption.name}" wilt verwijderen? Bestaande orders behouden hun snapshot.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={async () => {
            const target = deleteOption
            setDeleteOption(null)
            try {
              await deleteServiceOption(target.id)
              showSuccess('Dienst verwijderd.')
              reload()
            } catch (err) {
              showError(describeApiError(err, 'De dienst kon niet worden verwijderd.').message)
            }
          }}
          onCancel={() => setDeleteOption(null)}
        />
      )}
    </div>
  )
}
