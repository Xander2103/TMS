import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { getCustomer, searchCustomers } from '../../customers/api/customersApi'
import type { CustomerDetail, CustomerListItem } from '../../customers/types'
import { LocationSelect } from '../../locations/components/LocationSelect'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { STOP_TYPE_LABELS, type StopInput, type TransportOrderDetail, type TransportOrderInput } from '../types'
import './transport-order-form.css'

interface CargoFormRow {
  key: string
  description: string
  barcode: string
  expectedQuantity: string
  quantityUnit: string
  notes: string
}

interface StopFormRow {
  key: string
  stopType: StopInput['stopType']
  locationId: string
  locationName: string
  address: string
  postalCode: string
  city: string
  countryCode: string
  plannedFrom: string
  plannedTo: string
  requestedFrom: string
  requestedTo: string
  confirmedFrom: string
  confirmedTo: string
  earliestAllowed: string
  latestAllowed: string
  appointmentRequired: boolean
  appointmentReference: string
  reference: string
  instructions: string
  accessInstructions: string
  loadingInstructions: string
  unloadingInstructions: string
}

let stopKeyCounter = 0
function nextStopKey(): string {
  stopKeyCounter += 1
  return `stop-${stopKeyCounter}`
}

function emptyStop(stopType: StopInput['stopType']): StopFormRow {
  return {
    key: nextStopKey(),
    stopType,
    locationId: '',
    locationName: '',
    address: '',
    postalCode: '',
    city: '',
    countryCode: 'BE',
    plannedFrom: '',
    plannedTo: '',
    requestedFrom: '',
    requestedTo: '',
    confirmedFrom: '',
    confirmedTo: '',
    earliestAllowed: '',
    latestAllowed: '',
    appointmentRequired: false,
    appointmentReference: '',
    reference: '',
    instructions: '',
    accessInstructions: '',
    loadingInstructions: '',
    unloadingInstructions: '',
  }
}

/** ISO datetime → value usable by <input type="datetime-local"> (minute precision). */
function toLocalInput(value: string | null): string {
  return value ? value.slice(0, 16) : ''
}

interface TransportOrderFormProps {
  order?: TransportOrderDetail
  onSubmit: (input: TransportOrderInput) => Promise<void>
  onCancel?: () => void
  submitLabel: string
}

