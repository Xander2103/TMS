import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { EmployeeForm } from '../components/EmployeeForm'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'

export function NewEmployeePage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const mutations = useEmployeeMutations()

  const canCreateDriver = hasPermission('drivers.create')
  const [isDriver, setIsDriver] = useState(false)
  const [driverSuggested, setDriverSuggested] = useState(false)
  const [driverCategoryId, setDriverCategoryId] = useState<string | null>(null)
  const [driverNotes, setDriverNotes] = useState('')

  // Suggest (once) enabling the driver profile when a driver-type function is chosen;
  // the user stays in control of the final choice.
  function handleFunctionsChanged(codes: string[]) {
    if (!canCreateDriver || driverSuggested) return
    if (codes.some((code) => code.toUpperCase().startsWith('CHAUF'))) {
      setIsDriver(true)
      setDriverSuggested(true)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: 'Nieuwe medewerker' }]} />
      <PageHeader title="Nieuwe medewerker" />
      <EmployeeForm
        mode="create"
        isSubmitting={mutations.isSubmitting}
        submitError={mutations.error}
        onCancel={() => navigate('/employees')}
        onFunctionsChanged={handleFunctionsChanged}
        extraSections={
          canCreateDriver && (
            <FormSection
              title="Chauffeursprofiel"
              columns={2}
              description="Een chauffeursprofiel wordt in dezelfde stap aangemaakt — persoonsgegevens worden nooit dubbel ingevoerd."
            >
              <div className="form-span-all">
                <label className="customer-form-checkbox">
                  <input type="checkbox" checked={isDriver} onChange={(e) => setIsDriver(e.target.checked)} />
                  Deze medewerker is chauffeur
                </label>
              </div>
              {isDriver && (
                <>
                  <FormField label="Chauffeurcategorie" htmlFor="ne-driver-category">
                    <LookupSelect
                      id="ne-driver-category"
                      basePath="/api/driver-categories"
                      managePermission="driver_categories.manage"
                      singular="chauffeurcategorie"
                      value={driverCategoryId}
                      onChange={setDriverCategoryId}
                      placeholder="— Geen —"
                    />
                  </FormField>
                  <FormField label="Chauffeursnotities" htmlFor="ne-driver-notes">
                    <textarea id="ne-driver-notes" rows={2} value={driverNotes} onChange={(e) => setDriverNotes(e.target.value)} maxLength={2000} />
                  </FormField>
                </>
              )}
            </FormSection>
          )
        }
        onSubmit={async (values) => {
          const created = await mutations.create({
            ...values,
            driverProfile: isDriver ? { driverCategoryId, notes: driverNotes.trim() || null } : null,
          })
          if (created) {
            if (created.driverId) {
              toast.showSuccess(`Medewerker ${created.employeeNumber} en chauffeursprofiel aangemaakt.`)
              navigate(`/employees/${created.id}?tab=kwalificaties`)
            } else {
              toast.showSuccess(`Medewerker ${created.employeeNumber} aangemaakt.`)
              navigate(`/employees/${created.id}`)
            }
          }
        }}
      />
    </div>
  )
}
