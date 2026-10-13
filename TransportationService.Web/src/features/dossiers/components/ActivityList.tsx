import type { Ref } from 'react'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity, DossierDetail } from '../types'
import { ActivityCard, type ActivityDetailFocus } from './ActivityCard'

interface ActivityListProps {
  activities: DossierActivity[]
  dossier: DossierDetail
  canManage: boolean
  onOpen: (activity: DossierActivity) => void
  onOpenDetail?: (activity: DossierActivity, focus: ActivityDetailFocus) => void
  onOpenDocuments?: (activity: DossierActivity) => void
  /** Card an attention jump points at. */
  highlightedActivityId?: string | null
  onAdd: () => void
  /** Focus target for "Ga naar activiteiten" (section registry). */
  addButtonRef?: Ref<HTMLButtonElement>
}

/** §11 Activiteiten section body: ordered cards + the one add action. */
export function ActivityList({
  activities, dossier, canManage, onOpen, onOpenDetail, onOpenDocuments, highlightedActivityId, onAdd, addButtonRef,
}: ActivityListProps) {
  const { t } = useLocale()
  const ordered = [...activities].sort((a, b) => a.sequence - b.sequence)
  return (
    <>
      {ordered.length === 0 && <p className="placeholder-text">{t('dossiers.activities.empty')}</p>}
      {ordered.length > 0 && (
        <ul className="dossier-activity-list">
          {ordered.map((activity) => (
            <ActivityCard
              key={activity.id}
              activity={activity}
              activities={ordered}
              dossier={dossier}
              onOpen={onOpen}
              onOpenDetail={onOpenDetail}
              onOpenDocuments={onOpenDocuments}
              highlighted={highlightedActivityId === activity.id}
            />
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
