import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/ToastProvider'
import { CustomerForm } from '../components/CustomerForm'
import { useCustomerMutations } from '../hooks/useCustomerMutations'

export function NewCustomerPage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { create, isSubmitting, error } = useCustomerMutations()

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klanten', to: '/customers' }, { label: 'Nieuwe klant' }]} />
      <PageHeader title="Nieuwe klant" />
      <CustomerForm
        mode="create"
        isSubmitting={isSubmitting}
        submitError={error}
        onCancel={() => navigate('/customers')}
        onSubmit={async (values) => {
          const created = await create(values)
          if (created) {
            toast.showSuccess('Klant aangemaakt.')
            navigate(`/customers/${created.id}`)
          }
        }}
      />
    </div>
  )
}
