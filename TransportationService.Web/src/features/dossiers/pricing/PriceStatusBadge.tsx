import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import type { ActivityPriceStatus } from './types'

const STATUS: Record<ActivityPriceStatus, { label: string; tone: BadgeTone }> = {
  NotPriced: { label: 'dossierPricing.status.NotPriced', tone: 'warning' },
  PartiallyPriced: { label: 'dossierPricing.status.PartiallyPriced', tone: 'warning' },
  Priced: { label: 'dossierPricing.status.Priced', tone: 'success' },
  Free: { label: 'dossierPricing.status.Free', tone: 'info' },
}

/**
 * The SERVER's commercial status of one activity (D5) — display only. Null (non-billable type),
 * absent (older payload) or unrecognised renders nothing: the client never derives a status, and
 * "Gratis" appears only when the server says Free.
 */
export function PriceStatusBadge({ status }: { status: ActivityPriceStatus | null | undefined }) {
  const { t } = useLocale()
  const entry = status ? STATUS[status] : undefined
  if (!entry) return null
  return <Badge tone={entry.tone}>{t(entry.label)}</Badge>
}
