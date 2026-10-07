import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { dossierTabPath } from '../dossierSections'
import { DossierDocumentTable } from './DossierDocumentTable'
import { useDossierDocuments } from './useDossierDocuments'
import './dossier-documents.css'

/**
 * The dossier's general documents as seen from one of its ORDERS (D6): the same records as on
 * the dossier level, read-only here (view/download) — never copied, never managed from the
 * order. Needs the dossier read right; without it the block stays silent.
 */
export function DossierGeneralDocuments({ dossierId }: { dossierId: string }) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canView = hasPermission('dossiers.view') || hasPermission('dossiers.manage')
  const { documents, loadFailed, reload } = useDossierDocuments(dossierId, canView)

  if (!canView) return null
  const general = (documents ?? []).filter((document) => document.scope === 'Dossier')

  return (
    <section className="ddoc-general" aria-label={t('dossierDocuments.sections.general')}>
      <h3>{t('dossierDocuments.sections.general')}</h3>
      <p className="ddoc-hint">
        {t('dossierDocuments.sections.generalHint')}{' '}
        <Link to={dossierTabPath(dossierId, 'documenten')}>{t('dossierDocuments.sections.manageViaDossier')}</Link>
      </p>
      {loadFailed && (
        <p className="ddoc-error" role="alert">
          {t('dossierDocuments.loadFailed')}{' '}
          <button type="button" className="link-button" onClick={() => void reload()}>{t('dossierDocuments.retry')}</button>
        </p>
      )}
      {!loadFailed && documents === null && <p className="placeholder-text">{t('dossierDocuments.loading')}</p>}
      {documents && <DossierDocumentTable documents={general} emptyText={t('dossierDocuments.empty.general')} />}
    </section>
  )
}
