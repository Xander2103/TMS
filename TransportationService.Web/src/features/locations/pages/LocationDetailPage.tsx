import { useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import { deleteLocation, duplicateLocation, getLocation, setLocationActive, updateLocation } from '../api/locationsApi'
import { LocationForm } from '../components/LocationForm'
import { OPENING_DAYS, OPENING_DAY_LABEL_KEYS } from '../openingHours'
import { LOCATION_TYPE_LABEL_KEYS, type LocationDetail, type LocationInput } from '../types'
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

/**
 * Location detail as a desktop-friendly card grid (adres / contact / openingsuren /
 * planning / operationeel / instructies / interne info) instead of one long vertical list.
 * Header actions: Bewerken, Dupliceren, Deactiveren/Activeren (nooit verwijderen als
 * standaardpad — de aparte Verwijderen-knop blijft permission-gated bestaan).
 */
export function LocationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
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
        if (mounted) setLoadError(t('locations.detail.loadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

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
      showSuccess(t('locations.detail.updated'))
    } catch (err) {
      setSaveError(
        err instanceof ApiError && err.status === 409
          ? t('locations.detail.duplicateCode')
          : t('locations.detail.saveFailed'),
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
      showSuccess(t('locations.detail.duplicated'))
      navigate(`/locations/${copy.id}`)
      // The route param changes, so the effect reloads the copy; reset local view state.
      setLocation(null)
      setEditing(false)
    } catch {
      showError(t('locations.detail.duplicateFailed'))
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
      showSuccess(next ? t('locations.detail.activated') : t('locations.detail.deactivated'))
    } catch {
      showError(t('locations.detail.statusChangeFailed'))
    } finally {
      setTogglingActive(false)
      setConfirmDeactivate(false)
    }
  }

  async function handleDelete() {
    if (!id) return
    try {
      await deleteLocation(id)
      showSuccess(t('locations.detail.deleted'))
      navigate('/locations')
    } catch {
      showError(t('locations.detail.deleteFailed'))
      setConfirmDelete(false)
    }
  }

  if (loading) return <p className="placeholder-text">{t('locations.detail.loading')}</p>
  if (loadError || !location) return <p className="placeholder-text">{loadError ?? t('locations.detail.notFound')}</p>

  const noInfo = <p className="location-card-empty">{t('locations.detail.noInfo')}</p>

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
    location.craneRequired ? t('locations.flags.craneRequired') : null,
    location.forkliftAvailable ? t('locations.flags.forkliftAvailable') : null,
  ].filter((f): f is string => f !== null)
  const toegangFlags = [
    location.appointmentRequired ? t('locations.flags.appointmentRequired') : null,
    location.deliveryByAppointmentOnly ? t('locations.flags.deliveryByAppointmentOnly') : null,
    location.alfapassRequired ? t('locations.flags.alfapassRequired') : null,
  ].filter((f): f is string => f !== null)

  const hasTerrein = Boolean(location.gate || location.receptionPoint || location.dock || location.routeDescription) || terreinFlags.length > 0
  const hasToegang = Boolean(location.accessCode || location.accessRestrictions) || toegangFlags.length > 0
  const beperkingRows: { label: string; value: string }[] = [
    location.heightRestrictionMeters != null
      ? { label: t('locations.detail.maxHeight'), value: t('locations.detail.heightValue', { value: location.heightRestrictionMeters }) }
      : null,
    location.weightRestrictionTons != null
      ? { label: t('locations.detail.maxWeight'), value: t('locations.detail.weightValue', { value: location.weightRestrictionTons }) }
      : null,
    location.adrAllowed != null
      ? { label: t('locations.detail.adr'), value: location.adrAllowed ? t('locations.detail.adrAllowed') : t('locations.detail.adrNotAllowed') }
      : null,
    location.vehicleRestrictions ? { label: t('locations.detail.vehicles'), value: location.vehicleRestrictions } : null,
    location.trailerRestrictions ? { label: t('locations.detail.trailers'), value: location.trailerRestrictions } : null,
  ].filter((r): r is { label: string; value: string } => r !== null)
  const hasOperational = hasTerrein || hasToegang || beperkingRows.length > 0

  const instructionRows: { label: string; value: string }[] = [
    location.loadingInstructions ? { label: t('locations.detail.instructionLoading'), value: location.loadingInstructions } : null,
    location.unloadingInstructions ? { label: t('locations.detail.instructionUnloading'), value: location.unloadingInstructions } : null,
    location.accessInstructions ? { label: t('locations.detail.instructionAccess'), value: location.accessInstructions } : null,
    location.driverInstructions ? { label: t('locations.detail.instructionDriver'), value: location.driverInstructions } : null,
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
      <Breadcrumbs items={[{ label: t('navigation.menu.locations'), to: '/locations' }, { label: location.code }]} />
      <PageHeader
        title={location.name}
        subtitle={
          <span className="location-detail-subtitle">
            <code>{location.code}</code>
            <span aria-hidden="true"> • </span>
            <span>{t(LOCATION_TYPE_LABEL_KEYS[location.type])}</span>
            {location.customerId && location.customerName && (
              <>
                <span aria-hidden="true"> • </span>
                <Link to={`/customers/${location.customerId}`}>{location.customerName}</Link>
              </>
            )}
            <span aria-hidden="true"> </span>
            {location.isActive ? (
              <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
            ) : (
              <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
            )}
          </span>
        }
        action={
          <div className="location-detail-actions">
            {canEdit && !editing && (
              <Button variant="secondary" onClick={() => { setSaveError(null); setEditing(true) }}>
                {t('ui.actions.edit')}
              </Button>
            )}
            {hasPermission('locations.create') && !editing && (
              <Button variant="secondary" onClick={() => void handleDuplicate()} disabled={duplicating}>
                {duplicating ? t('locations.detail.busy') : t('locations.detail.duplicate')}
              </Button>
            )}
            {canEdit && !editing && (
              location.isActive ? (
                <Button variant="secondary" onClick={() => setConfirmDeactivate(true)} disabled={togglingActive}>
                  {t('locations.detail.deactivate')}
                </Button>
              ) : (
                <Button variant="secondary" onClick={() => void handleSetActive(true)} disabled={togglingActive}>
                  {t('locations.detail.activate')}
                </Button>
              )
            )}
            {hasPermission('locations.delete') && !editing && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)}>{t('ui.actions.delete')}</Button>
            )}
          </div>
        }
      />

      {!editing ? (
        <div className="location-cards">
          <DetailCard title={t('locations.detail.cards.address')}>
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
                  <p className="location-card-muted">{t('locations.detail.externalReference', { value: location.externalReference })}</p>
                )}
              </>
            ) : (
              noInfo
            )}
          </DetailCard>

          <DetailCard title={t('locations.detail.cards.contact')}>
            {hasContact ? (
              <dl>
                {location.contactName && <DetailRow label={t('locations.detail.contactName')}>{location.contactName}</DetailRow>}
                {location.contactPhone && (
                  <DetailRow label={t('locations.detail.phone')}>
                    <a href={`tel:${location.contactPhone.replace(/\s/g, '')}`}>{location.contactPhone}</a>
                  </DetailRow>
                )}
                {location.contactMobile && (
                  <DetailRow label={t('locations.detail.mobile')}>
                    <a href={`tel:${location.contactMobile.replace(/\s/g, '')}`}>{location.contactMobile}</a>
                  </DetailRow>
                )}
                {location.contactEmail && (
                  <DetailRow label={t('locations.detail.email')}>
                    <a href={`mailto:${location.contactEmail}`}>{location.contactEmail}</a>
                  </DetailRow>
                )}
              </dl>
            ) : (
              noInfo
            )}
          </DetailCard>

          <DetailCard title={t('locations.detail.cards.openingHours')}>
            {hasStructuredHours ? (
              <table className="location-hours-table">
                <tbody>
                  {OPENING_DAYS.map((day) => {
                    const windows = openingIntervals
                      .filter((i) => i.dayOfWeek === day)
                      .sort((a, b) => a.fromTime.localeCompare(b.fromTime))
                    return (
                      <tr key={day}>
                        <th scope="row">{t(OPENING_DAY_LABEL_KEYS[day - 1])}</th>
                        <td>
                          {windows.length === 0 ? (
                            <span className="location-hours-closed">{t('locations.openingHours.closed')}</span>
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
              noInfo
            )}
          </DetailCard>

          <DetailCard title={t('locations.detail.cards.planning')}>
            {hasPlanning ? (
              <dl>
                {location.defaultLoadingMinutes != null && (
                  <DetailRow label={t('locations.detail.defaultLoadingTime')}>
                    {t('locations.detail.minutesValue', { minutes: location.defaultLoadingMinutes })}
                  </DetailRow>
                )}
                {location.defaultUnloadingMinutes != null && (
                  <DetailRow label={t('locations.detail.defaultUnloadingTime')}>
                    {t('locations.detail.minutesValue', { minutes: location.defaultUnloadingMinutes })}
                  </DetailRow>
                )}
                {(location.preferredArrivalFrom || location.preferredArrivalTo) && (
                  <DetailRow label={t('locations.detail.preferredWindow')}>
                    {location.preferredArrivalFrom ?? '…'}–{location.preferredArrivalTo ?? '…'}
                  </DetailRow>
                )}
                {location.earliestArrival && <DetailRow label={t('locations.detail.earliestArrival')}>{location.earliestArrival}</DetailRow>}
                {location.latestArrival && <DetailRow label={t('locations.detail.latestArrival')}>{location.latestArrival}</DetailRow>}
              </dl>
            ) : (
              noInfo
            )}
          </DetailCard>

          <DetailCard title={t('locations.detail.cards.operational')} className="location-card-wide">
            {hasOperational ? (
              <div className="location-operational-groups">
                {hasTerrein && (
                  <div className="location-operational-group">
                    <h4>{t('locations.detail.terrain')}</h4>
                    <dl>
                      {location.gate && <DetailRow label={t('locations.detail.gate')}>{location.gate}</DetailRow>}
                      {location.receptionPoint && <DetailRow label={t('locations.detail.receptionPoint')}>{location.receptionPoint}</DetailRow>}
                      {location.dock && <DetailRow label={t('locations.detail.dock')}>{location.dock}</DetailRow>}
                      {location.routeDescription && <DetailRow label={t('locations.detail.route')}>{location.routeDescription}</DetailRow>}
                    </dl>
                    {terreinFlags.length > 0 && flagList(terreinFlags)}
                  </div>
                )}
                {hasToegang && (
                  <div className="location-operational-group">
                    <h4>{t('locations.detail.access')}</h4>
                    <dl>
                      {location.accessCode && <DetailRow label={t('locations.detail.accessCode')}>{location.accessCode}</DetailRow>}
                      {location.accessRestrictions && (
                        <DetailRow label={t('locations.detail.restrictions')}>{location.accessRestrictions}</DetailRow>
                      )}
                    </dl>
                    {toegangFlags.length > 0 && flagList(toegangFlags)}
                  </div>
                )}
                {beperkingRows.length > 0 && (
                  <div className="location-operational-group">
                    <h4>{t('locations.detail.limits')}</h4>
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
              <p className="location-card-empty">{t('locations.detail.noParticulars')}</p>
            )}
          </DetailCard>

          {instructionRows.length > 0 && (
            <DetailCard title={t('locations.detail.cards.instructions')} className="location-card-wide">
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
            <DetailCard title={t('locations.detail.cards.internal')} className="location-card-internal location-card-wide">
              <p className="location-card-internal-label">{t('locations.internalOnly')}</p>
              <dl>
                {location.internalMemo && <DetailRow label={t('locations.detail.memo')}>{location.internalMemo}</DetailRow>}
                {location.notes && <DetailRow label={t('locations.detail.notes')}>{location.notes}</DetailRow>}
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
          title={t('locations.detail.deactivateTitle')}
          message={t('locations.detail.deactivateMessage', { name: location.name })}
          confirmLabel={t('locations.detail.deactivate')}
          busy={togglingActive}
          onConfirm={() => void handleSetActive(false)}
          onCancel={() => setConfirmDeactivate(false)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('locations.detail.deleteTitle')}
          message={t('locations.detail.deleteMessage', { code: location.code })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
