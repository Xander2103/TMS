import { useEffect, useRef, useState } from 'react'
import type { UnitOptionItem } from '../UnitSelect'
import { getCustomer, searchCustomers } from '../../../customers/api/customersApi'
import type { CustomerDetail, CustomerListItem } from '../../../customers/types'
import { getLegalEntityOptions } from '../../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../../legal-entities/types'
import { getLocation } from '../../../locations/api/locationsApi'
import type { LocationOpeningInterval } from '../../../locations/types'
import { listWarehouses } from '../../../warehousing/api/warehousingApi'
import type { Warehouse } from '../../../warehousing/types'
import {
  getCustomerPricingConfig,
  listServiceOptions,
  listUnitTypeMaster,
  previewPrice,
  type CustomerPricingConfig,
  type PriceCalculationResult,
  type ServiceOption,
  type UnitTypeMaster,
} from '../../../tarification/api/pricingApi'
import { buildOneOffPreviewInput, buildServiceSelections } from './orderFormPayload'
import type { OrderFormValues, StopFormRow } from './orderFormState'

/**
 * Reference data the order form fetches once (or per selected customer/location): customers,
 * legal entities, service options, unit master, warehouses, the customer's pricing config +
 * intake requirements, and the structured opening hours per referenced stop location.
 * Extracted from `TransportOrderForm.tsx` (Wave 1 phase 6) — pure data loading, no form state.
 */
