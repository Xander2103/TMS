import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { listMyDocuments } from '../api/driverApi'
import { DRIVER_DOCUMENT_TYPE_LABELS, type DriverDocument } from '../types'

/** Documents of the assets on the driver's active trips — nothing else is ever listed. */
export function DriverDocumentsPage() {
  const [documents, setDocuments] = useState<DriverDocument[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    listMyDocuments()
      .then((data) => {
        if (!cancelled) setDocuments(data)
      })
      .catch(() => {
        if (!cancelled) setError('Documenten laden mislukt.')
      })
    return () => {
      cancelled = true
    }
  }, [])

  async function download(document_: DriverDocument) {
    // Authenticated download via fetch; object URLs avoid exposing the token in the DOM.
    const response = await fetch(`${apiBaseUrl}/api/my/documents/${document_.id}/download`, {
      headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    })
    if (!response.ok) return
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${document_.documentType}-${document_.documentNumber ?? document_.assetNumber}`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  if (error) return <p className="drv-muted">{error}</p>
  if (!documents) return <p className="drv-muted">Laden…</p>

  return (
    <div>
      <h1 className="drv-page-title">Documenten</h1>
      {documents.length === 0 && (
        <p className="drv-muted">Geen documenten voor jouw actieve ritten.</p>
      )}
      <ul className="drv-list">
        {documents.map((doc) => (
          <li key={doc.id} className="drv-card">
            <div className="drv-fact-row">
              <strong>{DRIVER_DOCUMENT_TYPE_LABELS[doc.documentType] ?? doc.customTypeName ?? doc.documentType}</strong>
              <span>{doc.assetNumber}</span>
            </div>
            {doc.documentNumber && (
              <div className="drv-fact-row"><span>Nummer</span><span>{doc.documentNumber}</span></div>
            )}
            <div className="drv-badges">
              {doc.expiryDate && (
                <Badge tone={new Date(doc.expiryDate) < new Date() ? 'danger' : 'neutral'}>
                  Vervalt {new Date(`${doc.expiryDate}T00:00:00`).toLocaleDateString('nl-BE')}
                </Badge>
              )}
              {doc.fileAvailable ? (
                <button type="button" className="drv-download" onClick={() => void download(doc)}>
                  Downloaden
                </button>
              ) : (
                <Badge tone="neutral">Geen bestand</Badge>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
