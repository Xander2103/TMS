import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { fetchMyQualificationDocumentUrl, listMyQualifications } from '../api/portalApi'
import {
  MY_QUALIFICATION_STATUS_LABELS,
  MY_QUALIFICATION_STATUS_TONE,
  type MyQualification,
} from '../types'
import './portal.css'

/** Own qualifications and their documents: licences, Code 95, ADR… with expiry status. */
export function PortalQualificationsPage() {
  const { showError } = useToast()
  const { t } = useLocale()
  const [qualifications, setQualifications] = useState<MyQualification[] | null>(null)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    let mounted = true
    listMyQualifications()
      .then((data) => {
        if (!mounted) return
        setQualifications(data)
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  async function openDocument(id: string) {
    try {
      const url = await fetchMyQualificationDocumentUrl(id)
      window.open(url, '_blank', 'noopener')
    } catch {
      showError(t('portalHome.qualifications.openFailed'))
    }
  }

  if (loadError) return <ErrorState message={t('portalHome.qualifications.loadFailed')} />
  if (!qualifications) return <LoadingState message={t('portalHome.qualifications.loading')} />

  return (
    <div>
      <PageHeader title={t('portalHome.qualifications.title')} subtitle={t('portalHome.qualifications.subtitle')} />

      {qualifications.length === 0 && <p className="portal-empty">{t('portalHome.qualifications.empty')}</p>}

      <ul className="portal-qualifications">
        {qualifications.map((qualification) => (
          <li key={qualification.id} className="portal-qualification">
            <div className="portal-absence-head">
              <strong>{qualification.qualificationTypeName}</strong>
              <Badge tone={MY_QUALIFICATION_STATUS_TONE[qualification.effectiveStatus]}>
                {t(MY_QUALIFICATION_STATUS_LABELS[qualification.effectiveStatus])}
              </Badge>
            </div>
            <div className="portal-absence-dates">
              {t('portalHome.qualifications.obtained', { date: qualification.obtainedDate })}
              {qualification.expiryDate && ` · ${t('portalHome.qualifications.expires', { date: qualification.expiryDate })}`}
              {qualification.documentNumber && ` · ${t('portalHome.qualifications.documentNumber', { number: qualification.documentNumber })}`}
            </div>
            {qualification.hasDocument && (
              <button type="button" className="portal-doc-link" onClick={() => void openDocument(qualification.id)}>
                📄 {t('portalHome.qualifications.viewDocument')}
              </button>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
