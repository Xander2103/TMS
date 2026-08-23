import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getQualificationTypes } from '../api/qualificationsApi'
import type { QualificationType } from '../types/qualification'

interface UseQualificationTypesResult {
  qualificationTypes: QualificationType[]
  isLoading: boolean
  error: string | null
}

const LOAD_ERROR_KEY = 'employees.errors.loadQualificationTypes'

export function useQualificationTypes(): UseQualificationTypesResult {
  const { t } = useLocale()
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
        setError(t(LOAD_ERROR_KEY))
        setIsLoading(false)
      })

    return () => {
      isMounted = false
    }
  }, [])

  return { qualificationTypes, isLoading, error }
}
