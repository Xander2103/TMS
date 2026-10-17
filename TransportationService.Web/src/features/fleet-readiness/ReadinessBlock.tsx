import { useEffect, useState } from 'react'
import { Badge } from '../../components/ui/Badge'
import { useLocale } from '../../i18n/localeContext'
import { formatDate } from '../../utils/dates'
import { getAssetReadiness } from './fleetReadinessApi'
import type { AssetReadiness, FleetAssetKind } from './types'
import { displaySeverity, READINESS_LEVEL_LABELS, READINESS_LEVEL_TONES, READINESS_SEVERITY_ICONS } from './readiness'
import './fleet-readiness.css'

interface ReadinessBlockProps {
  kind: FleetAssetKind
  assetId: string
  /** Bump to re-fetch after an edit on the page (status change, maintenance, damage…). */
  reloadToken?: number
}

/**
 * FL-H1/H2/H4: "is this asset deployable today and if not, why?" — straight from the server's
 * fleet readiness verdict (the same one Planning uses), never re-derived in the browser.
 */
export function ReadinessBlock({ kind, assetId, reloadToken = 0 }: ReadinessBlockProps) {
  const { t } = useLocale()
  const [state, setState] = useState<{ data: AssetReadiness | null; failed: boolean; key: string }>({ data: null, failed: false, key: '' })
  const key = `${kind}:${assetId}:${reloadToken}`

  useEffect(() => {
    let mounted = true
    getAssetReadiness(kind, assetId)
      .then((data) => mounted && setState({ data, failed: false, key }))
      .catch(() => mounted && setState({ data: null, failed: true, key }))
    return () => {
      mounted = false
    }
  }, [kind, assetId, key])

  const current = state.key === key ? state : { data: null, failed: false }
  if (current.failed) return <p className="fleet-readiness-muted">{t('fleet.readiness.loadFailed')}</p>
  if (!current.data) return <p className="fleet-readiness-muted">{t('fleet.readiness.loading')}</p>

  const readiness = current.data
  return (
    <div className="fleet-readiness" data-testid="fleet-readiness">
      <Badge tone={READINESS_LEVEL_TONES[readiness.level]}>{t(READINESS_LEVEL_LABELS[readiness.level])}</Badge>
      {readiness.issues.length > 0 && (
        <ul className="fleet-readiness-issues">
          {readiness.issues.map((issue, index) => {
            const shown = displaySeverity(issue)
            return (
            <li key={`${issue.code}-${index}`} className={`fleet-readiness-issue fleet-readiness-${shown.toLowerCase()}`}>
              <span aria-hidden="true">{READINESS_SEVERITY_ICONS[shown]}</span> {issue.message}
              {(issue.from || issue.until) && (
                <span className="fleet-readiness-muted">
                  {' '}
                  ({issue.from ? formatDate(issue.from) : '…'}
                  {issue.until && issue.until !== issue.from ? ` – ${formatDate(issue.until)}` : ''})
                </span>
              )}
              {issue.conditional && <span className="fleet-readiness-muted"> — {t('fleet.readiness.conditional')}</span>}
            </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
