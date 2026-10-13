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
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError, type FieldErrors } from '../../../api/problemDetails'
import { fromDateTimeLocalInput } from '../../../utils/dates'
import { UNIT_TYPE_LABELS, type PackageUnitType } from '../../packages/types'
import type { StopType } from '../../transport-orders/types'
import {
  createPortalLocation,
  listPortalLocations,
  submitPortalOrder,
  type PortalLocation,
  type PortalStopInput,
} from '../api/customerPortalApi'
import { stopTypeLabel, unitTypeLabel } from './portalStatusLabels'

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
  const { t } = useLocale()
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
        setError(t('orders.new.stopNeedsLocation'))
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
          // C-03: the datetime-local fields hold TENANT wall clock (the carrier's dock time).
          // They used to travel as a bare "2026-07-15T08:00" — no zone at all, which the API
          // cannot store as an instant; they now travel as the UTC instant the wire expects.
          requestedFrom: fromDateTimeLocalInput(stop.requestedFrom),
          requestedTo: fromDateTimeLocalInput(stop.requestedTo),
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
      toast.showSuccess(t('orders.new.submitted', { orderNumber: created.orderNumber }))
      navigate('/klantportaal')
    } catch (err) {
      const described = describeApiError(err, t('orders.new.submitFailed'))
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('orders.new.breadcrumb') }]} />
      <BackButton to="/klantportaal" label={t('orders.detail.back')} />
      <PageHeader title={t('orders.new.title')} subtitle={t('orders.new.subtitle')} />

      <form onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} fieldErrors={fieldErrors} />

        <FormSection title={t('orders.new.generalSection')} columns={3}>
          <FormField label={t('orders.new.yourReference')} htmlFor="cp-ref" hint={t('orders.new.yourReferenceHint')}>
            <input id="cp-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} maxLength={100} disabled={saving} />
          </FormField>
          <FormField label={t('orders.new.desiredDate')} htmlFor="cp-date">
            <input id="cp-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
          </FormField>
          <FormField label={t('orders.new.goodsDescription')} htmlFor="cp-goods">
            <input id="cp-goods" value={goodsDescription} onChange={(e) => setGoodsDescription(e.target.value)} maxLength={1000} disabled={saving} />
          </FormField>
          <FormField label={t('orders.new.remarks')} htmlFor="cp-remarks" className="form-span-all">
            <textarea id="cp-remarks" rows={2} value={remarks} onChange={(e) => setRemarks(e.target.value)} maxLength={4000} disabled={saving} />
          </FormField>
        </FormSection>

        <FormSection title={t('orders.new.stopsSection')} columns={1} description={t('orders.new.stopsDescription')}>
          <div className="form-span-all">
            {stops.map((stop, index) => (
              <fieldset key={stop.key} className="ui-form-section" style={{ marginBottom: 12 }}>
                <legend>
                  {index + 1}. {stopTypeLabel(t, stop.stopType)}
                </legend>
                <div className="ui-form-section-grid ui-form-section-grid-3">
                  <FormField label={t('orders.new.location')} htmlFor={`cp-loc-${stop.key}`} hint={t('orders.new.locationHint')}>
                    <select
                      id={`cp-loc-${stop.key}`}
                      value={stop.locationId}
                      onChange={(e) => setStop(stop.key, { locationId: e.target.value })}
                      disabled={saving}
                    >
                      <option value="">{t('orders.new.freeAddress')}</option>
                      {locations.map((location) => (
                        <option key={location.id} value={location.id}>
                          {location.name}
                          {location.city ? ` (${location.city})` : ''}
                          {stop.stopType === 'Loading' && location.isDefaultLoadingLocation
                            ? ` ${t('orders.new.defaultLoadingSuffix')}`
                            : ''}
                          {stop.stopType === 'Unloading' && location.isDefaultUnloadingLocation
                            ? ` ${t('orders.new.defaultUnloadingSuffix')}`
                            : ''}
                        </option>
                      ))}
                    </select>
                    {canManageLocations && (
                      <button type="button" className="tof-link" onClick={() => setNewLocationFor(stop.key)} disabled={saving}>
                        {t('orders.new.addLocation')}
                      </button>
                    )}
                  </FormField>
                  {stop.locationId === '' && (
                    <>
                      <FormField label={t('orders.new.nameCompany')} htmlFor={`cp-name-${stop.key}`}>
                        <input id={`cp-name-${stop.key}`} value={stop.locationName} onChange={(e) => setStop(stop.key, { locationName: e.target.value })} maxLength={200} disabled={saving} />
                      </FormField>
                      <FormField label={t('orders.new.address')} htmlFor={`cp-addr-${stop.key}`}>
                        <input id={`cp-addr-${stop.key}`} value={stop.address} onChange={(e) => setStop(stop.key, { address: e.target.value })} maxLength={200} disabled={saving} />
                      </FormField>
                      <FormField label={t('orders.new.postalCode')} htmlFor={`cp-postal-${stop.key}`}>
                        <input id={`cp-postal-${stop.key}`} value={stop.postalCode} onChange={(e) => setStop(stop.key, { postalCode: e.target.value })} maxLength={20} disabled={saving} />
                      </FormField>
                      <FormField label={t('orders.new.city')} htmlFor={`cp-city-${stop.key}`} required>
                        <input id={`cp-city-${stop.key}`} value={stop.city} onChange={(e) => setStop(stop.key, { city: e.target.value })} maxLength={100} disabled={saving} />
                      </FormField>
                    </>
                  )}
                  <FormField
                    label={stop.stopType === 'Loading' ? t('orders.new.loadingFrom') : t('orders.new.unloadingFrom')}
                    htmlFor={`cp-from-${stop.key}`}
                  >
                    <input id={`cp-from-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
                  </FormField>
                  <FormField label={t('orders.new.until')} htmlFor={`cp-to-${stop.key}`}>
                    <input id={`cp-to-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
                  </FormField>
                  <FormField label={t('orders.new.reference')} htmlFor={`cp-stopref-${stop.key}`}>
                    <input id={`cp-stopref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} maxLength={100} disabled={saving} />
                  </FormField>
                  <FormField label={t('orders.new.instructions')} htmlFor={`cp-instr-${stop.key}`}>
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
                    {t('orders.new.removeStop')}
                  </button>
                )}
              </fieldset>
            ))}
            <span className="customer-locations-actions">
              <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Loading')])} disabled={saving}>
                {t('orders.new.addLoadingStop')}
              </Button>
              <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Unloading')])} disabled={saving}>
                {t('orders.new.addUnloadingStop')}
              </Button>
            </span>
          </div>
        </FormSection>

        <FormSection title={t('orders.new.cargoSection')} columns={1} collapsible defaultOpen={cargo.length > 0}>
          <div className="form-span-all">
            {cargo.map((row, index) => (
              <div key={row.key} className="ne-qualification-row">
                <FormField label={t('orders.new.cargoLine', { number: index + 1 })} htmlFor={`cp-cg-desc-${row.key}`}>
                  <input id={`cp-cg-desc-${row.key}`} value={row.description} onChange={(e) => setCargoRow(row.key, { description: e.target.value })} maxLength={300} disabled={saving} />
                </FormField>
                <FormField label={t('orders.new.quantity')} htmlFor={`cp-cg-qty-${row.key}`}>
                  <input id={`cp-cg-qty-${row.key}`} type="number" min={0.01} step="0.01" value={row.expectedQuantity} onChange={(e) => setCargoRow(row.key, { expectedQuantity: e.target.value })} disabled={saving} />
                </FormField>
                <FormField label={t('orders.new.type')} htmlFor={`cp-cg-type-${row.key}`}>
                  <select id={`cp-cg-type-${row.key}`} value={row.unitType} onChange={(e) => setCargoRow(row.key, { unitType: e.target.value as PackageUnitType | '' })} disabled={saving}>
                    <option value="">{t('orders.new.choose')}</option>
                    {(Object.keys(UNIT_TYPE_LABELS) as PackageUnitType[]).map((value) => (
                      <option key={value} value={value}>
                        {unitTypeLabel(t, value)}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label={t('orders.new.weight')} htmlFor={`cp-cg-weight-${row.key}`}>
                  <input id={`cp-cg-weight-${row.key}`} type="number" min={0} step="0.01" value={row.totalWeightKg} onChange={(e) => setCargoRow(row.key, { totalWeightKg: e.target.value })} disabled={saving} />
                </FormField>
                <span>
                  <label className="customer-form-checkbox">
                    <input type="checkbox" checked={row.adrRequired} onChange={(e) => setCargoRow(row.key, { adrRequired: e.target.checked })} disabled={saving} />
                    {t('orders.new.adr')}
                  </label>
                  <Button variant="ghost" onClick={() => setCargo((rows) => rows.filter((r) => r.key !== row.key))} disabled={saving}>
                    {t('orders.new.remove')}
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
              {t('orders.new.addCargoLine')}
            </Button>
          </div>
        </FormSection>

        <FormActions>
          <Button variant="secondary" onClick={() => navigate('/klantportaal')} disabled={saving}>
            {t('common.actions.cancel')}
          </Button>
          <Button type="submit" disabled={saving}>
            {saving ? t('orders.new.submitting') : t('orders.new.submit')}
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
  const { t } = useLocale()
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
      setError(t('orders.locationDialog.nameRequired'))
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
      setError(describeApiError(err, t('orders.locationDialog.createFailed')).message)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('orders.locationDialog.title')}
      onClose={() => onClose(null)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(null)} disabled={saving}>
            {t('common.actions.cancel')}
          </Button>
          <Button type="submit" form="cp-location-form" disabled={saving}>
            {saving ? t('orders.locationDialog.creating') : t('orders.locationDialog.create')}
          </Button>
        </>
      }
    >
      <form id="cp-location-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} />
        <FormField label={t('orders.locationDialog.name')} htmlFor="cpl-name" required>
          <input id="cpl-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} disabled={saving} />
        </FormField>
        <FormField label={t('orders.locationDialog.street')} htmlFor="cpl-street">
          <input id="cpl-street" value={street} onChange={(e) => setStreet(e.target.value)} maxLength={150} disabled={saving} />
        </FormField>
        <FormField label={t('orders.locationDialog.houseNumber')} htmlFor="cpl-house">
          <input id="cpl-house" value={houseNumber} onChange={(e) => setHouseNumber(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label={t('orders.locationDialog.postalCode')} htmlFor="cpl-postal">
          <input id="cpl-postal" value={postalCode} onChange={(e) => setPostalCode(e.target.value)} maxLength={20} disabled={saving} />
        </FormField>
        <FormField label={t('orders.locationDialog.city')} htmlFor="cpl-city">
          <input id="cpl-city" value={city} onChange={(e) => setCity(e.target.value)} maxLength={100} disabled={saving} />
        </FormField>
      </form>
    </Modal>
  )
}
