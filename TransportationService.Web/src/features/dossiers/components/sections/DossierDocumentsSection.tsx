import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useLocale } from '../../../../i18n/localeContext'
import type { DossierDocument } from '../../api/dossierDocumentsApi'
import { DossierDocumentsWorkspace } from '../../documents/DossierDocumentsWorkspace'
import { buildDocumentLevels } from '../../documents/documentLevels'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'

/**
 * Documenten workspace (master sprint 2026-09-21, D6; activity scope 2026-10-08): one document model
 * with three kinds of level. Dossier documents hang on the dossier, order documents on one of its
 * orders, activity documents on one of its standalone activities; the level switcher groups them as
 * Dossier / Opdrachten / Activiteiten. `?opdracht={orderId}` opens on an order, `?activiteit={id}`
 * on a standalone activity.
 */
export function DossierDocumentsSection() {
  const { t } = useLocale()
  const { dossier, activities, legacyOrders, firstOrder, applyDossier, reloadDossier } = useDossierWorkspace()
  const register = useRegisterDossierSection('documenten')
  const [searchParams] = useSearchParams()

  const levels = useMemo(() => buildDocumentLevels(activities, legacyOrders), [activities, legacyOrders])
  // Only the route target's order is loaded by the shell; for the others the document strategy decides.
  const craneJobKinds = useMemo(() => (firstOrder ? { [firstOrder.id]: firstOrder.craneJobKind } : undefined), [firstOrder])

  // The overview card counts UNIQUE documents; keep it honest after an upload/delete here without
  // refetching the dossier (no version change, nothing else touched).
  const handleDocumentsChange = useCallback(
    (documents: DossierDocument[]) => {
      if ((dossier.documentCount ?? 0) === documents.length) return
      applyDossier({
        ...dossier,
        documentCount: documents.length,
        documentTypes: [...new Set(documents.map((doc) => doc.documentType))],
      })
    },
    [dossier, applyDossier],
  )

  return (
    <section id="sectie-documenten" className="dossier-section dossier-workspace" aria-label={t('dossiers.detail.documentsTitle')} ref={register}>
      <h2 tabIndex={-1}>{t('dossiers.detail.documentsTitle')}</h2>
      <DossierDocumentsWorkspace
        key={dossier.id}
        dossierId={dossier.id}
        levels={levels}
        initialOrderId={searchParams.get('opdracht')}
        initialActivityId={searchParams.get('activiteit')}
        craneJobKinds={craneJobKinds}
        onDocumentsChange={handleDocumentsChange}
        // M10: an issued CMR changes the overview count and the activity "CMR / document" cell.
        onTransportDocumentIssued={reloadDossier}
      />
    </section>
  )
}
