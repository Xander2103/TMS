import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useLocale } from '../../../../i18n/localeContext'
import type { DossierDocument } from '../../api/dossierDocumentsApi'
import { DossierDocumentsWorkspace } from '../../documents/DossierDocumentsWorkspace'
import { buildOrderLevels } from '../../documents/documentLevels'
import { useDossierWorkspace } from '../../dossierWorkspace'
import { useRegisterDossierSection } from '../../sectionRegistry'

/**
 * Documenten workspace (master sprint 2026-09-21, D6): one document model with two levels. General
 * documents hang on the dossier, order documents on one of its orders; the level switcher shows
 * "Dossier (algemeen)" first and one entry per linked order. `?opdracht={orderId}` opens on an order.
 */
export function DossierDocumentsSection() {
  const { t } = useLocale()
  const { dossier, activities, legacyOrders, firstOrder, applyDossier } = useDossierWorkspace()
  const register = useRegisterDossierSection('documenten')
  const [searchParams] = useSearchParams()

  const orderLevels = useMemo(() => buildOrderLevels(activities, legacyOrders), [activities, legacyOrders])
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
        orderLevels={orderLevels}
        initialLevel={searchParams.get('opdracht')}
        craneJobKinds={craneJobKinds}
        onDocumentsChange={handleDocumentsChange}
      />
    </section>
  )
}
