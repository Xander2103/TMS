import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'

/** Stabiele statuscode van CustomerPackageRowDto — de bron voor tone én weergave (§4). */
export type CustomerPackageStatusCode =
  | 'Preparing'
  | 'InTransit'
  | 'Delivered'
  | 'PartiallyDelivered'
  | 'RedeliveryPlanned'
  | 'Return'
  | 'Cancelled'
  | 'InProgress'

interface CustomerPackageRow {
  packageNumber: string
  description: string
  quantity: number
  unitType: string
  /** Legacy Nederlands displayveld; blijft in de API maar wordt niet meer gerenderd. */
  statusLabel: string
  statusCode: CustomerPackageStatusCode
}

interface CustomerPackageSummary {
  total: number
  delivered: number
  inTransit: number
  pending: number
  inHandling: number
  packages: CustomerPackageRow[]
}

/** Tone-map op de stabiele code — nooit op vertaalde/afgeleide tekst. */
const STATUS_TONE: Record<CustomerPackageStatusCode, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  Preparing: 'neutral',
  InTransit: 'info',
  Delivered: 'success',
  PartiallyDelivered: 'warning',
  RedeliveryPlanned: 'info',
  Return: 'warning',
  Cancelled: 'neutral',
  InProgress: 'warning',
}

/** Vertaalsleutels — renderen als t(STATUS_LABELS[code]). */
const STATUS_LABELS: Record<CustomerPackageStatusCode, string> = {
  Preparing: 'packages.customer.statusCode.Preparing',
  InTransit: 'packages.customer.statusCode.InTransit',
  Delivered: 'packages.customer.statusCode.Delivered',
  PartiallyDelivered: 'packages.customer.statusCode.PartiallyDelivered',
  RedeliveryPlanned: 'packages.customer.statusCode.RedeliveryPlanned',
  Return: 'packages.customer.statusCode.Return',
  Cancelled: 'packages.customer.statusCode.Cancelled',
  InProgress: 'packages.customer.statusCode.InProgress',
}

/**
 * Customer-facing package block: neutral status labels only. The server already redacts
 * (no barcodes, notes or incident details exist in this payload).
 */
export function CustomerPackagesSummary({ orderId }: { orderId: string }) {
  const { t } = useLocale()
  const [summary, setSummary] = useState<CustomerPackageSummary | null>(null)

  useEffect(() => {
    let mounted = true
    apiClient
      .getJson<CustomerPackageSummary>(`/api/transport-orders/${orderId}/package-summary`)
      .then((data) => {
        if (mounted) setSummary(data)
      })
      .catch(() => {
        // Block stays hidden without the endpoint (older orders, no permission).
      })
    return () => {
      mounted = false
    }
  }, [orderId])

  if (!summary || summary.total === 0) return null

  return (
    <section className="to-section">
      <h2>{t('packages.customer.title')}</h2>
      <p className="pk-customer-counts">
        {t('packages.customer.deliveredOfTotal', { delivered: summary.delivered, total: summary.total })}
        {summary.inTransit > 0 && ` · ${t('packages.customer.inTransit', { count: summary.inTransit })}`}
        {summary.pending > 0 && ` · ${t('packages.customer.pending', { count: summary.pending })}`}
        {summary.inHandling > 0 && ` · ${t('packages.customer.inHandling', { count: summary.inHandling })}`}
      </p>
      <ul className="pk-customer-list">
        {summary.packages.map((row) => (
          <li key={row.packageNumber}>
            <code>{row.packageNumber}</code>
            <span className="pk-customer-desc">{row.description}</span>
            <span className="pk-customer-qty">
              {row.quantity} {row.unitType}
            </span>
            <Badge tone={STATUS_TONE[row.statusCode] ?? 'neutral'}>
              {STATUS_LABELS[row.statusCode] ? t(STATUS_LABELS[row.statusCode]) : row.statusLabel}
            </Badge>
          </li>
        ))}
      </ul>
    </section>
  )
}
