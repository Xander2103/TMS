import { useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { updateTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { GoodsSection } from '../../transport-orders/components/sections/GoodsSection'
import { useOrderFormData } from '../../transport-orders/components/sections/useOrderFormData'
import { buildSubmitPayload } from '../../transport-orders/components/sections/orderFormPayload'
import {
  cargoFromOrder,
  cargoRowFromHeader,
  computeCargoSummary,
  emptyCargoRow,
  fieldErrorMap,
  stopsFromOrder,
  validateOrderForm,
  type CargoFormRow,
} from '../../transport-orders/components/sections/orderFormState'
import { applyUnitToCargoRow, orderValuesFromDetail } from './orderDrawerState'
import { SectionDrawer } from './SectionDrawer'
import '../../transport-orders/components/transport-order-form.css'

interface GoodsDrawerProps {
  order: TransportOrderDetail
  onClose: () => void
  /** Receives the fresh order detail after a successful save. */
  onSaved: (order: TransportOrderDetail) => void
}

/** §11 goods drawer: hosts the Phase-6 GoodsSection against ONE linked order (see RouteDrawer). */
export function GoodsDrawer({ order, onClose, onSaved }: GoodsDrawerProps) {
  const [baseOrder, setBaseOrder] = useState(order)
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => cargoFromOrder(order))
  const [goodsDescription, setGoodsDescription] = useState(order.goodsDescription ?? '')
  const [quantity, setQuantity] = useState(order.quantity == null ? '' : String(order.quantity))
  const [quantityUnitCode, setQuantityUnitCode] = useState<string | null>(order.quantityUnitCode)
  const [weightKg, setWeightKg] = useState(order.weightKg == null ? '' : String(order.weightKg))
  const [volumeM3, setVolumeM3] = useState(order.volumeM3 == null ? '' : String(order.volumeM3))
  const [palletCount, setPalletCount] = useState(order.palletCount == null ? '' : String(order.palletCount))
  const [distanceKm, setDistanceKm] = useState(order.distanceKm == null ? '' : String(order.distanceKm))
  const [loadingMeters, setLoadingMeters] = useState(order.loadingMeters == null ? '' : String(order.loadingMeters))
  const [adrRequired, setAdrRequired] = useState(order.adrRequired)
  const [craneRequired, setCraneRequired] = useState(order.craneRequired)
  const [plateauRequired, setPlateauRequired] = useState(order.plateauRequired ?? false)
  const [moffettRequired, setMoffettRequired] = useState(order.moffettRequired ?? false)
  const [isReturnMovement, setIsReturnMovement] = useState(order.isReturnMovement ?? false)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [conflict, setConflict] = useState<TransportOrderDetail | null>(null)

  const stops = stopsFromOrder(baseOrder)
  const { options: unitOptions } = useLookupOptions('/api/unit-types')
  const { unitMaster, serviceOptions, customerConfig } = useOrderFormData(baseOrder.customerId, stops)
  const preferredUnits = customerConfig?.preferredUnits ?? []

  const touch = <T,>(setter: (value: T) => void) => (value: T) => {
    setter(value)
    setDirty(true)
  }

  function setCargo(key: string, patch: Partial<CargoFormRow>) {
    setCargoItems((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
    setDirty(true)
  }

  const masterByCode = (code: string | null) =>
    code ? unitMaster.find((u) => u.code.toUpperCase() === code.toUpperCase()) ?? null : null

  function applyCargoUnit(key: string, code: string | null) {
    setCargoItems((rows) => rows.map((row) => (row.key === key ? applyUnitToCargoRow(row, code, masterByCode(code)) : row)))
    setDirty(true)
  }

  const derivedFromCargo = cargoItems.length > 0
  const cargoSummary = derivedFromCargo ? computeCargoSummary(cargoItems, unitOptions) : null

  function currentValues() {
    return {
      ...orderValuesFromDetail(baseOrder, serviceOptions),
      cargoItems,
      goodsDescription,
      quantity,
      quantityUnitCode,
      weightKg,
      volumeM3,
      palletCount,
      distanceKm,
      loadingMeters,
      adrRequired,
      craneRequired,
      plateauRequired,
      moffettRequired,
      isReturnMovement,
    }
  }

  async function save() {
    const values = currentValues()
    const goodsErrors = validateOrderForm(values).filter((e) => e.section === 'goederen')
    setErrors(fieldErrorMap(goodsErrors))
    if (goodsErrors.length > 0) {
      setError('Controleer de gemarkeerde goederenlijnen.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const updated = await updateTransportOrder(baseOrder.id, buildSubmitPayload(values))
      onSaved(updated)
      onClose()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && err.body && typeof err.body === 'object' && 'stops' in err.body) {
        setConflict(err.body as TransportOrderDetail)
      } else {
        setError(err instanceof Error ? err.message : 'De goederen konden niet worden opgeslagen.')
      }
    } finally {
      setSaving(false)
    }
  }

  function reloadFromConflict() {
    if (!conflict) return
    setBaseOrder(conflict)
    setCargoItems(cargoFromOrder(conflict))
    setGoodsDescription(conflict.goodsDescription ?? '')
    setQuantity(conflict.quantity == null ? '' : String(conflict.quantity))
    setQuantityUnitCode(conflict.quantityUnitCode)
    setWeightKg(conflict.weightKg == null ? '' : String(conflict.weightKg))
    setVolumeM3(conflict.volumeM3 == null ? '' : String(conflict.volumeM3))
    setPalletCount(conflict.palletCount == null ? '' : String(conflict.palletCount))
    setDistanceKm(conflict.distanceKm == null ? '' : String(conflict.distanceKm))
    setLoadingMeters(conflict.loadingMeters == null ? '' : String(conflict.loadingMeters))
    setAdrRequired(conflict.adrRequired)
    setCraneRequired(conflict.craneRequired)
    setPlateauRequired(conflict.plateauRequired ?? false)
    setMoffettRequired(conflict.moffettRequired ?? false)
    setIsReturnMovement(conflict.isReturnMovement ?? false)
    setDirty(false)
    setConflict(null)
    setError(null)
  }

  return (
    <SectionDrawer title={`Goederen — ${baseOrder.orderNumber}`} dirty={dirty} busy={saving} onClose={onClose} onSave={() => void save()}>
      {conflict && (
        <div className="dossier-conflict-banner" role="alert">
          <span>⚠ Deze opdracht is intussen gewijzigd door een collega. Uw wijzigingen zijn niet opgeslagen.</span>
          <Button variant="secondary" onClick={reloadFromConflict}>
            Herladen
          </Button>
        </div>
      )}
      <ValidationSummary message={error} />
      <div className="tof">
        <GoodsSection
          goodsDescription={goodsDescription}
          setGoodsDescription={touch(setGoodsDescription)}
          quantity={quantity}
          setQuantity={touch(setQuantity)}
          quantityUnit={baseOrder.quantityUnit ?? ''}
          quantityUnitCode={quantityUnitCode}
          setQuantityUnitCode={touch(setQuantityUnitCode)}
          weightKg={weightKg}
          setWeightKg={touch(setWeightKg)}
          volumeM3={volumeM3}
          setVolumeM3={touch(setVolumeM3)}
          palletCount={palletCount}
          setPalletCount={touch(setPalletCount)}
          distanceKm={distanceKm}
          setDistanceKm={touch(setDistanceKm)}
          loadingMeters={loadingMeters}
          setLoadingMeters={touch(setLoadingMeters)}
          adrRequired={adrRequired}
          setAdrRequired={touch(setAdrRequired)}
          craneRequired={craneRequired}
          setCraneRequired={touch(setCraneRequired)}
          plateauRequired={plateauRequired}
          setPlateauRequired={touch(setPlateauRequired)}
          moffettRequired={moffettRequired}
          setMoffettRequired={touch(setMoffettRequired)}
          isReturnMovement={isReturnMovement}
          setIsReturnMovement={touch(setIsReturnMovement)}
          derivedFromCargo={derivedFromCargo}
          cargoSummary={cargoSummary}
          cargoItems={cargoItems}
          stops={stops}
          unitOptions={unitOptions}
          preferredUnits={preferredUnits}
          setCargo={setCargo}
          onAddCargoRow={() => {
            setCargoItems((rows) => [...rows, emptyCargoRow()])
            setDirty(true)
          }}
          onAddCargoRowFromHeader={() => {
            setCargoItems((rows) => [
              ...rows,
              cargoRowFromHeader({ quantity, quantityUnit: baseOrder.quantityUnit ?? '', quantityUnitCode, weightKg, volumeM3, palletCount }),
            ])
            setDirty(true)
          }}
          onRemoveCargoRow={(key) => {
            setCargoItems((rows) => rows.filter((row) => row.key !== key))
            setDirty(true)
          }}
          applyCargoUnit={applyCargoUnit}
          cargoDimensionsFixed={(cargo) => masterByCode(cargo.quantityUnitCode)?.dimensionBehavior === 'Fixed'}
          saving={saving}
          errors={errors}
        />
      </div>
    </SectionDrawer>
  )
}
