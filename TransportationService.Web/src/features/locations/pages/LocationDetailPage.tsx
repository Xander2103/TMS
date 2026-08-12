import { useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { deleteLocation, duplicateLocation, getLocation, setLocationActive, updateLocation } from '../api/locationsApi'
import { LocationForm } from '../components/LocationForm'
import { OPENING_DAYS, OPENING_DAY_LABELS } from '../openingHours'
import { LOCATION_TYPE_LABELS, type LocationDetail, type LocationInput } from '../types'
import './locations.css'
import './location-form.css'

/** Full detail → form payload; every field round-trips so saving never clears untouched data. */
function toInput(location: LocationDetail): LocationInput {
  return {
    code: location.code,
    name: location.name,
    type: location.type,
    street: location.street,
    houseNumber: location.houseNumber,
    postalCode: location.postalCode,
    city: location.city,
    countryCode: location.countryCode,
    latitude: location.latitude,
    longitude: location.longitude,
    contactName: location.contactName,
    contactPhone: location.contactPhone,
    contactMobile: location.contactMobile,
    contactEmail: location.contactEmail,
    customerContactId: location.customerContactId,
    externalReference: location.externalReference,
    openingHours: location.openingHours,
    // null would clear the intervals server-side, so the form always carries the full list.
    openingIntervals: location.openingIntervals ?? [],
    loadingInstructions: location.loadingInstructions,
    unloadingInstructions: location.unloadingInstructions,
    accessInstructions: location.accessInstructions,
    accessRestrictions: location.accessRestrictions,
    vehicleRestrictions: location.vehicleRestrictions,
    trailerRestrictions: location.trailerRestrictions,
    alfapassRequired: location.alfapassRequired,
    appointmentRequired: location.appointmentRequired,
    gate: location.gate,
    accessCode: location.accessCode ?? null,
    receptionPoint: location.receptionPoint,
    dock: location.dock,
    routeDescription: location.routeDescription,
    deliveryByAppointmentOnly: location.deliveryByAppointmentOnly,
    heightRestrictionMeters: location.heightRestrictionMeters,
    weightRestrictionTons: location.weightRestrictionTons,
    adrAllowed: location.adrAllowed,
    craneRequired: location.craneRequired,
    forkliftAvailable: location.forkliftAvailable,
    driverInstructions: location.driverInstructions,
    internalMemo: location.internalMemo,
    defaultLoadingMinutes: location.defaultLoadingMinutes,
    defaultUnloadingMinutes: location.defaultUnloadingMinutes,
    preferredArrivalFrom: location.preferredArrivalFrom,
    preferredArrivalTo: location.preferredArrivalTo,
    earliestArrival: location.earliestArrival,
    latestArrival: location.latestArrival,
    isActive: location.isActive,
    customerId: location.customerId,
    notes: location.notes,
    // Carried through so saving an edit never silently clears the customer defaults.
    isDefaultLoadingLocation: location.isDefaultLoadingLocation,
    isDefaultUnloadingLocation: location.isDefaultUnloadingLocation,
    isDefaultBillingLocation: location.isDefaultBillingLocation,
  }
}

/** One card of the detail grid. */
function DetailCard({
  title,
  className,
  children,
}: {
  title: string
  className?: string
  children: ReactNode
}) {
  return (
    <section className={['location-card', className].filter(Boolean).join(' ')} aria-label={title}>
      <h3 className="location-card-title">{title}</h3>
      <div className="location-card-body">{children}</div>
    </section>
  )
}

function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="location-card-row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

const NO_INFO = <p className="location-card-empty">Geen informatie</p>

/**
 * Location detail as a desktop-friendly card grid (adres / contact / openingsuren /
 * planning / operationeel / instructies / interne info) instead of one long vertical list.
 * Header actions: Bewerken, Dupliceren, Deactiveren/Activeren (nooit verwijderen als
 * standaardpad — de aparte Verwijderen-knop blijft permission-gated bestaan).
 */
export function LocationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [location, setLocation] = useState<LocationDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmDeactivate, setConfirmDeactivate] = useState(false)
  const [duplicating, setDuplicating] = useState(false)
  const [togglingActive, setTogglingActive] = useState(false)

  useEffect(() => {
    if (!id) return
    let mounted = true
    getLocation(id)
      .then((result) => {
        if (!mounted) return
        setLocation(result)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Locatie kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  const loading = location === null && loadError === null
  const canEdit = hasPermission('locations.edit')

  async function saveEdit(value: LocationInput) {
    if (!id) return
    setSaving(true)
    setSaveError(null)
    try {
      const updated = await updateLocation(id, value)
      setLocation(updated)
      setEditing(false)
      showSuccess('Locatie bijgewerkt.')
    } catch (err) {
      setSaveError(
        err instanceof ApiError && err.status === 409
          ? 'Er bestaat al een locatie met deze code.'
          : 'Wijzigingen konden niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  async function handleDuplicate() {
    if (!id) return
    setDuplicating(true)
    try {
      const copy = await duplicateLocation(id)
      showSuccess('Locatie gedupliceerd.')
      navigate(`/locations/${copy.id}`)
      // The route param changes, so the effect reloads the copy; reset local view state.
      setLocation(null)
      setEditing(false)
    } catch {
      showError('Locatie kon niet worden gedupliceerd.')
    } finally {
      setDuplicating(false)
    }
  }

  async function handleSetActive(next: boolean) {
    if (!id || !location) return
    setTogglingActive(true)
    try {
      await setLocationActive(id, next)
      setLocation({ ...location, isActive: next })
      showSuccess(next ? 'Locatie geactiveerd.' : 'Locatie gedeactiveerd.')
    } catch {
      showError('Status kon niet worden gewijzigd.')
    } finally {
      setTogglingActive(false)
      setConfirmDeactivate(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteLocation(id)
      showSuccess('Locatie verwijderd.')
      navigate('/locations')
    } catch {
      showError('Locatie kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">Locatie laden…</p>
  if (loadError || !location) return <p className="placeholder-text">{loadError ?? 'Niet gevonden.'}</p>

  // ---- Card contents ----------------------------------------------------------------------

  const addressLine1 = [location.street, location.houseNumber].filter(Boolean).join(' ')
  const addressLine2 = [location.postalCode, location.city].filter(Boolean).join(' ')
  const hasAddress = Boolean(addressLine1 || addressLine2 || location.countryCode)
  const hasCoordinates = location.latitude != null && location.longitude != null

  const hasContact = Boolean(location.contactName || location.contactPhone || location.contactMobile || location.contactEmail)

  const openingIntervals = location.openingIntervals ?? []
  const hasStructuredHours = openingIntervals.length > 0

  const hasPlanning =
    location.defaultLoadingMinutes != null ||
    location.defaultUnloadingMinutes != null ||
    Boolean(location.preferredArrivalFrom || location.preferredArrivalTo || location.earliestArrival || location.latestArrival)

  // Operationeel: booleans render as a ✓-list of ACTIVE flags only — never ten "Nee" rows.
  const terreinFlags = [
    location.craneRequired ? 'Kraan vereist' : null,
    location.forkliftAvailable ? 'Heftruck beschikbaar' : null,
  ].filter((f): f is string => f !== null)
  const toegangFlags = [
    location.appointmentRequired ? 'Afspraak verplicht' : null,
    location.deliveryByAppointmentOnly ? 'Leveren enkel op afspraak' : null,
    location.alfapassRequired ? 'Alfapass vereist' : null,
  ].filter((f): f is string => f !== null)

  const hasTerrein = Boolean(location.gate || location.receptionPoint || location.dock || location.routeDescription) || terreinFlags.length > 0
  const hasToegang = Boolean(location.accessCode || location.accessRestrictions) || toegangFlags.length > 0
  const beperkingRows: { label: string; value: string }[] = [
    location.heightRestrictionMeters != null ? { label: 'Max. hoogte', value: `${location.heightRestrictionMeters} m` } : null,
    location.weightRestrictionTons != null ? { label: 'Max. gewicht', value: `${location.weightRestrictionTons} t` } : null,
    location.adrAllowed != null
      ? { label: 'ADR', value: location.adrAllowed ? 'Toegelaten' : 'Niet toegelaten' }
      : null,
    location.vehicleRestrictions ? { label: 'Voertuigen', value: location.vehicleRestrictions } : null,
    location.trailerRestrictions ? { label: 'Opleggers', value: location.trailerRestrictions } : null,
  ].filter((r): r is { label: string; value: string } => r !== null)
  const hasOperational = hasTerrein || hasToegang || beperkingRows.length > 0

  const instructionRows: { label: string; value: string }[] = [
    location.loadingInstructions ? { label: 'Laden', value: location.loadingInstructions } : null,
    location.unloadingInstructions ? { label: 'Lossen', value: location.unloadingInstructions } : null,
    location.accessInstructions ? { label: 'Toegang', value: location.accessInstructions } : null,
    location.driverInstructions ? { label: 'Chauffeur', value: location.driverInstructions } : null,
  ].filter((r): r is { label: string; value: string } => r !== null)

  const hasInternal = Boolean(location.internalMemo || location.notes)

  const flagList = (flags: string[]) => (
    <ul className="location-flag-list">
      {flags.map((flag) => (
        <li key={flag}>
          <span className="location-flag-check" aria-hidden="true">✓</span> <span>{flag}</span>
        </li>
      ))}
    </ul>
  )

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties', to: '/locations' }, { label: location.code }]} />
      <PageHeader
        title={location.name}
        subtitle={
          <span className="location-detail-subtitle">
            <code>{location.code}</code>
            <span aria-hidden="true"> • </span>
            <span>{LOCATION_TYPE_LABELS[location.type]}</span>
            {location.customerId && location.customerName && (
              <>
                <span aria-hidden="true"> • </span>
                <Link to={`/customers/${location.customerId}`}>{location.customerName}</Link>
              </>
            )}
            <span aria-hidden="true"> </span>
            {location.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
          </span>
        }
        action={
          <div className="location-detail-actions">
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => { setSaveError(null); setEditing(true) }}>
                Bewerken
              </Button>
            )}
            {hasPermission('locations.create') && !editing && (
              <Button variant="secondary" onClick={() => void handleDuplicate()} disabled={duplicating}>
                {duplicating ? 'Bezig…' : 'Dupliceren'}
              </Button>
            )}
            {canEdit && !editing && (
              location.isActive ? (
                <Button variant="secondary" onClick={() => setConfirmDeactivate(true)} disabled={togglingActive}>
                  Deactiveren
                </Button>
              ) : (
                <Button variant="secondary" onClick={() => void handleSetActive(true)} disabled={togglingActive}>
                  Activeren
                </Button>
              )
            )}
            {hasPermission('locations.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>Verwijderen</Button>
            )}
          </div>
        }
      />

      {!editing ? (
        <div className="location-cards">
          <DetailCard title="Adres">
            {hasAddress || hasCoordinates ? (
              <>
                <p className="location-card-address">
                  {addressLine1 && <span>{addressLine1}</span>}
                  {addressLine2 && <span>{addressLine2}</span>}
                  {location.countryCode && <span>{location.countryCode}</span>}
                </p>
                {hasCoordinates && (
                  <p className="location-card-muted">
                    {location.latitude}, {location.longitude}
                  </p>
                )}
                {location.externalReference && (
                  <p className="location-card-muted">Externe referentie: {location.externalReference}</p>
                )}
              </>
            ) : (
              NO_INFO
            )}
          </DetailCard>

          <DetailCard title="Contact">
            {hasContact ? (
              <dl>
                {location.contactName && <DetailRow label="Naam">{location.contactName}</DetailRow>}
                {location.contactPhone && (
                  <DetailRow label="Telefoon">
                    <a href={`tel:${location.contactPhone.replace(/\s/g, '')}`}>{location.contactPhone}</a>
                  </DetailRow>
                )}
                {location.contactMobile && (
                  <DetailRow label="Gsm">
                    <a href={`tel:${location.contactMobile.replace(/\s/g, '')}`}>{location.contactMobile}</a>
                  </DetailRow>
                )}
                {location.contactEmail && (
                  <DetailRow label="E-mail">
                    <a href={`mailto:${location.contactEmail}`}>{location.contactEmail}</a>
                  </DetailRow>
                )}
              </dl>
            ) : (
              NO_INFO
            )}
          </DetailCard>

          <DetailCard title="Openingsuren">
            {hasStructuredHours ? (
              <table className="location-hours-table">
                <tbody>
                  {OPENING_DAYS.map((day) => {
                    const windows = openingIntervals
                      .filter((i) => i.dayOfWeek === day)
                      .sort((a, b) => a.fromTime.localeCompare(b.fromTime))
                    return (
                      <tr key={day}>
                        <th scope="row">{OPENING_DAY_LABELS[day - 1]}</th>
                        <td>
                          {windows.length === 0 ? (
                            <span className="location-hours-closed">Gesloten</span>
                          ) : (
                            windows
                              .map((w) => `${w.fromTime}–${w.toTime}${w.note ? ` (${w.note})` : ''}`)
                              .join(', ')
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            ) : location.openingHours ? (
              <p>{location.openingHours}</p>
            ) : (
              NO_INFO
            )}
          </DetailCard>

          <DetailCard title="Planning">
            {hasPlanning ? (
              <dl>
                {location.defaultLoadingMinutes != null && (
                  <DetailRow label="Standaard laadtijd">{location.defaultLoadingMinutes} min</DetailRow>
                )}
                {location.defaultUnloadingMinutes != null && (
                  <DetailRow label="Standaard lostijd">{location.defaultUnloadingMinutes} min</DetailRow>
                )}
                {(location.preferredArrivalFrom || location.preferredArrivalTo) && (
                  <DetailRow label="Voorkeursvenster">
                    {location.preferredArrivalFrom ?? '…'}–{location.preferredArrivalTo ?? '…'}
                  </DetailRow>
                )}
                {location.earliestArrival && <DetailRow label="Vroegste aankomst">{location.earliestArrival}</DetailRow>}
                {location.latestArrival && <DetailRow label="Laatste aankomst">{location.latestArrival}</DetailRow>}
              </dl>
            ) : (
              NO_INFO
            )}
          </DetailCard>

          <DetailCard title="Operationeel" className="location-card-wide">
            {hasOperational ? (
              <div className="location-operational-groups">
                {hasTerrein && (
                  <div className="location-operational-group">
                    <h4>Terrein</h4>
                    <dl>
                      {location.gate && <DetailRow label="Poort">{location.gate}</DetailRow>}
                      {location.receptionPoint && <DetailRow label="Aanmeldpunt">{location.receptionPoint}</DetailRow>}
                      {location.dock && <DetailRow label="Kade/dok">{location.dock}</DetailRow>}
                      {location.routeDescription && <DetailRow label="Route">{location.routeDescription}</DetailRow>}
                    </dl>
                    {terreinFlags.length > 0 && flagList(terreinFlags)}
                  </div>
                )}
                {hasToegang && (
                  <div className="location-operational-group">
                    <h4>Toegang</h4>
                    <dl>
                      {location.accessCode && <DetailRow label="Toegangscode">{location.accessCode}</DetailRow>}
                      {location.accessRestrictions && (
                        <DetailRow label="Restricties">{location.accessRestrictions}</DetailRow>
                      )}
                    </dl>
                    {toegangFlags.length > 0 && flagList(toegangFlags)}
                  </div>
                )}
                {beperkingRows.length > 0 && (
                  <div className="location-operational-group">
                    <h4>Beperkingen</h4>
                    <dl>
                      {beperkingRows.map((row) => (
                        <DetailRow key={row.label} label={row.label}>
                          {row.value}
                        </DetailRow>
                      ))}
                    </dl>
                  </div>
                )}
              </div>
            ) : (
              <p className="location-card-empty">Geen bijzonderheden</p>
            )}
          </DetailCard>

          {instructionRows.length > 0 && (
            <DetailCard title="Instructies" className="location-card-wide">
              <dl>
                {instructionRows.map((row) => (
                  <DetailRow key={row.label} label={row.label}>
                    {row.value}
                  </DetailRow>
                ))}
              </dl>
            </DetailCard>
          )}

          {hasInternal && (
            <DetailCard title="Interne informatie" className="location-card-internal location-card-wide">
              <p className="location-card-internal-label">Alleen interne gebruikers</p>
              <dl>
                {location.internalMemo && <DetailRow label="Memo">{location.internalMemo}</DetailRow>}
                {location.notes && <DetailRow label="Notities">{location.notes}</DetailRow>}
              </dl>
            </DetailCard>
          )}
        </div>
      ) : (
        <LocationForm
          mode="edit"
          initial={toInput(location)}
          submitting={saving}
          error={saveError}
          onSubmit={(value) => void saveEdit(value)}
          onCancel={() => setEditing(false)}
        />
      )}

      {confirmDeactivate && (
        <ConfirmDialog
          title="Locatie deactiveren"
          message={`'${location.name}' deactiveren? De locatie blijft zichtbaar en behouden voor bestaande opdrachten, maar is niet meer kiesbaar voor nieuwe.`}
          confirmLabel="Deactiveren"
          busy={togglingActive}
          onConfirm={() => void handleSetActive(false)}
          onCancel={() => setConfirmDeactivate(false)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Locatie verwijderen"
          message={`Weet je zeker dat je locatie ${location.code} wilt verwijderen? Overweeg deactiveren: dan blijft de historiek behouden.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
