import { useCallback, useEffect, useRef, useState } from 'react'
import { listDossierDocuments, type DossierDocument } from '../api/dossierDocumentsApi'

/**
 * The ONE list every document view of a dossier derives from (D6): the dossier endpoint returns
 * both scopes, each document exactly once. A stale response (dossier switched, retry fired) is
 * ignored by sequence; `reload()` re-reads after every mutation so what is on screen is what the
 * server holds — never a locally invented row.
 */
export function useDossierDocuments(dossierId: string, enabled = true) {
  const [documents, setDocuments] = useState<DossierDocument[] | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const sequence = useRef(0)

  const reload = useCallback(() => {
    if (!enabled) return Promise.resolve<DossierDocument[] | null>(null)
    const mine = ++sequence.current
    // State changes only in the callbacks: the effect that triggers a load sets nothing itself.
    return listDossierDocuments(dossierId)
      .then((data) => {
        if (mine !== sequence.current) return null
        setDocuments(data)
        setLoadFailed(false)
        return data
      })
      .catch(() => {
        if (mine === sequence.current) setLoadFailed(true)
        return null
      })
  }, [dossierId, enabled])

  useEffect(() => {
    void reload()
  }, [reload])

  return { documents, loadFailed, reload, setDocuments }
}
