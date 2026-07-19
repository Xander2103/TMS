import { useEffect, useState } from 'react'
import { apiClient } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'

interface CustomerPackageRow {
  packageNumber: string
  description: string
  quantity: number
  unitType: string
  statusLabel: string
}

interface CustomerPackageSummary {
  total: number
  delivered: number
  inTransit: number
  pending: number
  inHandling: number
  packages: CustomerPackageRow[]
}

const LABEL_TONE: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  'In voorbereiding': 'neutral',
  Onderweg: 'info',
  Geleverd: 'success',
  'Gedeeltelijk geleverd': 'warning',
  'Nieuwe levering gepland': 'info',
  Retour: 'warning',
  'In behandeling': 'warning',
  Geannuleerd: 'neutral',
}

/**
 * Customer-facing package block: neutral status labels only. The server already redacts
 * (no barcodes, notes or incident details exist in this payload).
 */
export function CustomerPackagesSummary({ orderId }: { orderId: string }) {
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
      <h2>Colli</h2>
      <p className="pk-customer-counts">
        {summary.delivered} van {summary.total} geleverd
        {summary.inTransit > 0 && ` · ${summary.inTransit} onderweg`}
        {summary.pending > 0 && ` · ${summary.pending} in voorbereiding`}
        {summary.inHandling > 0 && ` · ${summary.inHandling} in behandeling`}
      </p>
      <ul className="pk-customer-list">
        {summary.packages.map((row) => (
          <li key={row.packageNumber}>
            <code>{row.packageNumber}</code>
            <span className="pk-customer-desc">{row.description}</span>
            <span className="pk-customer-qty">
              {row.quantity} {row.unitType}
            </span>
            <Badge tone={LABEL_TONE[row.statusLabel] ?? 'neutral'}>{row.statusLabel}</Badge>
          </li>
        ))}
      </ul>
    </section>
  )
}
