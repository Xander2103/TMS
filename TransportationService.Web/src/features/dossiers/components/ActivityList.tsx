import type { Ref } from 'react'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'
import { ActivityCard } from './ActivityCard'

interface ActivityListProps {
  activities: DossierActivity[]
  canManage: boolean
  onOpen: (activity: DossierActivity) => void
  onAdd: () => void
  /** Focus target for "Ga naar activiteiten" (section registry). */
  addButtonRef?: Ref<HTMLButtonElement>
}

/** §11 Activiteiten section body: ordered cards + the one add action. */
export function ActivityList({ activities, canManage, onOpen, onAdd, addButtonRef }: ActivityListProps) {
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
          <Button ref={addButtonRef} variant="secondary" onClick={onAdd}>
            {t('dossiers.activities.add')}
          </Button>
        </p>
      )}
    </>
  )
}
