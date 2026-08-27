import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'
import { ActivityCard } from './ActivityCard'

interface ActivityListProps {
  activities: DossierActivity[]
  canManage: boolean
  onOpen: (activity: DossierActivity) => void
  onAdd: () => void
}

/** §11 Activiteiten section body: ordered cards + the one add action. */
export function ActivityList({ activities, canManage, onOpen, onAdd }: ActivityListProps) {
  const { t } = useLocale()
  const ordered = [...activities].sort((a, b) => a.sequence - b.sequence)
  return (
    <>
      {ordered.length === 0 && <p className="placeholder-text">{t('dossiers.activities.empty')}</p>}
      {ordered.length > 0 && (
        <ul className="dossier-activity-list">
          {ordered.map((activity) => (
            <ActivityCard key={activity.id} activity={activity} activities={ordered} onOpen={onOpen} />
          ))}
        </ul>
      )}
      {canManage && (
        <p>
          <Button variant="secondary" onClick={onAdd}>
            {t('dossiers.activities.add')}
          </Button>
        </p>
      )}
    </>
  )
}
