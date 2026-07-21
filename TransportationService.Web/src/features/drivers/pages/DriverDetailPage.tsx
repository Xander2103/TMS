import { useEffect, useState } from 'react'
import { Navigate, useParams } from 'react-router-dom'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { getDriver } from '../api/driversApi'

/**
 * Legacy standalone driver route. The driver profile now lives inside the personnel dossier,
 * so this resolver fetches the driver, learns its employee, and redirects to the
 * "chauffeursprofiel" tab. Personal and driver data live in one place — no duplicate screens.
 */
export function DriverDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    let mounted = true
    getDriver(id)
      .then((driver) => {
        if (mounted) setEmployeeId(driver.employeeId)
      })
      .catch(() => {
        if (mounted) setError('Chauffeur kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  if (error) return <ErrorState message={error} />
  if (!employeeId) return <LoadingState message="Chauffeursprofiel openen…" />
  return <Navigate to={`/employees/${employeeId}?tab=chauffeursprofiel`} replace />
}
