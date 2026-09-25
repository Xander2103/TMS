import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import type { CargoCommercialCoverage } from '../types'

const COVERAGE: Record<CargoCommercialCoverage, { label: string; tone: BadgeTone }> = {
  SeparatelyPriced: { label: 'transportOrders.coverageBadge.SeparatelyPriced', tone: 'success' },
  Included: { label: 'transportOrders.coverageBadge.Included', tone: 'info' },
  ToReview: { label: 'transportOrders.coverageBadge.ToReview', tone: 'warning' },
}

/**
 * Commercial coverage of ONE goods line (D4) — display only. The value is derived by the backend;
 * absent (older payload) or unrecognised means "unknown" and renders nothing, never a guess.
 */
export function CargoCoverageBadge({ coverage }: { coverage: CargoCommercialCoverage | null | undefined }) {
  const { t } = useLocale()
  const entry = coverage ? COVERAGE[coverage] : undefined
  if (!entry) return null
  return <Badge tone={entry.tone}>{t(entry.label)}</Badge>
}