/** Shared create/edit form: order header, cargo fields and the multi-stop editor. */
export function TransportOrderForm({ order, onSubmit, onCancel, submitLabel }: TransportOrderFormProps) {
  const [customers, setCustomers] = useState<CustomerListItem[]>([])

  const [customerId, setCustomerId] = useState(order?.customerId ?? '')
  const [customerReference, setCustomerReference] = useState(order?.customerReference ?? '')
  const [orderDate, setOrderDate] = useState(order?.orderDate ?? new Date().toISOString().slice(0, 10))
  const [goodsDescription, setGoodsDescription] = useState(order?.goodsDescription ?? '')
  const [quantity, setQuantity] = useState(order?.quantity === null || order === undefined ? '' : String(order.quantity))
  const [quantityUnit, setQuantityUnit] = useState(order?.quantityUnit ?? '')
  const [weightKg, setWeightKg] = useState(order?.weightKg === null || order === undefined ? '' : String(order.weightKg))
  const [volumeM3, setVolumeM3] = useState(order?.volumeM3 === null || order === undefined ? '' : String(order.volumeM3))
  const [palletCount, setPalletCount] = useState(
    order?.palletCount === null || order === undefined ? '' : String(order.palletCount),
  )
  const [adrRequired, setAdrRequired] = useState(order?.adrRequired ?? false)
  const [craneRequired, setCraneRequired] = useState(order?.craneRequired ?? false)
  const [agreedPrice, setAgreedPrice] = useState(
    order?.agreedPrice === null || order === undefined ? '' : String(order.agreedPrice),
  )
  const [notes, setNotes] = useState(order?.notes ?? '')
  const [stops, setStops] = useState<StopFormRow[]>(() =>
    order && order.stops.length > 0
      ? order.stops.map((s) => ({
          key: nextStopKey(),
          stopType: s.stopType,
          locationId: s.locationId ?? '',
          locationName: s.locationId ? '' : s.locationName,
          address: s.address ?? '',
          postalCode: s.postalCode ?? '',
          city: s.city ?? '',
          countryCode: s.countryCode ?? 'BE',
          plannedFrom: toLocalInput(s.plannedFrom),
          plannedTo: toLocalInput(s.plannedTo),
          requestedFrom: toLocalInput(s.requestedFrom),
          requestedTo: toLocalInput(s.requestedTo),
          confirmedFrom: toLocalInput(s.confirmedFrom),
          confirmedTo: toLocalInput(s.confirmedTo),
          earliestAllowed: toLocalInput(s.earliestAllowed),
          latestAllowed: toLocalInput(s.latestAllowed),
          appointmentRequired: s.appointmentRequired,
          appointmentReference: s.appointmentReference ?? '',
          reference: s.reference ?? '',
          instructions: s.instructions ?? '',
          accessInstructions: s.accessInstructions ?? '',
          loadingInstructions: s.loadingInstructions ?? '',
          unloadingInstructions: s.unloadingInstructions ?? '',
        }))
      : [emptyStop('Loading'), emptyStop('Unloading')],
  )

  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() =>
    (order?.cargoItems ?? []).map((c) => ({
      key: nextStopKey(),
      description: c.description,
      barcode: c.barcode ?? '',
      expectedQuantity: String(c.expectedQuantity),
      quantityUnit: c.quantityUnit ?? '',
      notes: c.notes ?? '',
    })),
  )

  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [loadedCustomerDetail, setLoadedCustomerDetail] = useState<{ id: string; detail: CustomerDetail } | null>(null)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  // Surface the selected customer's intake requirements (reference/PO/signed CMR) as hints.
  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getCustomer(customerId)
      .then((detail) => {
        if (mounted) setLoadedCustomerDetail({ id: customerId, detail })
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [customerId])

  // Derive so a stale detail (from a previously selected customer) is never shown.
  const customerRequirements = loadedCustomerDetail?.id === customerId ? loadedCustomerDetail.detail : null
  const requirementHints = customerRequirements
    ? [
        customerRequirements.customerReferenceRequired ? 'een klantreferentie is verplicht' : null,
        customerRequirements.purchaseOrderRequired ? 'een bestelbon (PO) is vereist' : null,
        customerRequirements.signedDeliveryNoteRequired ? 'een getekende leverbon (CMR) is vereist' : null,
      ].filter((hint): hint is string => hint !== null)
    : []

  function setStop(key: string, patch: Partial<StopFormRow>) {
    setStops((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function setCargo(key: string, patch: Partial<CargoFormRow>) {
    setCargoItems((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function moveStop(index: number, delta: number) {
    setStops((rows) => {
      const target = index + delta
      if (target < 0 || target >= rows.length) return rows
      const next = [...rows]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!customerId) {
      setFormError('Selecteer een klant.')
      return
    }
    if (!goodsDescription.trim()) {
      setFormError('Een omschrijving van de goederen is verplicht.')
      return
    }
    for (const stop of stops) {
      if (!stop.locationId && !stop.city.trim()) {
        setFormError('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')
        return
      }
      const windowPairs: Array<[string, string]> = [
        [stop.plannedFrom, stop.plannedTo],
        [stop.requestedFrom, stop.requestedTo],
        [stop.confirmedFrom, stop.confirmedTo],
      ]
      if (windowPairs.some(([from, to]) => from && to && to < from)) {
        setFormError('Het einde van een tijdvenster moet na het begin liggen.')
        return
      }
      if (stop.earliestAllowed && stop.latestAllowed && stop.latestAllowed < stop.earliestAllowed) {
        setFormError('Het uiterste tijdstip moet na het vroegst toegelaten tijdstip liggen.')
        return
      }
    }
    for (const cargo of cargoItems) {
      if (!cargo.description.trim()) {
        setFormError('Elke goederenlijn heeft een omschrijving nodig.')
        return
      }
      if (cargo.expectedQuantity === '' || Number(cargo.expectedQuantity) <= 0) {
        setFormError('De verwachte hoeveelheid van een goederenlijn moet groter dan nul zijn.')
        return
      }
    }
    const barcodes = cargoItems.map((c) => c.barcode.trim().toLowerCase()).filter(Boolean)
    if (barcodes.length !== new Set(barcodes).size) {
      setFormError('Een barcode mag maar één keer voorkomen binnen dezelfde opdracht.')
      return
    }

    const input: TransportOrderInput = {
      customerId,
      customerReference: customerReference.trim() || null,
      orderDate: orderDate || null,
      goodsDescription: goodsDescription.trim(),
      quantity: quantity === '' ? null : Number(quantity),
      quantityUnit: quantityUnit.trim() || null,
      weightKg: weightKg === '' ? null : Number(weightKg),
      volumeM3: volumeM3 === '' ? null : Number(volumeM3),
      palletCount: palletCount === '' ? null : Number(palletCount),
      adrRequired,
      craneRequired,
      agreedPrice: agreedPrice === '' ? null : Number(agreedPrice),
      notes: notes.trim() || null,
      stops: stops.map((stop) => ({
        stopType: stop.stopType,
        locationId: stop.locationId || null,
        locationName: stop.locationId ? null : stop.locationName.trim() || null,
        address: stop.address.trim() || null,
        postalCode: stop.postalCode.trim() || null,
        city: stop.city.trim() || null,
        countryCode: stop.countryCode.trim() || null,
        plannedFrom: stop.plannedFrom ? `${stop.plannedFrom}:00Z` : null,
        plannedTo: stop.plannedTo ? `${stop.plannedTo}:00Z` : null,
        requestedFrom: stop.requestedFrom ? `${stop.requestedFrom}:00Z` : null,
        requestedTo: stop.requestedTo ? `${stop.requestedTo}:00Z` : null,
        confirmedFrom: stop.confirmedFrom ? `${stop.confirmedFrom}:00Z` : null,
        confirmedTo: stop.confirmedTo ? `${stop.confirmedTo}:00Z` : null,
        earliestAllowed: stop.earliestAllowed ? `${stop.earliestAllowed}:00Z` : null,
        latestAllowed: stop.latestAllowed ? `${stop.latestAllowed}:00Z` : null,
        appointmentRequired: stop.appointmentRequired,
        appointmentReference: stop.appointmentReference.trim() || null,
        reference: stop.reference.trim() || null,
        instructions: stop.instructions.trim() || null,
        accessInstructions: stop.accessInstructions.trim() || null,
        loadingInstructions: stop.loadingInstructions.trim() || null,
        unloadingInstructions: stop.unloadingInstructions.trim() || null,
      })),
      cargoItems: cargoItems.map((cargo) => ({
        description: cargo.description.trim(),
        barcode: cargo.barcode.trim() || null,
        expectedQuantity: Number(cargo.expectedQuantity),
        quantityUnit: cargo.quantityUnit.trim() || null,
        notes: cargo.notes.trim() || null,
      })),
    }

    setSaving(true)
    try {
      await onSubmit(input)
    } catch (err) {
      setFormError(
        err instanceof ApiError && err.status === 400
          ? 'De opdracht kon niet worden opgeslagen — controleer de invoer.'
          : 'De opdracht kon niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="tof" onSubmit={handleSubmit} noValidate>
      {formError && (
        <div className="tof-error" role="alert">
          {formError}
        </div>
      )}

      <div className="tof-row">
        <FormField label="Klant" htmlFor="to-customer" required>
          <select id="to-customer" value={customerId} onChange={(e) => setCustomerId(e.target.value)} disabled={saving}>
            <option value="">Selecteer een klant…</option>
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.customerNumber})
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Klantreferentie" htmlFor="to-ref">
          <input id="to-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} disabled={saving} maxLength={100} />
        </FormField>
        <FormField label="Opdrachtdatum" htmlFor="to-date">
          <input id="to-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      {requirementHints.length > 0 && (
        <p className="tof-customer-requirements" role="note">
          Let op voor deze klant: {requirementHints.join(', ')}.
        </p>
      )}
      {customerRequirements?.isBlocked && (
        <p className="tof-error" role="alert">
          Deze klant is geblokkeerd{customerRequirements.blockReason ? ` (${customerRequirements.blockReason})` : ''}; er
          kunnen geen nieuwe opdrachten voor worden aangemaakt.
        </p>
      )}

      <FormField label="Omschrijving goederen" htmlFor="to-goods" required>
        <textarea id="to-goods" rows={2} value={goodsDescription} onChange={(e) => setGoodsDescription(e.target.value)} disabled={saving} maxLength={1000} />
      </FormField>

      <div className="tof-row tof-row-4">
        <FormField label="Aantal" htmlFor="to-qty">
          <input id="to-qty" type="number" min={0} step="0.01" value={quantity} onChange={(e) => setQuantity(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label="Eenheid" htmlFor="to-unit">
          <input id="to-unit" value={quantityUnit} onChange={(e) => setQuantityUnit(e.target.value)} disabled={saving} maxLength={50} placeholder="bv. paletten" />
        </FormField>
        <FormField label="Gewicht (kg)" htmlFor="to-weight">
          <input id="to-weight" type="number" min={0} step="0.01" value={weightKg} onChange={(e) => setWeightKg(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label="Volume (m³)" htmlFor="to-volume">
          <input id="to-volume" type="number" min={0} step="0.01" value={volumeM3} onChange={(e) => setVolumeM3(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      <div className="tof-row tof-row-4">
        <FormField label="Paletten" htmlFor="to-pallets">
          <input id="to-pallets" type="number" min={0} value={palletCount} onChange={(e) => setPalletCount(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label="Afgesproken prijs (€)" htmlFor="to-price">
          <input id="to-price" type="number" min={0} step="0.01" value={agreedPrice} onChange={(e) => setAgreedPrice(e.target.value)} disabled={saving} />
        </FormField>
        <label className="tof-checkbox">
          <input type="checkbox" checked={adrRequired} onChange={(e) => setAdrRequired(e.target.checked)} disabled={saving} />
          ADR-transport
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={craneRequired} onChange={(e) => setCraneRequired(e.target.checked)} disabled={saving} />
          Kraan vereist
        </label>
      </div>

      <div className="tof-stops-header">
        <h3>Stops</h3>
        <div className="tof-stops-actions">
          <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Loading')])} disabled={saving}>
            + Laadstop
          </Button>
          <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Unloading')])} disabled={saving}>
            + Losstop
          </Button>
        </div>
      </div>

      {stops.map((stop, index) => (
        <fieldset key={stop.key} className="tof-stop">
          <legend>
            {index + 1}. {STOP_TYPE_LABELS[stop.stopType]}
          </legend>
          <div className="tof-stop-toolbar">
            <select
              value={stop.stopType}
              onChange={(e) => setStop(stop.key, { stopType: e.target.value as StopInput['stopType'] })}
              disabled={saving}
              aria-label="Stoptype"
            >
              <option value="Loading">Laden</option>
              <option value="Unloading">Lossen</option>
            </select>
            <button type="button" className="tof-link" onClick={() => moveStop(index, -1)} disabled={saving || index === 0}>
              ↑
            </button>
            <button type="button" className="tof-link" onClick={() => moveStop(index, 1)} disabled={saving || index === stops.length - 1}>
              ↓
            </button>
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => setStops((rows) => rows.filter((row) => row.key !== stop.key))}
              disabled={saving}
            >
              Verwijderen
            </button>
          </div>
          <div className="tof-row">
            <FormField label="Locatie (stamgegevens)" htmlFor={`st-loc-${stop.key}`}>
              <LocationSelect
                id={`st-loc-${stop.key}`}
                value={stop.locationId}
                onChange={(locationId) => setStop(stop.key, { locationId })}
                disabled={saving}
                placeholder="Geen — adres hieronder"
              />
            </FormField>
            <FormField label="Naam (vrij adres)" htmlFor={`st-name-${stop.key}`}>
              <input
                id={`st-name-${stop.key}`}
                value={stop.locationName}
                onChange={(e) => setStop(stop.key, { locationName: e.target.value })}
                disabled={saving || stop.locationId !== ''}
                maxLength={200}
              />
            </FormField>
          </div>
          {stop.locationId === '' && (
            <div className="tof-row tof-row-4">
              <FormField label="Adres" htmlFor={`st-addr-${stop.key}`}>
                <input id={`st-addr-${stop.key}`} value={stop.address} onChange={(e) => setStop(stop.key, { address: e.target.value })} disabled={saving} maxLength={300} />
              </FormField>
              <FormField label="Postcode" htmlFor={`st-pc-${stop.key}`}>
                <input id={`st-pc-${stop.key}`} value={stop.postalCode} onChange={(e) => setStop(stop.key, { postalCode: e.target.value })} disabled={saving} maxLength={20} />
              </FormField>
              <FormField label="Plaats" htmlFor={`st-city-${stop.key}`} required>
                <input id={`st-city-${stop.key}`} value={stop.city} onChange={(e) => setStop(stop.key, { city: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Land" htmlFor={`st-cc-${stop.key}`}>
                <CountryCombobox
                  id={`st-cc-${stop.key}`}
                  value={stop.countryCode || null}
                  onChange={(code) => setStop(stop.key, { countryCode: code ?? '' })}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          <div className="tof-row tof-row-4">
            <FormField label="Venster van" htmlFor={`st-from-${stop.key}`}>
              <input id={`st-from-${stop.key}`} type="datetime-local" value={stop.plannedFrom} onChange={(e) => setStop(stop.key, { plannedFrom: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Venster tot" htmlFor={`st-to-${stop.key}`}>
              <input id={`st-to-${stop.key}`} type="datetime-local" value={stop.plannedTo} onChange={(e) => setStop(stop.key, { plannedTo: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Referentie" htmlFor={`st-ref-${stop.key}`}>
              <input id={`st-ref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Instructies" htmlFor={`st-instr-${stop.key}`}>
              <input id={`st-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} disabled={saving} maxLength={2000} />
            </FormField>
          </div>
          <details
            className="tof-stop-extended"
            open={Boolean(
              stop.requestedFrom || stop.requestedTo || stop.confirmedFrom || stop.confirmedTo ||
              stop.earliestAllowed || stop.latestAllowed || stop.appointmentRequired ||
              stop.accessInstructions || stop.loadingInstructions || stop.unloadingInstructions,
            )}
          >
            <summary>Tijdvensters, afspraak &amp; instructies</summary>
            <div className="tof-row tof-row-4">
              <FormField label="Gevraagd van" htmlFor={`st-reqfrom-${stop.key}`} hint="Venster gevraagd door de klant">
                <input id={`st-reqfrom-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Gevraagd tot" htmlFor={`st-reqto-${stop.key}`}>
                <input id={`st-reqto-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd van" htmlFor={`st-conffrom-${stop.key}`} hint="Venster bevestigd aan de klant">
                <input id={`st-conffrom-${stop.key}`} type="datetime-local" value={stop.confirmedFrom} onChange={(e) => setStop(stop.key, { confirmedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd tot" htmlFor={`st-confto-${stop.key}`}>
                <input id={`st-confto-${stop.key}`} type="datetime-local" value={stop.confirmedTo} onChange={(e) => setStop(stop.key, { confirmedTo: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField label="Vroegst toegelaten" htmlFor={`st-earliest-${stop.key}`}>
                <input id={`st-earliest-${stop.key}`} type="datetime-local" value={stop.earliestAllowed} onChange={(e) => setStop(stop.key, { earliestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Uiterste tijdstip" htmlFor={`st-latest-${stop.key}`} hint="Na dit tijdstip is een reden voor late aankomst verplicht">
                <input id={`st-latest-${stop.key}`} type="datetime-local" value={stop.latestAllowed} onChange={(e) => setStop(stop.key, { latestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <label className="tof-checkbox">
                <input type="checkbox" checked={stop.appointmentRequired} onChange={(e) => setStop(stop.key, { appointmentRequired: e.target.checked })} disabled={saving} />
                Afspraak verplicht
              </label>
              <FormField label="Afspraakreferentie" htmlFor={`st-appref-${stop.key}`}>
                <input id={`st-appref-${stop.key}`} value={stop.appointmentReference} onChange={(e) => setStop(stop.key, { appointmentReference: e.target.value })} disabled={saving} maxLength={100} placeholder="bv. slotnummer" />
              </FormField>
            </div>
            <div className="tof-row">
              <FormField label="Toegangsinstructies" htmlFor={`st-access-${stop.key}`}>
                <input id={`st-access-${stop.key}`} value={stop.accessInstructions} onChange={(e) => setStop(stop.key, { accessInstructions: e.target.value })} disabled={saving} maxLength={2000} />
              </FormField>
              {stop.stopType === 'Loading' ? (
                <FormField label="Laadinstructies" htmlFor={`st-loadinstr-${stop.key}`}>
                  <input id={`st-loadinstr-${stop.key}`} value={stop.loadingInstructions} onChange={(e) => setStop(stop.key, { loadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              ) : (
                <FormField label="Losinstructies" htmlFor={`st-unloadinstr-${stop.key}`}>
                  <input id={`st-unloadinstr-${stop.key}`} value={stop.unloadingInstructions} onChange={(e) => setStop(stop.key, { unloadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              )}
            </div>
          </details>
        </fieldset>
      ))}

      <div className="tof-stops-header">
        <h3>Goederenlijnen (scanbaar)</h3>
        <div className="tof-stops-actions">
          <Button
            variant="secondary"
            onClick={() =>
              setCargoItems((rows) => [
                ...rows,
                { key: nextStopKey(), description: '', barcode: '', expectedQuantity: '1', quantityUnit: '', notes: '' },
              ])
            }
            disabled={saving}
          >
            + Goederenlijn
          </Button>
        </div>
      </div>
      {cargoItems.length === 0 && (
        <p className="tof-cargo-hint">
          Zonder goederenlijnen kan de chauffeur niet scannen; de opdracht blijft wel gewoon uitvoerbaar.
        </p>
      )}
      {cargoItems.map((cargo, index) => (
        <fieldset key={cargo.key} className="tof-stop">
          <legend>Lijn {index + 1}</legend>
          <div className="tof-row tof-row-4">
            <FormField label="Omschrijving" htmlFor={`cg-desc-${cargo.key}`} required>
              <input id={`cg-desc-${cargo.key}`} value={cargo.description} onChange={(e) => setCargo(cargo.key, { description: e.target.value })} disabled={saving} maxLength={300} />
            </FormField>
            <FormField label="Barcode" htmlFor={`cg-bc-${cargo.key}`}>
              <input id={`cg-bc-${cargo.key}`} value={cargo.barcode} onChange={(e) => setCargo(cargo.key, { barcode: e.target.value })} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Verwacht aantal" htmlFor={`cg-qty-${cargo.key}`} required>
              <input id={`cg-qty-${cargo.key}`} type="number" min={0.01} step="0.01" value={cargo.expectedQuantity} onChange={(e) => setCargo(cargo.key, { expectedQuantity: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Eenheid" htmlFor={`cg-unit-${cargo.key}`}>
              <input id={`cg-unit-${cargo.key}`} value={cargo.quantityUnit} onChange={(e) => setCargo(cargo.key, { quantityUnit: e.target.value })} disabled={saving} maxLength={50} placeholder="bv. colli" />
            </FormField>
          </div>
          <div className="tof-stop-toolbar">
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => setCargoItems((rows) => rows.filter((row) => row.key !== cargo.key))}
              disabled={saving}
            >
              Verwijderen
            </button>
          </div>
        </fieldset>
      ))}

      <FormField label="Notities" htmlFor="to-notes">
        <textarea id="to-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={saving} maxLength={4000} />
      </FormField>

      <div className="tof-actions">
        {onCancel && (
          <Button variant="secondary" onClick={onCancel} disabled={saving}>
            Annuleren
          </Button>
        )}
        <Button type="submit" disabled={saving}>
          {saving ? 'Opslaan…' : submitLabel}
        </Button>
      </div>
    </form>
  )
}
