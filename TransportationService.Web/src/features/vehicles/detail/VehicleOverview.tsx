import { Link } from 'react-router-dom'
import { AttentionList, type AttentionItem } from '../../../components/ui/AttentionList'
import { Badge } from '../../../components/ui/Badge'
import { DetailCard, DetailGrid } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { formatInteger } from '../../../utils/numbers'
import { fleetDocumentDisplayName, FLEET_DOCUMENT_TYPE_LABELS } from '../../fleet-documents/types'
import { inspectionDisplayName } from '../../inspections/types'
import { maintenanceDisplayName } from '../../maintenance/types'
import {
  EMISSION_CLASS_LABELS,
  FUEL_TYPE_LABELS,
  OPERATIONAL_STATUS_LABELS,
  OPERATIONAL_STATUS_TONES,
  OWNERSHIP_TYPE_LABELS,
  type VehicleDetail,
} from '../types'
import { buildUpcoming, buildVehicleAttention, type VehicleCompliance, type VehicleFocus } from './vehicleCompliance'

interface VehicleOverviewProps {
  vehicle: VehicleDetail
  compliance: VehicleCompliance
  complianceLoading: boolean
  canEdit: boolean
  /** Opens the grouped section that hosts a sub-block and scrolls to it. */
  onFocus: (focus: VehicleFocus) => void
  onOpenTechnical: () => void
}

/**
 * B16 overview: answers the fleet manager's questions before any click — is it available
 * (status card), is anything expiring/due/damaged/missing (attention strip, each line jumping
 * to the block that resolves it), who drives it (assignment), what is it (technical summary)
 * and what is coming (scheduled). Everything else lives one section away.
 */
