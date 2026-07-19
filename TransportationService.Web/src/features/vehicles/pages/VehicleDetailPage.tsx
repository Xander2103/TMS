import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { FleetDocumentsPanel } from '../../fleet-documents/components/FleetDocumentsPanel'
import { MaintenancePanel } from '../../maintenance/components/MaintenancePanel'
import { DamagePanel } from '../../damage/components/DamagePanel'
import { FuelPanel } from '../../fuel/components/FuelPanel'
import { InspectionsPanel } from '../../inspections/components/InspectionsPanel'
import { AssignmentSlot } from '../../fleet-assignment/AssignmentSlot'
import { searchDrivers } from '../../drivers/api/driversApi'
import { deleteVehicle, getVehicle, setVehicleDriver, updateVehicle } from '../api/vehiclesApi'
import {
  FUEL_TYPE_LABELS,
  OPERATIONAL_STATUS_LABELS,
  OWNERSHIP_TYPE_LABELS,
  type VehicleDetail,
  type VehicleInput,
  type VehicleOperationalStatus,
} from '../types'
import './vehicle-form.css'

const STATUS_TONE: Record<VehicleOperationalStatus, BadgeTone> = {
  Active: 'success',
  InMaintenance: 'warning',
  OutOfService: 'danger',
  Decommissioned: 'neutral',
}

