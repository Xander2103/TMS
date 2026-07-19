import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
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
  const [qualifications, setQualifications] = useState<MyQualification[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listMyQualifications()
      .then((data) => {
        if (!mounted) return
        setQualifications(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Je kwalificaties konden niet worden geladen.')
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
      showError('Het document kon niet worden geopend.')
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!qualifications) return <LoadingState message="Kwalificaties laden..." />

  return (
    <div>
      <PageHeader title="Mijn kwalificaties" subtitle="Je rijbewijzen, attesten en hun vervaldata." />

      {qualifications.length === 0 && <p className="portal-empty">Nog geen kwalificaties geregistreerd.</p>}

      <ul className="portal-qualifications">
        {qualifications.map((qualification) => (
          <li key={qualification.id} className="portal-qualification">
            <div className="portal-absence-head">
              <strong>{qualification.qualificationTypeName}</strong>
              <Badge tone={MY_QUALIFICATION_STATUS_TONE[qualification.effectiveStatus]}>
                {MY_QUALIFICATION_STATUS_LABELS[qualification.effectiveStatus]}
              </Badge>
            </div>
            <div className="portal-absence-dates">
              Behaald: {qualification.obtainedDate}
              {qualification.expiryDate && ` · vervalt: ${qualification.expiryDate}`}
              {qualification.documentNumber && ` · nr. ${qualification.documentNumber}`}
            </div>
            {qualification.hasDocument && (
              <button type="button" className="portal-doc-link" onClick={() => void openDocument(qualification.id)}>
                📄 Document bekijken
              </button>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
