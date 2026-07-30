import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { FormActions } from '../../../components/ui/FormActions'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError, type FieldErrors } from '../../../api/problemDetails'
import { UNIT_TYPE_LABELS, type PackageUnitType } from '../../packages/types'
import type { StopType } from '../../transport-orders/types'
import {
  createPortalLocation,
  listPortalLocations,
  submitPortalOrder,
  type PortalLocation,
  type PortalStopInput,
} from '../api/customerPortalApi'

interface StopRow {
  key: string
  stopType: StopType
  locationId: string
  locationName: string
  address: string
  postalCode: string
  city: string
  requestedFrom: string
  requestedTo: string
  reference: string
  instructions: string
}

interface CargoRow {
  key: string
  description: string
  expectedQuantity: string
  quantityUnit: string
  unitType: PackageUnitType | ''
  totalWeightKg: string
  adrRequired: boolean
  adrDetails: string
}

let portalKey = 0
const nextKey = () => `p-${(portalKey += 1)}`

function emptyStop(stopType: StopType): StopRow {
  return {
    key: nextKey(), stopType, locationId: '', locationName: '', address: '', postalCode: '', city: '',
    requestedFrom: '', requestedTo: '', reference: '', instructions: '',
  }
}

/**
 * Customer-facing order intake. Deliberately a lean composition: the heavy planner concerns
 * (pricing, planned windows, execution planning) do not exist here, but every submission
 * runs through the same backend use case and validators as internal order entry.
 */
export function CustomerPortalNewOrderPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManageLocations = hasPermission('customer_portal.manage_locations')

  const [locations, setLocations] = useState<PortalLocation[]>([])
  const [customerReference, setCustomerReference] = useState('')
  const [orderDate, setOrderDate] = useState(new Date().toISOString().slice(0, 10))
  const [goodsDescription, setGoodsDescription] = useState('')
  const [remarks, setRemarks] = useState('')
  const [stops, setStops] = useState<StopRow[]>([emptyStop('Loading'), emptyStop('Unloading')])
  const [cargo, setCargo] = useState<CargoRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)
  const [newLocationFor, setNewLocationFor] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listPortalLocations()
      .then((data) => {
        if (mounted) setLocations(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  function setStop(key: string, patch: Partial<StopRow>) {
    setStops((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function setCargoRow(key: string, patch: Partial<CargoRow>) {
    setCargo((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setFieldErrors({})
    for (const stop of stops) {
      if (!stop.locationId && !stop.city.trim()) {
        setError('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')
        return
      }
    }

    setSaving(true)
    try {
      const payload = {
        customerReference: customerReference.trim() || null,
        orderDate,
        goodsDescription: goodsDescription.trim() || null,
        remarks: remarks.trim() || null,
        stops: stops.map<PortalStopInput>((stop) => ({
          stopType: stop.stopType,
          locationId: stop.locationId || null,
          locationName: stop.locationId ? null : stop.locationName.trim() || null,
          address: stop.locationId ? null : stop.address.trim() || null,
          postalCode: stop.locationId ? null : stop.postalCode.trim() || null,
          city: stop.locationId ? null : stop.city.trim() || null,
          countryCode: stop.locationId ? null : 'BE',
          requestedFrom: stop.requestedFrom || null,
          requestedTo: stop.requestedTo || null,
          reference: stop.reference.trim() || null,
          instructions: stop.instructions.trim() || null,
        })),
        cargoItems: cargo
          .filter((row) => row.description.trim())
          .map((row) => ({
            description: row.description.trim(),
            expectedQuantity: Number(row.expectedQuantity) || 1,
            quantityUnit: row.quantityUnit.trim() || null,
            unitType: row.unitType || null,
            totalWeightKg: row.totalWeightKg === '' ? null : Number(row.totalWeightKg),
            adrRequired: row.adrRequired,
            adrDetails: row.adrRequired ? row.adrDetails.trim() || null : null,
          })),
      }
      const created = await submitPortalOrder(payload)
      toast.showSuccess(`Opdracht ${created.orderNumber} ingediend. Onze planning neemt deze in behandeling.`)
      navigate('/klantportaal')
    } catch (err) {
      const described = describeApiError(err, 'De opdracht kon niet worden ingediend.')
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/klantportaal' }, { label: 'Nieuwe opdracht' }]} />
      <BackButton to="/klantportaal" label="Terug naar mijn opdrachten" />
      <PageHeader title="Nieuwe transportopdracht indienen" subtitle="Na indiening beoordeelt onze planning uw aanvraag." />

      <form onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} fieldErrors={fieldErrors} />

        <FormSection title="Algemeen" columns={3}>
          <FormField label="Uw referentie" htmlFor="cp-ref" hint="PO-nummer of eigen kenmerk.">
            <input id="cp-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} maxLength={100} disabled={saving} />
          </FormField>
          <FormField label="Gewenste datum" htmlFor="cp-date">
            <input id="cp-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
          </FormField>
          <FormField label="Omschrijving goederen" htmlFor="cp-goods">
            <input id="cp-goods" value={goodsDescription} onChange={(e) => setGoodsDescription(e.target.value)} maxLength={1000} disabled={saving} />
          </FormField>
          <FormField label="Opmerkingen / instructies" htmlFor="cp-remarks" className="form-span-all">
            <textarea id="cp-remarks" rows={2} value={remarks} onChange={(e) => setRemarks(e.target.value)} maxLength={4000} disabled={saving} />
          </FormField>
        </FormSection>

        <FormSection title="Stops" columns={1} description="Kies uw eigen locaties of vul een vrij adres in.">
          <div className="form-span-all">
            {stops.map((stop, index) => (
              <fieldset key={stop.key} className="ui-form-section" style={{ marginBottom: 12 }}>
                <legend>
                  {index + 1}. {stop.stopType === 'Loading' ? 'Laden' : 'Lossen'}
                </legend>
                <div className="ui-form-section-grid ui-form-section-grid-3">
                  <FormField label="Locatie" htmlFor={`cp-loc-${stop.key}`} hint="Leeg = vrij adres hieronder.">
                    <select
                      id={`cp-loc-${stop.key}`}
                      value={stop.locationId}
                      onChange={(e) => setStop(stop.key, { locationId: e.target.value })}
                      disabled={saving}
                    >
                      <option value="">— Vrij adres —</option>
                      {locations.map((location) => (
                        <option key={location.id} value={location.id}>
                          {location.name}
                          {location.city ? ` (${location.city})` : ''}
                          {stop.stopType === 'Loading' && location.isDefaultLoadingLocation ? ' — standaard laden' : ''}
                          {stop.stopType === 'Unloading' && location.isDefaultUnloadingLocation ? ' — standaard lossen' : ''}
                        </option>
                      ))}
                    </select>
                    {canManageLocations && (
                      <button type="button" className="tof-link" onClick={() => setNewLocationFor(stop.key)} disabled={saving}>
                        + Nieuwe locatie
                      </button>
                    )}
                  </FormField>
                  {stop.locationId === '' && (
                    <>
                      <FormField label="Naam / bedrijf" htmlFor={`cp-name-${stop.key}`}>
                        <input id={`cp-name-${stop.key}`} value={stop.locationName} onChange={(e) => setStop(stop.key, { locationName: e.target.value })} maxLength={200} disabled={saving} />
                      </FormField>
                      <FormField label="Adres" htmlFor={`cp-addr-${stop.key}`}>
                        <input id={`cp-addr-${stop.key}`} value={stop.address} onChange={(e) => setStop(stop.key, { address: e.target.value })} maxLength={200} disabled={saving} />
                      </FormField>
                      <FormField label="Postcode" htmlFor={`cp-postal-${stop.key}`}>
                        <input id={`cp-postal-${stop.key}`} value={stop.postalCode} onChange={(e) => setStop(stop.key, { postalCode: e.target.value })} maxLength={20} disabled={saving} />
                      </FormField>
                      <FormField label="Plaats" htmlFor={`cp-city-${stop.key}`} required>
                        <input id={`cp-city-${stop.key}`} value={stop.city} onChange={(e) => setStop(stop.key, { city: e.target.value })} maxLength={100} disabled={saving} />
                      </FormField>
                    </>
                  )}
                  <FormField label={stop.stopType === 'Loading' ? 'Laden vanaf' : 'Lossen vanaf'} htmlFor={`cp-from-${stop.key}`}>
                    <input id={`cp-from-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
                  </FormField>
                  <FormField label="Tot" htmlFor={`cp-to-${stop.key}`}>
                    <input id={`cp-to-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
                  </FormField>
                  <FormField label="Referentie" htmlFor={`cp-stopref-${stop.key}`}>
                    <input id={`cp-stopref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} maxLength={100} disabled={saving} />
                  </FormField>
                  <FormField label="Instructies" htmlFor={`cp-instr-${stop.key}`}>
                    <input id={`cp-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} maxLength={2000} disabled={saving} />
                  </FormField>
                </div>
                {stops.length > 2 && (
                  <button
                    type="button"
                    className="tof-link tof-link-danger"
                    onClick={() => setStops((rows) => rows.filter((row) => row.key !== stop.key))}
                    disabled={saving}
                  >
                    Stop verwijderen
                  </button>
                )}
              </fieldset>
            ))}
            <span className="customer-locations-actions">
              <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Loading')])} disabled={saving}>
                + Laadstop
              </Button>
              <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Unloading')])} disabled={saving}>
                + Losstop
              </Button>
            </span>
          </div>
        </FormSection>

        <FormSection title="Goederen (optioneel)" columns={1} collapsible defaultOpen={cargo.length > 0}>
          <div className="form-span-all">
            {cargo.map((row, index) => (
              <div key={row.key} className="ne-qualification-row">
                <FormField label={`Lijn ${index + 1} — omschrijving`} htmlFor={`cp-cg-desc-${row.key}`}>
                  <input id={`cp-cg-desc-${row.key}`} value={row.description} onChange={(e) => setCargoRow(row.key, { description: e.target.value })} maxLength={300} disabled={saving} />
                </FormField>
                <FormField label="Aantal" htmlFor={`cp-cg-qty-${row.key}`}>
                  <input id={`cp-cg-qty-${row.key}`} type="number" min={0.01} step="0.01" value={row.expectedQuantity} onChange={(e) => setCargoRow(row.key, { expectedQuantity: e.target.value })} disabled={saving} />
                </FormField>
                <FormField label="Type" htmlFor={`cp-cg-type-${row.key}`}>
                  <select id={`cp-cg-type-${row.key}`} value={row.unitType} onChange={(e) => setCargoRow(row.key, { unitType: e.target.value as PackageUnitType | '' })} disabled={saving}>
                    <option value="">— Kies —</option>
                    {Object.entries(UNIT_TYPE_LABELS).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Gewicht (kg)" htmlFor={`cp-cg-weight-${row.key}`}>
                  <input id={`cp-cg-weight-${row.key}`} type="number" min={0} step="0.01" value={row.totalWeightKg} onChange={(e) => setCargoRow(row.key, { totalWeightKg: e.target.value })} disabled={saving} />
                </FormField>
                <span>
                  <label className="customer-form-checkbox">
                    <input type="checkbox" checked={row.adrRequired} onChange={(e) => setCargoRow(row.key, { adrRequired: e.target.checked })} disabled={saving} />
                    ADR
                  </label>
                  <Button variant="ghost" onClick={() => setCargo((rows) => rows.filter((r) => r.key !== row.key))} disabled={saving}>
                    Verwijderen
                  </Button>
                </span>
              </div>
            ))}
            <Button
              variant="secondary"
              disabled={saving}
              onClick={() =>
                setCargo((rows) => [
                  ...rows,
                  { key: nextKey(), description: '', expectedQuantity: '1', quantityUnit: '', unitType: '', totalWeightKg: '', adrRequired: false, adrDetails: '' },
                ])
              }
            >
              + Goederenlijn
            </Button>
          </div>
        </FormSection>

        <FormActions>
          <Button variant="secondary" onClick={() => navigate('/klantportaal')} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" disabled={saving}>
            {saving ? 'Indienen…' : 'Opdracht indienen'}
          </Button>
        </FormActions>
      </form>

      {newLocationFor && (
        <PortalLocationDialog
          onClose={(created) => {
            if (created) {
              setLocations((current) => [...current, created])
              setStop(newLocationFor, { locationId: created.id })
            }
            setNewLocationFor(null)
          }}
        />
      )}
    </div>
  )
}

function PortalLocationDialog({ onClose }: { onClose: (created: PortalLocation | null) => void }) {
  const [name, setName] = useState('')
  const [street, setStreet] = useState('')
  const [houseNumber, setHouseNumber] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [city, setCity] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim()) {
      setError('Een naam is verplicht.')
      return
    }
    setSaving(true)
    try {
      const created = await createPortalLocation({
        name: name.trim(),
        street: street.trim() || null,
        houseNumber: houseNumber.trim() || null,
        postalCode: postalCode.trim() || null,
        city: city.trim() || null,
        countryCode: 'BE',
      })
      onClose(created)
    } catch (err) {
      setError(describeApiError(err, 'De locatie kon niet worden aangemaakt.').message)
      setSaving(false)
    }
  }

  return (
    <Modal
      title="Nieuwe locatie"
      onClose={() => onClose(null)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(null)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="cp-location-form" disabled={saving}>
            {saving ? 'Aanmaken…' : 'Locatie aanmaken'}
          </Button>
        </>
      }
    >
      <form id="cp-location-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} />
        <FormField label="Naam" htmlFor="cpl-name" required>
          <input id="cpl-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label="Straat" htmlFor="cpl-street">
          <input id="cpl-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} disabled={saving} />
        </FormField>
        <FormField label="Nummer" htmlFor="cpl-house">
          <input id="cpl-house" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label="Postcode" htmlFor="cpl-postal">
          <input id="cpl-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label="Plaats" htmlFor="cpl-city">
          <input id="cpl-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} disabled={saving} />
        </FormField>
      </form>
    </Modal>
  )
}