export function VehicleDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [vehicle, setVehicle] = useState<VehicleDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<VehicleInput | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (!id) return
    let mounted = true
    getVehicle(id)
      .then((result) => {
        if (!mounted) return
        setVehicle(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Voertuig kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  const loading = vehicle === null && loadError === null
  const canEdit = hasPermission('vehicles.edit')

  function startEdit() {
    if (!vehicle) return
    setForm({
      licensePlate: vehicle.licensePlate,
      vin: vehicle.vin,
      categoryId: vehicle.categoryId,
      brand: vehicle.brand,
      model: vehicle.model,
      year: vehicle.year,
      firstRegistrationDate: vehicle.firstRegistrationDate,
      fuelType: vehicle.fuelType,
      emissionClass: vehicle.emissionClass,
      grossVehicleWeightKg: vehicle.grossVehicleWeightKg,
      payloadKg: vehicle.payloadKg,
      lengthMeters: vehicle.lengthMeters,
      widthMeters: vehicle.widthMeters,
      heightMeters: vehicle.heightMeters,
      volumeM3: vehicle.volumeM3,
      odometerKm: vehicle.odometerKm,
      hasCrane: vehicle.hasCrane,
      hasRefrigeration: vehicle.hasRefrigeration,
      hasTailLift: vehicle.hasTailLift,
      adrSuitable: vehicle.adrSuitable,
      ownershipType: vehicle.ownershipType,
      operationalStatus: vehicle.operationalStatus,
      isActive: vehicle.isActive,
      notes: vehicle.notes,
    })
    setEditing(true)
  }

  function reloadVehicle() {
    if (!id) return
    getVehicle(id)
      .then(setVehicle)
      .catch(() => showError('Voertuig kon niet opnieuw worden geladen.'))
  }

  async function loadDriverOptions() {
    const drivers = await searchDrivers({ isActive: true, page: 1, pageSize: 200 })
    return drivers.items.map((d) => ({
      value: d.id,
      label: d.fullName,
      description: d.driverNumber,
      keywords: `${d.driverNumber} ${d.employeeNumber}`,
    }))
  }

  function set<K extends keyof VehicleInput>(key: K, value: VehicleInput[K]) {
    setForm((f) => (f ? { ...f, [key]: value } : f))
  }

  async function saveEdit() {
    if (!id || !form) return
    setSaving(true)
    try {
      const updated = await updateVehicle(id, form)
      setVehicle(updated)
      setEditing(false)
      showSuccess('Voertuig bijgewerkt.')
    } catch (err) {
      showError(err instanceof ApiError && err.status === 409 ? 'Er bestaat al een voertuig met dit kenteken.' : 'Wijzigingen konden niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteVehicle(id)
      showSuccess('Voertuig verwijderd.')
      navigate('/vehicles')
    } catch {
      showError('Voertuig kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Voertuig laden…</p>
  if (loadError || !vehicle) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Voertuigen', to: '/vehicles' }, { label: vehicle.internalNumber }]} />
      <PageHeader
        title={`${vehicle.brand ?? ''} ${vehicle.model ?? ''}`.trim() || vehicle.internalNumber}
        subtitle={`${vehicle.internalNumber} · ${vehicle.licensePlate}`}
        action={
          <div className="vehicle-detail-actions">
            {canEdit && !editing && <Button variant="secondary" onClick={startEdit}>Bewerken</Button>}
            {hasPermission('vehicles.delete') && <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>}
          </div>
        }
      />

      <div className="vehicle-detail-badges">
        <Badge tone={STATUS_TONE[vehicle.operationalStatus]}>{OPERATIONAL_STATUS_LABELS[vehicle.operationalStatus]}</Badge>
        {vehicle.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
        {vehicle.adrSuitable && <Badge tone="warning">ADR</Badge>}
        {vehicle.hasCrane && <Badge tone="info">Kraan</Badge>}
        {vehicle.hasRefrigeration && <Badge tone="info">Koeling</Badge>}
        {vehicle.hasTailLift && <Badge tone="info">Laadklep</Badge>}
      </div>

      {!editing ? (
        <div className="vehicle-detail-grid">
          <FormField label="Categorie">
            <span>{vehicle.categoryName ?? '—'}</span>
          </FormField>
          <FormField label="Eigendomsvorm">
            <span>{OWNERSHIP_TYPE_LABELS[vehicle.ownershipType]}</span>
          </FormField>
          <FormField label="Brandstof">
            <span>{FUEL_TYPE_LABELS[vehicle.fuelType]}</span>
          </FormField>
          <FormField label="Kilometerstand">
            <span>{vehicle.odometerKm.toLocaleString('nl-BE')} km</span>
          </FormField>
          <FormField label="VIN">
            <span>{vehicle.vin ?? '—'}</span>
          </FormField>
          <FormField label="Bouwjaar">
            <span>{vehicle.year ?? '—'}</span>
          </FormField>
          <FormField label="Notities" className="vehicle-detail-full">
            <span>{vehicle.notes ?? '—'}</span>
          </FormField>
        </div>
      ) : (
        form && (
          <form
            className="vehicle-form"
            onSubmit={(e) => {
              e.preventDefault()
              void saveEdit()
            }}
          >
            <section className="vehicle-form-card">
              <h2>Basisgegevens</h2>
              <div className="vehicle-form-grid">
                <FormField label="Kenteken" htmlFor="e-plate" required>
                  <input id="e-plate" value={form.licensePlate} onChange={(e) => set('licensePlate', e.target.value)} disabled={saving} />
                </FormField>
                <FormField label="Status" htmlFor="e-status">
                  <select id="e-status" value={form.operationalStatus} onChange={(e) => set('operationalStatus', e.target.value as VehicleOperationalStatus)} disabled={saving}>
                    {Object.entries(OPERATIONAL_STATUS_LABELS).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Kilometerstand" htmlFor="e-odometer">
                  <input id="e-odometer" type="number" min={0} value={form.odometerKm} onChange={(e) => set('odometerKm', Number(e.target.value) || 0)} disabled={saving} />
                </FormField>
                <FormField label="Actief" htmlFor="e-active">
                  <label className="vehicle-checkbox">
                    <input id="e-active" type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
                    <span>Voertuig is actief</span>
                  </label>
                </FormField>
              </div>
            </section>

            <div className="vehicle-form-actions">
              <Button type="button" variant="secondary" onClick={() => setEditing(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </div>
          </form>
        )
      )}

      {!editing && (
        <section className="vehicle-assignment">
          <h2>Toewijzing</h2>
          <p className="assignment-slots-note">
            <strong>Vaste chauffeur</strong> is een langetermijnvoorkeur; <strong>actuele chauffeur</strong> is de
            tijdelijke operationele toewijzing. Wijzigingen hier worden ook op de chauffeurspagina zichtbaar — het is
            dezelfde koppeling.
          </p>
          <div className="assignment-slots">
            <AssignmentSlot
              title="Vaste chauffeur"
              description="Rijdt standaard met dit voertuig."
              assigned={vehicle.fixedDriverId && vehicle.fixedDriverName ? { label: vehicle.fixedDriverName, linkTo: `/drivers/${vehicle.fixedDriverId}` } : null}
              canEdit={canEdit}
              pickerLabel="Chauffeur"
              loadOptions={loadDriverOptions}
              assign={async (driverId, replaceExisting) => {
                await setVehicleDriver(vehicle.id, 'fixed-driver', driverId, replaceExisting)
              }}
              onChanged={reloadVehicle}
            />
            <AssignmentSlot
              title="Actuele chauffeur"
              description="Tijdelijke operationele toewijzing."
              assigned={vehicle.currentDriverId && vehicle.currentDriverName ? { label: vehicle.currentDriverName, linkTo: `/drivers/${vehicle.currentDriverId}` } : null}
              canEdit={canEdit}
              pickerLabel="Chauffeur"
              loadOptions={loadDriverOptions}
              assign={async (driverId, replaceExisting) => {
                await setVehicleDriver(vehicle.id, 'current-driver', driverId, replaceExisting)
              }}
              onChanged={reloadVehicle}
            />
          </div>
        </section>
      )}

      {!editing && id && <FleetDocumentsPanel ownerType="vehicle" ownerId={id} />}
      {!editing && id && <MaintenancePanel ownerType="vehicle" ownerId={id} />}
      {!editing && id && <InspectionsPanel ownerType="vehicle" ownerId={id} />}
      {!editing && id && <DamagePanel ownerType="vehicle" ownerId={id} />}
      {!editing && id && <FuelPanel vehicleId={id} />}

      {confirmDelete && (
        <ConfirmDialog
          title="Voertuig verwijderen"
          message={`Weet je zeker dat je voertuig ${vehicle.internalNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
