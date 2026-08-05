import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { CustomerForm } from '../components/CustomerForm'
import { PreparedLocationsEditor } from '../components/PreparedLocationsEditor'
import { CustomerCreateFollowUpDialog } from '../components/CustomerCreateFollowUpDialog'
import { useCustomerMutations } from '../hooks/useCustomerMutations'
import {
  createPreparedLocations,
  type LocationFollowUpResult,
  type PreparedLocation,
} from '../utils/preparedLocations'
import type { CustomerDetail } from '../types'

export function NewCustomerPage() {
  // "Opslaan en nieuwe klant" resets the entire create flow (form fields, contact rows,
  // staged locations) by remounting the inner page with a fresh key.
  const [formInstance, setFormInstance] = useState(0)
  return <NewCustomerPageContent key={formInstance} onSavedAndNew={() => setFormInstance((k) => k + 1)} />
}

function NewCustomerPageContent({ onSavedAndNew }: { onSavedAndNew: () => void }) {
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const { create, isSubmitting, error, fieldErrors } = useCustomerMutations()
  const canCreateLocations = hasPermission('locations.create')

  // Staged locations: held in form state until the customer exists, then POSTed one by one.
  const [stagedLocations, setStagedLocations] = useState<PreparedLocation[]>([])
  // After creation: the created customer + any follow-up results awaiting retry/dismissal.
  const [createdCustomer, setCreatedCustomer] = useState<CustomerDetail | null>(null)
  const [followUpResults, setFollowUpResults] = useState<LocationFollowUpResult[] | null>(null)
  const [followUpBusy, setFollowUpBusy] = useState(false)

  function goToCustomer(customer: CustomerDetail) {
    navigate(`/customers/${customer.id}`)
  }

  // Runs after the customer create succeeds. On full success it navigates to the detail page —
  // or, for "Opslaan en nieuwe klant", resets the create page for the next entry. On partial
  // failure it keeps the customer + staged rows and surfaces the retry dialog.
  async function processFollowUps(customer: CustomerDetail, startNextEntry: boolean) {
    const finish = startNextEntry ? onSavedAndNew : () => goToCustomer(customer)
    if (stagedLocations.length === 0) {
      toast.showSuccess(`Klant ${customer.customerNumber} aangemaakt.`)
      finish()
      return
    }
    setFollowUpBusy(true)
    try {
      const results = await createPreparedLocations(customer.id, stagedLocations)
      if (results.every((r) => r.ok)) {
        toast.showSuccess(`Klant ${customer.customerNumber} aangemaakt; ${results.length} locatie(s) aangemaakt.`)
        finish()
      } else {
        toast.showError('Klant aangemaakt, maar enkele locaties zijn mislukt.')
        setCreatedCustomer(customer)
        setFollowUpResults(results)
      }
    } finally {
      setFollowUpBusy(false)
    }
  }

  async function retryFailedFollowUps() {
    if (!createdCustomer || !followUpResults) return
    setFollowUpBusy(true)
    try {
      const failedKeys = new Set(followUpResults.filter((r) => !r.ok).map((r) => r.key))
      const retried = await createPreparedLocations(
        createdCustomer.id,
        stagedLocations.filter((row) => failedKeys.has(row.key)),
      )
      const merged = followUpResults.map((r) => retried.find((n) => n.key === r.key) ?? r)
      if (merged.every((r) => r.ok)) {
        const customer = createdCustomer
        toast.showSuccess('Alle locaties aangemaakt.')
        setFollowUpResults(null)
        setCreatedCustomer(null)
        goToCustomer(customer)
      } else {
        setFollowUpResults(merged)
      }
    } finally {
      setFollowUpBusy(false)
    }
  }

  function dismissFollowUp() {
    const customer = createdCustomer
    setFollowUpResults(null)
    setCreatedCustomer(null)
    if (customer) goToCustomer(customer)
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klanten', to: '/customers' }, { label: 'Nieuwe klant' }]} />
      <PageHeader title="Nieuwe klant" />
      <CustomerForm
        mode="create"
        isSubmitting={isSubmitting || followUpBusy}
        submitError={error}
        serverFieldErrors={fieldErrors}
        onCancel={() => navigate('/customers')}
        stagedLocationsSlot={
          canCreateLocations ? (
            <PreparedLocationsEditor value={stagedLocations} onChange={setStagedLocations} disabled={isSubmitting || followUpBusy} />
          ) : (
            <p className="customer-form-muted">Je hebt geen rechten om locaties aan te maken.</p>
          )
        }
        onSubmit={async (values, intent) => {
          const created = await create(values)
          // Creation failed → no locations posted, nothing orphaned.
          if (created) await processFollowUps(created, intent === 'saveAndNew')
        }}
      />
      {createdCustomer && followUpResults && (
        <CustomerCreateFollowUpDialog
          customerLabel={`Klant ${createdCustomer.customerNumber}`}
          results={followUpResults}
          busy={followUpBusy}
          onRetry={retryFailedFollowUps}
          onClose={dismissFollowUp}
        />
      )}
    </div>
  )
}
