import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { listIncidents } from '../api/incidentsApi'
import type { IncidentListItem } from '../types'

interface RelatedIncidentsNoticeProps {
  transportOrderId?: string
  tripId?: string
}

/**
 * Transport acceptance M12: an order or trip page names the OPEN incidents about it, each as a
 * link — one compact line, nothing when there are none (or the user cannot see incidents).
 */
export function RelatedIncidentsNotice({ transportOrderId, tripId }: RelatedIncidentsNoticeProps) {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const canSee = hasPermission('incidents.view') || hasPermission('incidents.manage')
  const [incidents, setIncidents] = useState<IncidentListItem[]>([])

  useEffect(() => {
    if (!canSee || (!transportOrderId && !tripId)) return
    let active = true
    // One page of the OPEN incidents is all the notice names (the server filters and caps).
    listIncidents({ transportOrderId, tripId, openOnly: true, pageSize: 50 })
      .then((result) => {
        if (active) setIncidents(result.items.filter((i) => i.status === 'New' || i.status === 'InProgress'))
      })
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [canSee, transportOrderId, tripId])

  if (incidents.length === 0) return null
  return (
    <p className="related-incidents-notice" role="status">
      ⚠ {t('incidents.related.open', { count: incidents.length })}{' '}
      {incidents.map((incident, index) => (
        <span key={incident.id}>
          {index > 0 && ', '}
          <Link className="ui-id-link" to={`/incidents/${incident.id}`}>
            {incident.title}
          </Link>
        </span>
      ))}
    </p>
  )
}
