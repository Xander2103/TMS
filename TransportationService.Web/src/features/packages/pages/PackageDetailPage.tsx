import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  downloadLabelVersion,
  getPackage,
  getPackageBarcodes,
  getPackageTimeline,
  listLabelVersions,
} from '../api/packagesApi'
import {
  PACKAGE_EVENT_LABELS,
  PACKAGE_STATUS_LABELS,
  PACKAGE_STATUS_TONE,
  UNIT_TYPE_LABELS,
  type Package,
  type PackageBarcodeRow,
  type PackageLabelVersion,
  type PackageTimelineEvent,
} from '../types'
import { formatDateTime as formatDateTimeIso } from '../../../utils/dates'
import '../components/packages.css'

function formatDateTime(value: string | null): string {
  return value ? formatDateTimeIso(value) : '—'
}

/**
 * Package detail: identity, handling flags, the barcode register (old values keep their
 * history forever) and the append-only chain of custody. Nothing here is editable —
 * corrections happen through scans, incidents and dispositions that append new events.
 */
export function PackageDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { showError } = useToast()
  const { hasPermission } = useAuth()

  const [pkg, setPkg] = useState<Package | null>(null)
  const [timeline, setTimeline] = useState<PackageTimelineEvent[]>([])
  const [barcodes, setBarcodes] = useState<PackageBarcodeRow[]>([])
  const [labels, setLabels] = useState<PackageLabelVersion[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getPackage(id), getPackageTimeline(id), getPackageBarcodes(id)])
      .then(([detail, events, codes]) => {
        if (!mounted) return
        setPkg(detail)
        setTimeline(events)
        setBarcodes(codes)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Het colli kon niet worden geladen.')
      })
    listLabelVersions(id)
      .then((versions) => {
        if (mounted) setLabels(versions)
      })
      .catch(() => {
        // Label history is optional on this page.
      })
    return () => {
      mounted = false
    }
  }, [id])

  if (loadError) return <ErrorState message={loadError} />
  if (!pkg) return <LoadingState message="Colli laden..." />

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: 'Opdrachten', to: '/transport-orders' },
          { label: pkg.packageNumber },
        ]}
      />
      <PageHeader
        title={pkg.packageNumber}
        subtitle={pkg.description}
        action={
          <span className="pk-header-badges">
            <Badge tone={PACKAGE_STATUS_TONE[pkg.status]}>{PACKAGE_STATUS_LABELS[pkg.status]}</Badge>
            {pkg.exceptionState === 'Open' && <Badge tone="danger">Melding open</Badge>}
          </span>
        }
      />

      <section className="to-section">
        <h2>Gegevens</h2>
        <dl className="to-facts">
          <div>
            <dt>Opdracht</dt>
            <dd>
              <Link to={`/transport-orders/${pkg.transportOrderId}`}>Naar opdracht</Link>
            </dd>
          </div>
          <div>
            <dt>Aantal</dt>
            <dd>
              {pkg.quantity} {pkg.unitTypeLabel ?? UNIT_TYPE_LABELS[pkg.unitType]}
            </dd>
          </div>
          <div>
            <dt>Gewicht / volume</dt>
            <dd>
              {pkg.weightKg != null ? `${pkg.weightKg} kg` : '—'}
              {pkg.volumeM3 != null ? ` / ${pkg.volumeM3} m³` : ''}
            </dd>
          </div>
          <div>
            <dt>Kenmerken</dt>
            <dd>
              {[
                pkg.isMandatory ? null : 'optioneel',
                pkg.isFragile ? 'breekbaar' : null,
                pkg.requiresTemperatureControl ? 'temperatuur' : null,
                pkg.requiresSignature ? 'handtekening' : null,
                pkg.parentPackageId ? 'onderdeel van groep' : null,
              ]
                .filter(Boolean)
                .join(', ') || '—'}
            </dd>
          </div>
          <div>
            <dt>Klantreferentie</dt>
            <dd>{pkg.customerReference ?? '—'}</dd>
          </div>
          <div>
            <dt>Externe barcode</dt>
            <dd>{pkg.externalBarcode ? <code>{pkg.externalBarcode}</code> : '—'}</dd>
          </div>
        </dl>
      </section>

      <section className="to-section">
        <h2>Barcodes</h2>
        <ul className="pk-barcodes">
          {barcodes.map((row) => (
            <li key={row.id}>
              <code>{row.value}</code>
              {row.isActive ? (
                <Badge tone="success">Actief</Badge>
              ) : (
                <Badge tone="neutral">Vervangen</Badge>
              )}
              {row.retiredAt && (
                <span className="pk-barcode-retired">
                  vervangen {formatDateTime(row.retiredAt)}
                  {row.retireReason ? ` — ${row.retireReason}` : ''}
                </span>
              )}
            </li>
          ))}
        </ul>
      </section>

      {labels.length > 0 && hasPermission('packages.view') && (
        <section className="to-section">
          <h2>Etiketversies</h2>
          <ul className="pk-labels">
            {labels.map((label) => (
              <li key={label.id}>
                <span>
                  v{label.version} · {label.format} · {formatDateTime(label.printedAt)}
                  {label.reprintReason ? ` — ${label.reprintReason}` : ''}
                </span>
                <button
                  type="button"
                  className="scan-correct-link"
                  onClick={() => void downloadLabelVersion(id, label.version).catch(() => showError('Download mislukt.'))}
                >
                  Download PDF
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="to-section">
        <h2>Traceerbaarheid</h2>
        {timeline.length === 0 && <p>Nog geen gebeurtenissen.</p>}
        <ol className="pk-timeline">
          {timeline.map((event) => (
            <li key={event.id} className={event.isOverride ? 'pk-event-override' : ''}>
              <span className="pk-event-time">{formatDateTime(event.occurredAt)}</span>
              <span className="pk-event-body">
                <strong>{PACKAGE_EVENT_LABELS[event.eventType] ?? event.eventType}</strong>
                {event.oldStatus && event.newStatus && event.oldStatus !== event.newStatus && (
                  <>
                    {' '}
                    ({PACKAGE_STATUS_LABELS[event.oldStatus]} → {PACKAGE_STATUS_LABELS[event.newStatus]})
                  </>
                )}
                {event.isOverride && <Badge tone="warning">Vrijgave</Badge>}
                <span className="pk-event-meta">
                  {[
                    event.tripNumber ? `rit ${event.tripNumber}` : null,
                    event.stopLabel,
                    event.userName,
                    event.barcodeUsed ? `barcode ${event.barcodeUsed}` : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
                {event.notes && <span className="pk-event-notes">{event.notes}</span>}
                {event.exceptionId && (
                  <Link to={`/exceptions/${event.exceptionId}`} className="pk-event-link">
                    Naar melding
                  </Link>
                )}
              </span>
            </li>
          ))}
        </ol>
      </section>
    </div>
  )
}
