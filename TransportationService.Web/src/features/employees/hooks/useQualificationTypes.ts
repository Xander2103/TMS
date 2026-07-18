import { useEffect, useState } from 'react'
import { getQualificationTypes } from '../api/qualificationsApi'
import type { QualificationType } from '../types/qualification'

interface UseQualificationTypesResult {
  qualificationTypes: QualificationType[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_MESSAGE = 'Kwalificatietypes konden niet worden geladen.'

export function useQualificationTypes(): UseQualificationTypesResult {
  const [qualificationTypes, setQualificationTypes] = useState<QualificationType[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    getQualificationTypes()
      .then((data) => {
        if (!isMounted) return
        setQualificationTypes(data)
        setIsLoading(false)
      })
      .catch(() => {
        if (!isMounted) return
        setError(LOAD_ERROR_MESSAGE)
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [])

  return { qualificationTypes, isLoading, error }
}
