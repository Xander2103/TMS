import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/toastContext'
import { EmployeeForm } from '../components/EmployeeForm'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'

export function NewEmployeePage() {
  const navigate = useNavigate()
  const toast = useToast()
  const mutations = useEmployeeMutations()

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: 'Nieuwe medewerker' }]} />
      <PageHeader title="Nieuwe medewerker" />
      <EmployeeForm
        mode="create"
        isSubmitting={mutations.isSubmitting}
        submitError={mutations.error}
        onCancel={() => navigate('/employees')}
        onSubmit={async (values) => {
          const created = await mutations.create(values)
          if (created) {
            toast.showSuccess(`Medewerker ${created.employeeNumber} aangemaakt.`)
            navigate(`/employees/${created.id}`)
          }
        }}
      />
    </div>
  )
}