export function VehicleOverview({ vehicle, compliance, complianceLoading, canEdit, onFocus, onOpenTechnical }: VehicleOverviewProps) {
  const { t } = useLocale()
  const labels = {
    document: (doc: Parameters<typeof fleetDocumentDisplayName>[0]) => t(fleetDocumentDisplayName(doc)),
    documentType: (type: keyof typeof FLEET_DOCUMENT_TYPE_LABELS) => t(FLEET_DOCUMENT_TYPE_LABELS[type]),
    inspection: (inspection: Parameters<typeof inspectionDisplayName>[0]) => t(inspectionDisplayName(inspection)),
    maintenance: (record: Parameters<typeof maintenanceDisplayName>[0]) => t(maintenanceDisplayName(record)),
  }

  const attention: AttentionItem[] = buildVehicleAttention(vehicle, compliance, labels).map((fact) => {
    const missing = fact.kind === 'equipmentDocumentMissing' || fact.kind === 'baseDocumentsMissing'
    return {
      key: fact.key,
      severity: fact.severity,
      text: t(`vehicles.detail.attention.${fact.kind}`, {
        label: fact.label ?? '',
        labels: (fact.labels ?? []).join(', '),
        date: fact.date ? formatDate(fact.date) : '',
        ...(fact.count !== undefined ? { count: fact.count } : {}),
      }),
      detail: fact.kind === 'maintenanceOverdue' && fact.date ? formatDate(fact.date) : undefined,
      action: { label: t(missing ? 'vehicles.detail.attention.add' : 'vehicles.detail.attention.view'), onSelect: () => onFocus(fact.focus) },
    }
  })

  const upcoming = buildUpcoming(compliance, labels)
  const equipment = [
    vehicle.adrSuitable ? t('fleet.common.equipment.adrShort') : null,
    vehicle.hasCrane ? t('fleet.common.equipment.crane') : null,
    vehicle.hasRefrigeration ? t('fleet.common.equipment.refrigeration') : null,
    vehicle.hasTailLift ? t('fleet.common.equipment.tailLift') : null,
  ].filter((item): item is string => item !== null)

  const driverLink = (id: string | null, name: string | null) =>
    id && name ? <Link to={`/drivers/${id}`}>{name}</Link> : <span className="vehicle-muted">{t('vehicles.detail.overview.notAssigned')}</span>

  return (
    <>
      <AttentionList items={attention} />
      <DetailGrid>
        <DetailCard title={t('vehicles.detail.overview.statusCard')}>
          <FactList
            facts={[
              {
                key: 'status',
                label: t('vehicles.detail.overview.operationalStatus'),
                value: (
                  <>
                    <Badge tone={OPERATIONAL_STATUS_TONES[vehicle.operationalStatus]}>{t(OPERATIONAL_STATUS_LABELS[vehicle.operationalStatus])}</Badge>
                    {vehicle.statusReason && <span className="vehicle-muted"> — {vehicle.statusReason}</span>}
                  </>
                ),
              },
              { key: 'admin', label: t('vehicles.detail.overview.administrative'), value: t(vehicle.isActive ? 'vehicles.detail.overview.active' : 'vehicles.detail.overview.inactive') },
              { key: 'category', label: t('fleet.form.category'), value: vehicle.categoryName },
              { key: 'ownership', label: t('fleet.form.ownership'), value: t(OWNERSHIP_TYPE_LABELS[vehicle.ownershipType]) },
              { key: 'odometer', label: t('vehicles.form.odometer'), value: `${formatInteger(vehicle.odometerKm)} km` },
            ]}
          />
        </DetailCard>

        <DetailCard
          title={t('vehicles.detail.overview.assignmentCard')}
          action={
            canEdit ? (
              <button type="button" className="vehicle-card-link" onClick={() => onFocus('toewijzing')}>
                {t('vehicles.detail.overview.change')}
              </button>
            ) : undefined
          }
        >
          <FactList
            facts={[
              { key: 'fixed', label: t('vehicles.detail.fixedDriver'), value: driverLink(vehicle.fixedDriverId, vehicle.fixedDriverName) },
              { key: 'current', label: t('vehicles.detail.currentDriver'), value: driverLink(vehicle.currentDriverId, vehicle.currentDriverName) },
            ]}
          />
        </DetailCard>

        <DetailCard
          title={t('vehicles.detail.overview.technicalCard')}
          action={
            <button type="button" className="vehicle-card-link" onClick={onOpenTechnical}>
              {t('vehicles.detail.overview.allDetails')}
            </button>
          }
        >
          <FactList
            facts={[
              {
                key: 'fuel',
                label: t('vehicles.detail.overview.fuelEmission'),
                value: [t(FUEL_TYPE_LABELS[vehicle.fuelType]), vehicle.emissionClass ? t(EMISSION_CLASS_LABELS[vehicle.emissionClass]) : null].filter(Boolean).join(' · '),
              },
              {
                key: 'weights',
                label: t('vehicles.detail.overview.weights'),
                value:
                  vehicle.grossVehicleWeightKg === null && vehicle.payloadKg === null
                    ? null
                    : `${vehicle.grossVehicleWeightKg !== null ? formatInteger(vehicle.grossVehicleWeightKg) : '—'} / ${vehicle.payloadKg !== null ? formatInteger(vehicle.payloadKg) : '—'} kg`,
              },
              { key: 'ldm', label: t('vehicles.detail.overview.loadingMeters'), value: vehicle.loadingMeters ? `${vehicle.loadingMeters} ldm` : null },
              {
                key: 'equipment',
                label: t('vehicles.detail.equipment'),
                value: equipment.length > 0 ? (
                  <span className="vehicle-equipment-badges">
                    {equipment.map((item) => (
                      <Badge key={item} tone="neutral">
                        {item}
                      </Badge>
                    ))}
                  </span>
                ) : (
                  <span className="vehicle-muted">{t('vehicles.detail.noEquipment')}</span>
                ),
              },
            ]}
          />
        </DetailCard>

        <DetailCard title={t('vehicles.detail.overview.upcomingCard')}>
          {complianceLoading ? (
            <p className="ui-detail-empty">{t('vehicles.detail.overview.upcomingLoading')}</p>
          ) : upcoming.length === 0 ? (
            <p className="ui-detail-empty">{t('vehicles.detail.overview.noUpcoming')}</p>
          ) : (
            <ul className="ui-detail-list">
              {upcoming.map((item) => (
                <li key={item.key}>
                  <button type="button" className="vehicle-upcoming-link ui-detail-list-main" onClick={() => onFocus(item.focus)}>
                    {item.label === 'tachograph' ? t('vehicles.detail.overview.tachograph') : item.label}
                  </button>
                  <span className="ui-detail-list-meta">{formatDate(item.date)}</span>
                </li>
              ))}
            </ul>
          )}
        </DetailCard>

        {vehicle.notes && (
          <DetailCard title={t('vehicles.detail.overview.notesCard')} className="ui-detail-card-wide">
            <p className="vehicle-notes">{vehicle.notes}</p>
          </DetailCard>
        )}
      </DetailGrid>
    </>
  )
}
