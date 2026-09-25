import { useLocale } from '../../../../i18n/localeContext'
import { formatDate as formatIsoDate } from '../../../../utils/dates'
import { AuditHistoryPanel } from '../../../auditing/components/AuditHistoryPanel'
import { dossierLifecycleFacts } from '../../dossierDisplay'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { DossierNotesPanel } from '../../notes/DossierNotesPanel'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierMoreSection } from '../DossierMoreSection'

/**
 * Historiek workspace: user notes (left) next to the automatic audit trail (right, permission-gated
 * inside the panel) — two separate sources that are never mixed — plus description, lifecycle
 * facts and the compat block with financials, related dossiers and incidents.
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
        <div>
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
            // The Overzicht card shows the dossier's note count: re-read it after every change.
            onChanged={ws.reloadDossier}
          />
          {dossier.description && (
            <>
              <h3>{t('dossiers.overview.historyDescription')}</h3>
              <p>{dossier.description}</p>
            </>
          )}
          <p className="placeholder-text">
            {t('dossiers.detail.createdAt', { date: formatIsoDate(dossier.createdAt) })}
            {dossier.responsibleName && <> · {t('dossiers.detail.responsible', { name: dossier.responsibleName })}</>}
            {lifecycle.facts.length > 0 && <> · {lifecycle.facts.join(' · ')}</>}
          </p>
          {lifecycle.reason && <p className="placeholder-text">{lifecycle.reason}</p>}
        </div>
        <div>
          <h3>{t('dossiers.overview.historyAudit')}</h3>
          <AuditHistoryPanel entityType="TransportDossier" entityId={dossier.id} />
        </div>
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