export function useOrderFormData(customerId: string, stops: StopFormRow[]) {
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [legalEntities, setLegalEntities] = useState<LegalEntityOption[]>([])
  const [serviceOptions, setServiceOptions] = useState<ServiceOption[]>([])
  const [unitMaster, setUnitMaster] = useState<UnitTypeMaster[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [pricingConfig, setPricingConfig] = useState<{ customerId: string; config: CustomerPricingConfig } | null>(null)
  const [loadedCustomerDetail, setLoadedCustomerDetail] = useState<{ id: string; detail: CustomerDetail } | null>(null)
  // Phase 7: structured opening hours per selected location (fetched lazily whenever a stop
  // references one) for the immediate, non-blocking client-side hint; the backend detail's
  // warnings stay authoritative after save.
  const [locationHours, setLocationHours] = useState<Record<string, LocationOpeningInterval[]>>({})
  const requestedHoursRef = useRef<Set<string>>(new Set())

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    // Facturerende entiteit is optioneel; bij laadfout of ontbrekende rechten blijft alleen "klantstandaard".
    getLegalEntityOptions()
      .then((data) => {
        if (mounted) setLegalEntities(data)
      })
      .catch(() => {})
    // Only active services that are selectable during order entry (spec §20).
    listServiceOptions(false, true)
      .then((data) => {
        if (mounted) setServiceOptions(data)
      })
      .catch(() => {})
    // Physical defaults for dimension autofill; unavailable (e.g. no permission) is fine.
    listUnitTypeMaster()
      .then((data) => {
        if (mounted) setUnitMaster(data)
      })
      .catch(() => {})
    // Warehouses map stop locations onto warehouse-conditioned services so the PREVIEW matches
    // what the save will charge; unavailable (no permission) degrades to the save-time result.
    listWarehouses()
      .then((data) => {
        if (mounted) setWarehouses(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getCustomerPricingConfig(customerId)
      .then((config) => {
        if (mounted) setPricingConfig({ customerId, config })
      })
      .catch(() => {})
    // Surface the selected customer's intake requirements (reference/PO/signed CMR) as hints.
    getCustomer(customerId)
      .then((detail) => {
        if (mounted) setLoadedCustomerDetail({ id: customerId, detail })
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [customerId])

  // Fetch each referenced location's structured hours once (initial load + on selection).
  useEffect(() => {
    for (const stop of stops) {
      const locationId = stop.locationId
      if (!locationId || requestedHoursRef.current.has(locationId)) continue
      requestedHoursRef.current.add(locationId)
      getLocation(locationId)
        .then((detail) => {
          setLocationHours((current) => ({ ...current, [locationId]: detail.openingIntervals ?? [] }))
        })
        .catch(() => {
          // Hint only — without hours there is simply no client-side warning.
        })
    }
  }, [stops])

  return {
    customers,
    legalEntities,
    serviceOptions,
    unitMaster,
    warehouses,
    locationHours,
    // Derive so a stale config/detail (from a previously selected customer) is never used.
    customerConfig: pricingConfig?.customerId === customerId ? pricingConfig.config : null,
    customerRequirements: loadedCustomerDetail?.id === customerId ? loadedCustomerDetail.detail : null,
  }
}

/**
 * Live price preview, debounced on the pricing-relevant inputs. Extracted verbatim from
 * `TransportOrderForm.tsx` (Wave 1 phase 6): mirrors the backend's save-time pricing so the
 * preview matches what the save will charge.
 */
export function useOrderPricePreview(
  values: OrderFormValues,
  unitOptions: UnitOptionItem[],
  touchedWarehouseIds: string[],
  effectiveWeightKg: number | null,
  effectivePalletCount: number | null,
): PriceCalculationResult | null {
  const [preview, setPreview] = useState<PriceCalculationResult | null>(null)
  const {
    customerId, orderDate, quantity, quantityUnitCode, weightKg, palletCount, adrRequired,
    selectedServiceOptionIds, serviceQuantities, servicePallets, serviceDays, stops, cargoItems,
    pricingSource, oneOffFixedAmount, oneOffTimeMode, oneOffIncludedLoadingMinutes,
    oneOffIncludedUnloadingMinutes, oneOffIncludedCombinedMinutes, oneOffExtraHourlyRate, oneOffNotes,
  } = values
  const lastUnloading = [...stops].reverse().find((s) => s.stopType === 'Unloading')
  const previewKey = JSON.stringify([
    customerId, orderDate, quantity, quantityUnitCode, weightKg, palletCount,
    lastUnloading?.postalCode, lastUnloading?.countryCode, selectedServiceOptionIds, serviceQuantities,
    servicePallets, serviceDays, touchedWarehouseIds,
    adrRequired, cargoItems.map((c) => [c.quantityUnitCode, c.expectedQuantity, c.totalWeightKg, c.palletCount]),
    stops.map((s) => [s.stopType, s.timeRequirement, s.timeReqFrom, s.timeReqTo, s.appointmentRequired, s.date]),
    pricingSource, oneOffFixedAmount, oneOffTimeMode, oneOffIncludedLoadingMinutes, oneOffIncludedUnloadingMinutes,
    oneOffIncludedCombinedMinutes, oneOffExtraHourlyRate, oneOffNotes,
  ])

  useEffect(() => {
    // Mirrors the backend: cargo lines with a managed unit drive the price (one line per unit,
    // quantities summed); the header pair only serves orders without coded lines.
    const codedCargo = cargoItems.filter(
      (c) => c.quantityUnitCode && c.expectedQuantity !== '' && Number(c.expectedQuantity) > 0,
    )
    let lines: Array<{ unitTypeId: string; quantity: number }> = []
    if (codedCargo.length > 0) {
      const byCode = new Map<string, number>()
      for (const c of codedCargo) {
        byCode.set(c.quantityUnitCode!, (byCode.get(c.quantityUnitCode!) ?? 0) + Number(c.expectedQuantity))
      }
      lines = [...byCode.entries()].flatMap(([code, qty]) => {
        const unitTypeId = unitOptions.find((u) => u.code === code)?.id
        return unitTypeId ? [{ unitTypeId, quantity: qty }] : []
      })
    } else {
      const unitTypeId = unitOptions.find((u) => u.code === quantityUnitCode)?.id ?? null
      lines = unitTypeId && quantity !== '' && Number(quantity) > 0
        ? [{ unitTypeId, quantity: Number(quantity) }]
        : []
    }
    const oneOff = buildOneOffPreviewInput(values)
    const shouldPreview = Boolean(customerId)
      && (lines.length > 0 || selectedServiceOptionIds.length > 0 || cargoItems.length > 0 || adrRequired || oneOff !== null)
    const timer = window.setTimeout(() => {
      if (!shouldPreview) {
        setPreview(null)
        return
      }
      previewPrice({
        customerId,
        date: orderDate || new Date().toISOString().slice(0, 10),
        lines,
        deliveryCountryCode: lastUnloading?.countryCode || null,
        deliveryPostalCode: lastUnloading?.postalCode || null,
        weightKg: effectiveWeightKg,
        distanceKm: null,
        palletCount: effectivePalletCount,
        serviceOptionIds: selectedServiceOptionIds,
        services: buildServiceSelections(values),
        adrRequired,
        cargoLineCount: cargoItems.length > 0 ? cargoItems.length : null,
        oneOff,
        warehouseIds: touchedWarehouseIds.length > 0 ? touchedWarehouseIds : null,
        // §16: stop time requirements drive time-based surcharges live in the preview.
        stopTimes: stops.map((s) => ({
          isUnloading: s.stopType === 'Unloading',
          requirementKind: s.timeRequirement || 'None',
          requirementFrom: s.timeReqFrom || null,
          requirementTo: s.timeReqTo || null,
          appointmentRequired: s.appointmentRequired,
          plannedDate: s.date || null,
        })),
      })
        .then(setPreview)
        .catch(() => setPreview(null))
    }, shouldPreview ? 400 : 0)
    return () => window.clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewKey, unitOptions.length])

  return preview
}
