import { useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { FormField } from '../../../components/ui/FormField'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { CustomerForm } from '../components/CustomerForm'
import { CustomerContactsPanel } from '../components/CustomerContactsPanel'
import { useCustomer } from '../hooks/useCustomer'
import { useCustomerMutations } from '../hooks/useCustomerMutations'
import { VAT_TREATMENT_LABELS } from '../types'
import './../components/customers.css'

export function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const { customer, isLoading, error, reload } = useCustomer(id)
  const mutations = useCustomerMutations()
  const canEdit = hasPermission('customers.edit')
  const canDelete = hasPermission('customers.delete')

  const [isEditing, setIsEditing] = useState(false)
  const [showBlockDialog, setShowBlockDialog] = useState(false)
  const [blockReason, setBlockReason] = useState('')
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [showUnblockConfirm, setShowUnblockConfirm] = useState(false)

  if (isLoading) return <LoadingState message="Klant laden..." />
  if (error || !customer) return <ErrorState message={error ?? 'Klant niet gevonden.'} />

  async function handleBlock(event: FormEvent) {
    event.preventDefault()
    if (!id) return
    const ok = await mutations.setBlocked(id, true, blockReason.trim() || null)
    if (ok) {
      toast.showSuccess('Klant geblokkeerd.')
      setShowBlockDialog(false)
      setBlockReason('')
      reload()
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klanten', to: '/customers' }, { label: customer.name }]} />
      <PageHeader
        title={customer.name}
        action={
          !isEditing && (
            <div className="customer-detail-toolbar">
              {canEdit && (
                <Button variant="secondary" onClick={() => setIsEditing(true)}>
                  Bewerken
                </Button>
              )}
              {canEdit &&
                (customer.isBlocked ? (
                  <Button variant="secondary" onClick={() => setShowUnblockConfirm(true)}>
                    Deblokkeren
                  </Button>
                ) : (
                  <Button variant="secondary" onClick={() => setShowBlockDialog(true)}>
                    Blokkeren
                  </Button>
                ))}
              {canDelete && (
                <Button variant="danger" onClick={() => setShowDeleteConfirm(true)}>
                  Verwijderen
                </Button>
              )}
            </div>
          )
        }
      />

      {isEditing ? (
        <CustomerForm
          mode="edit"
          initial={customer}
          isSubmitting={mutations.isSubmitting}
          submitError={mutations.error}
          onCancel={() => setIsEditing(false)}
          onSubmit={async (values) => {
            if (!id) return
            const updated = await mutations.update(id, values)
            if (updated) {
              toast.showSuccess('Klant bijgewerkt.')
              setIsEditing(false)
              reload()
            }
          }}
        />
      ) : (
        <div className="customer-detail-layout">
          <div>
            <div className="customer-summary">
              <dl>
                <dt>Klantnummer</dt>
                <dd>
                  <code>{customer.customerNumber}</code>
                </dd>
                <dt>Status</dt>
                <dd>
                  <StatusBadges
                    active={customer.isActive}
                    blocked={{ isBlocked: customer.isBlocked, reason: customer.blockReason }}
                  />
                </dd>
                {customer.categoryName && (
                  <>
                    <dt>Categorie</dt>
                    <dd>{customer.categoryName}</dd>
                  </>
                )}
                <dt>Adres</dt>
                <dd>
                  {[customer.street, customer.houseNumber].filter(Boolean).join(' ')}
                  {customer.city ? `, ${[customer.postalCode, customer.city].filter(Boolean).join(' ')}` : ''}
                  {customer.countryCode ? ` (${customer.countryCode})` : ''}
                </dd>
                {customer.email && (
                  <>
                    <dt>E-mail</dt>
                    <dd>{customer.email}</dd>
                  </>
                )}
                {customer.phoneNumber && (
                  <>
                    <dt>Telefoon</dt>
                    <dd>{customer.phoneNumber}</dd>
                  </>
                )}
                <dt>Betaaltermijn</dt>
                <dd>{customer.paymentTermDays} dagen</dd>
                {customer.notes && (
                  <>
                    <dt>Notities</dt>
                    <dd>{customer.notes}</dd>
                  </>
                )}
              </dl>
            </div>

            <div className="customer-summary customer-vat-summary">
              <h3>BTW & Peppol</h3>
              <dl>
                <dt>BTW-nummer</dt>
                <dd>{customer.vatNumber ?? '—'}</dd>
                <dt>BTW-behandeling</dt>
                <dd>{VAT_TREATMENT_LABELS[customer.vatTreatment]}</dd>
                <dt>Standaard tarief</dt>
                <dd>{customer.defaultVatRatePercent !== null ? `${customer.defaultVatRatePercent}%` : 'Bedrijfsstandaard'}</dd>
                {customer.vatCountryCode && (
                  <>
                    <dt>BTW-land</dt>
                    <dd>{customer.vatCountryCode}</dd>
                  </>
                )}
                <dt>Peppol</dt>
                <dd>{customer.peppolId ? `${customer.peppolScheme ?? '?'}:${customer.peppolId}` : 'Niet geconfigureerd'}</dd>
                {customer.invoiceEmail && (
                  <>
                    <dt>Facturatie-e-mail</dt>
                    <dd>{customer.invoiceEmail}</dd>
                  </>
                )}
                {(customer.customerReferenceRequired || customer.purchaseOrderRequired || customer.signedDeliveryNoteRequired) && (
                  <>
                    <dt>Vereisten</dt>
                    <dd>
                      {[
                        customer.customerReferenceRequired ? 'Klantreferentie verplicht' : null,
                        customer.purchaseOrderRequired ? 'Bestelbon vereist' : null,
                        customer.signedDeliveryNoteRequired ? 'Getekende leverbon vereist' : null,
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </dd>
                  </>
                )}
                {customer.vatNotes && (
                  <>
                    <dt>BTW-notities</dt>
                    <dd>{customer.vatNotes}</dd>
                  </>
                )}
              </dl>
            </div>
          </div>

          <CustomerContactsPanel
            contacts={customer.contacts}
            isSubmitting={mutations.isSubmitting}
            onAdd={async (input) => {
              if (!id) return false
              const ok = await mutations.addContact(id, input)
              if (ok) {
                toast.showSuccess('Contactpersoon toegevoegd.')
                reload()
              }
              return ok
            }}
            onUpdate={async (contactId, input) => {
              if (!id) return false
              const ok = await mutations.updateContact(id, contactId, input)
              if (ok) {
                toast.showSuccess('Contactpersoon bijgewerkt.')
                reload()
              }
              return ok
            }}
            onRemove={async (contactId) => {
              if (!id) return false
              const ok = await mutations.removeContact(id, contactId)
              if (ok) {
                toast.showSuccess('Contactpersoon verwijderd.')
                reload()
              }
              return ok
            }}
          />
        </div>
      )}

      {showBlockDialog && (
        <Modal
          title="Klant blokkeren"
          onClose={() => setShowBlockDialog(false)}
          busy={mutations.isSubmitting}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowBlockDialog(false)} disabled={mutations.isSubmitting}>
                Annuleren
              </Button>
              <Button variant="danger" type="submit" form="block-form" disabled={mutations.isSubmitting}>
                Blokkeren
              </Button>
            </>
          }
        >
          <form id="block-form" onSubmit={handleBlock}>
            <FormField label="Reden" htmlFor="block-reason" hint="Optioneel, maar aanbevolen.">
              <textarea id="block-reason" value={blockReason} onChange={(e) => setBlockReason(e.target.value)} rows={3} maxLength={500} />
            </FormField>
          </form>
        </Modal>
      )}

      {showUnblockConfirm && (
        <ConfirmDialog
          title="Klant deblokkeren"
          message={`Blokkering voor '${customer.name}' opheffen?`}
          confirmLabel="Deblokkeren"
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const ok = await mutations.setBlocked(id, false, null)
            if (ok) {
              toast.showSuccess('Klant gedeblokkeerd.')
              setShowUnblockConfirm(false)
              reload()
            }
          }}
          onCancel={() => setShowUnblockConfirm(false)}
        />
      )}

      {showDeleteConfirm && (
        <ConfirmDialog
          title="Klant verwijderen"
          message={`Weet u zeker dat u '${customer.name}' wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const ok = await mutations.remove(id)
            if (ok) {
              toast.showSuccess('Klant verwijderd.')
              navigate('/customers')
            }
          }}
          onCancel={() => setShowDeleteConfirm(false)}
        />
      )}
    </div>
  )
}
