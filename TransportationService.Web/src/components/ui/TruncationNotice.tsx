import { useLocale } from '../../i18n/localeContext'
import './TruncationNotice.css'

interface TruncationNoticeProps {
  /** How many rows the screen received. */
  shown: number
  /**
   * The real total when the server reported one (`totalCount`); the notice then reads "Eerste
   * {shown} van {total} weergegeven — verfijn de zoekopdracht." Without it, pass `limit`.
   */
  total?: number | null
  /** The server's cap when it only says "cut at N" (`X-Result-Limit`, board `limit`). */
  limit?: number | null
  /** Replaces the generic text (e.g. the planning's "maximaal 500 ritten; verfijn de periode"). */
  message?: string
  /** Renders nothing unless this is true (default: `total > shown`, or `limit` given). */
  truncated?: boolean
  className?: string
}

/**
 * PERF-H3: a list that could not show everything says so. Lists used to request 200/500 rows
 * and silently drop the rest (customer #201 was simply unpickable); every screen that keeps a
 * client-side list now renders this notice next to it when the server had more.
 */
export function TruncationNotice({ shown, total, limit, message, truncated, className }: TruncationNoticeProps) {
  const { t } = useLocale()
  const isTruncated = truncated ?? (typeof total === 'number' ? total > shown : typeof limit === 'number')
  if (!isTruncated) return null

  const text =
    message ??
    (typeof total === 'number' && total > shown
      ? t('ui.truncation.partial', { shown, total })
      : t('ui.truncation.limit', { limit: limit ?? shown }))

  return (
    <p className={className ? `ui-truncation-notice ${className}` : 'ui-truncation-notice'} role="status">
      {text}
    </p>
  )
}
