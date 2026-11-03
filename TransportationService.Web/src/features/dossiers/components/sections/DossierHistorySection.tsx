import { useLocale } from '../../../../i18n/localeContext'
import { formatDate as formatIsoDate } from '../../../../utils/dates'
import { dossierLifecycleFacts } from '../../dossierDisplay'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { DossierNotesPanel } from '../../notes/DossierNotesPanel'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierMoreSection } from '../DossierMoreSection'
import { DossierTimelinePanel } from '../DossierTimelinePanel'

/**
 * Historiek workspace: two equal cards with a fixed-height list — user notes next to the dossier's
 * reconstructed story (T-H6: orders, planning, prices, documents, incidents and lifecycle, readable
 * with dossiers.view), two separate sources that are never mixed. Below them description, lifecycle
 * facts and the block with financials, related dossiers and incidents.
 */
export function DossierHistorySection() {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('notities')
  const { dossier } = ws
  // Confirmed/cancelled facts (same wording as the header); nothing for an open dossier.
  const lifecycle = dossierLifecycleFacts(dossier, t)

  return (
    <section id="sectie-notities" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.notesTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.notesTitle')}</h2>
      <div className="dossier-history-grid">
        {/* Dossier-level notes only (activity notes live on their activity). The legacy free-text
            `dossier.notes` is migrated into a first note server-side; it is passed along solely as
            the read-only fallback for when the notes cannot be loaded. */}
        <DossierNotesPanel
          dossierId={dossier.id}
          activityId={null}
          // Same rule as the activity drawer: a closed dossier takes no new notes.
          canWrite={ws.canManage && ws.isOpen}
          title={t('dossiers.overview.historyNotes')}
          legacyText={dossier.notes}
          layout="workspace"
          // The Overzicht card shows the dossier's note count: re-read it after every change.
          onChanged={ws.reloadDossier}
        />
        {/* Keyed: another dossier starts without the search and filter of the previous one. */}
        <DossierTimelinePanel key={dossier.id} dossierId={dossier.id} title={t('dossiers.overview.historyAudit')} />
      </div>
      <div className="dossier-history-facts">
        {dossier.description && (
          <>
            <h3>{t('dossiers.overview.historyDescription')}</h3>
            <p className="dossier-history-description">{dossier.description}</p>
          </>
        )}
        <p>
          {t('dossiers.detail.createdAt', { date: formatIsoDate(dossier.createdAt) })}
          {dossier.responsibleName && <> · {t('dossiers.detail.responsible', { name: dossier.responsibleName })}</>}
          {lifecycle.facts.length > 0 && <> · {lifecycle.facts.join(' · ')}</>}
        </p>
        {lifecycle.reason && <p>{lifecycle.reason}</p>}
      </div>
      <DossierMoreSection
        dossier={dossier}
        canManage={ws.canManage}
        busy={ws.busy}
        onRemoveRelation={ws.removeRelation}
        defaultOpen
      />
    </section>
  )
}
