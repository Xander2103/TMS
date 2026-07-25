import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  createPricingZone,
  deletePricingZone,
  listPricingZones,
  updatePricingZone,
  type PricingZone,
} from '../api/pricingApi'
import { ServiceOptionsEditor } from '../components/ServiceOptionsEditor'
import { UnitTypeMasterEditor } from '../components/UnitTypeMasterEditor'

type TabId = 'zones' | 'diensten' | 'eenheden'

interface ZoneDraft {
  zone: PricingZone | null
  code: string
  name: string
  areas: { countryCode: string; from: string; to: string }[]
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
  const [loadError, setLoadError] = useState<string | null>(null)

  const [zoneDraft, setZoneDraft] = useState<ZoneDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteZone, setDeleteZone] = useState<PricingZone | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listPricingZones()
      .then((zoneData) => {
        setZones(zoneData)
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
          { id: 'diensten', label: 'Diensten & toeslagen' },
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
          {/* Same editor as Stamgegevens → Services & toeslagen: one source of truth. */}
          <ServiceOptionsEditor />
        </TabPanel>
      )}

      {tab === 'eenheden' && (
        <TabPanel tabId="eenheden">
          {/* Same editor as Stamgegevens → Eenheden: one source of truth for unit master data. */}
          <UnitTypeMasterEditor />
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
    </div>
  )
}
