import { useLocale } from '../../../../i18n/localeContext'
import { formatDate as formatIsoDate } from '../../../../utils/dates'
import { AuditHistoryPanel } from '../../../auditing/components/AuditHistoryPanel'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'
import { DossierMoreSection } from '../DossierMoreSection'

/**
 * Historiek workspace: description + notes + lifecycle facts, the audit trail (permission-gated
 * inside the panel) and the compat block with financials, related dossiers and incidents.
 */
export function DossierHistorySection() {
  const { t } = useLocale()
  const ws = useDossierWorkspace()
  const register = useRegisterDossierSection('notities')
  const { dossier } = ws

  return (
    <section id="sectie-notities" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.notesTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.notesTitle')}</h2>
      <div className="dossier-history-grid">
        <div>
          <h3>{t('dossiers.overview.historyNotes')}</h3>
          {dossier.notes ? <p>{dossier.notes}</p> : <p className="placeholder-text">{t('dossiers.detail.noNotes')}</p>}
          {dossier.description && (
            <>
              <h3>{t('dossiers.overview.historyDescription')}</h3>
              <p>{dossier.description}</p>
            </>
          )}
          <p className="placeholder-text">
            {t('dossiers.detail.createdAt', { date: formatIsoDate(dossier.createdAt) })}
            {dossier.responsibleName && <> · {t('dossiers.detail.responsible', { name: dossier.responsibleName })}</>}
            {dossier.closedAt && <> · {t('dossiers.detail.closedAt', { date: formatIsoDate(dossier.closedAt) })}</>}
          </p>
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
