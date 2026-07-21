import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { addRecentItem } from '../../../hooks/recentItems'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { FormField } from '../../../components/ui/FormField'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { changeCustomerNumber } from '../api/customersApi'
import { CustomerForm } from '../components/CustomerForm'
import { CustomerContactsPanel } from '../components/CustomerContactsPanel'
import { CustomerLocationsPanel } from '../components/CustomerLocationsPanel'
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
  const canDeactivate = hasPermission('customers.deactivate')
  const canViewLocations = hasPermission('locations.view')
  const canOverrideNumber = hasPermission('customers.override_number')

  const [activeTab, setActiveTab] = useState('general')
  const [isEditing, setIsEditing] = useState(false)
  const [showBlockDialog, setShowBlockDialog] = useState(false)
  const [blockReason, setBlockReason] = useState('')
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [showUnblockConfirm, setShowUnblockConfirm] = useState(false)
  const [showActiveConfirm, setShowActiveConfirm] = useState<null | 'activate' | 'deactivate'>(null)
  const [showNumberDialog, setShowNumberDialog] = useState(false)
  const [newNumber, setNewNumber] = useState('')
  const [numberReason, setNumberReason] = useState('')
  const [numberBusy, setNumberBusy] = useState(false)
  const [numberError, setNumberError] = useState<string | null>(null)
  const [numberFieldErrors, setNumberFieldErrors] = useState<FieldErrors>({})
  const [numberLocalErrors, setNumberLocalErrors] = useState<{ customerNumber?: string; reason?: string }>({})

  useEffect(() => {
    if (customer) {
      addRecentItem({ category: 'Klanten', title: customer.name, route: `/customers/${customer.id}` })
    }
  }, [customer])

  if (isLoading) return <LoadingState message="Klant laden..." />
  if (error || !customer) return <ErrorState message={error ?? 'Klant niet gevonden.'} />

  function openNumberDialog() {
    setNewNumber('')
    setNumberReason('')
    setNumberError(null)
    setNumberFieldErrors({})
    setNumberLocalErrors({})
    setShowNumberDialog(true)
  }

  async function handleChangeNumber(event: FormEvent) {
    event.preventDefault()
    if (!id) return

    const localErrors: { customerNumber?: string; reason?: string } = {}
    if (!newNumber.trim()) localErrors.customerNumber = 'Nieuw klantnummer is verplicht.'
    else if (newNumber.trim().length > 30) localErrors.customerNumber = 'Maximaal 30 tekens.'
    if (!numberReason.trim()) localErrors.reason = 'Reden is verplicht.'
    setNumberLocalErrors(localErrors)
    if (localErrors.customerNumber || localErrors.reason) return

    setNumberBusy(true)
    setNumberError(null)
    setNumberFieldErrors({})
    try {
      await changeCustomerNumber(id, { customerNumber: newNumber.trim(), reason: numberReason.trim() })
      toast.showSuccess('Klantnummer gewijzigd.')
      setShowNumberDialog(false)
      reload()
    } catch (err) {
      const described = describeApiError(err, 'Het klantnummer kon niet worden gewijzigd.')
      setNumberError(described.message)
      setNumberFieldErrors(described.fieldErrors)
    } finally {
      setNumberBusy(false)
    }
  }

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
      <BackButton to="/customers" label="Terug naar klanten" />
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
              {canDeactivate &&
                (customer.isActive ? (
                  <Button variant="secondary" onClick={() => setShowActiveConfirm('deactivate')}>
                    Deactiveren
                  </Button>
                ) : (
                  <Button variant="secondary" onClick={() => setShowActiveConfirm('activate')}>
                    Heractiveren
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
          serverFieldErrors={mutations.fieldErrors}
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
        <>
          <div className="customer-detail-tabs">
            <Tabs
              tabs={[
                { id: 'general', label: 'Algemeen' },
                { id: 'contacts', label: 'Contactpersonen', badge: customer.contacts.length || undefined },
                ...(canViewLocations ? [{ id: 'locations', label: 'Locaties' }] : []),
              ]}
              activeId={activeTab}
              onChange={setActiveTab}
            />
          </div>

          {activeTab === 'general' && (
            <TabPanel tabId="general">
              <div className="customer-detail-layout">
            <div className="customer-summary">
              <dl>
                <dt>Klantnummer</dt>
                <dd>
                  <span className="customer-number-line">
                    <code>{customer.customerNumber}</code>
                    {canOverrideNumber && (
                      <Button variant="secondary" onClick={openNumberDialog}>
                        Nummer wijzigen
                      </Button>
                    )}
                  </span>
                </dd>
                <dt>Status</dt>
                <dd>
                  <StatusBadges
                    active={customer.isActive}
                    blocked={{ isBlocked: customer.isBlocked, reason: customer.blockReason }}
                  />
                </dd>
                {customer.nickname && (
                  <>
                    <dt>Roepnaam</dt>
                    <dd>{customer.nickname}</dd>
                  </>
                )}
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
              <h3>Fiscaal & Peppol</h3>
              <dl>
                <dt>BTW-nummer</dt>
                <dd>{customer.vatNumber ?? '—'}</dd>
                {customer.companyNumber && (
                  <>
                    <dt>Ondernemingsnummer</dt>
                    <dd>{customer.companyNumber}</dd>
                  </>
                )}
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
                {customer.iban && (
                  <>
                    <dt>IBAN</dt>
                    <dd>{customer.iban}{customer.bic ? ` (${customer.bic})` : ''}</dd>
                  </>
                )}
                {customer.currencyCode && customer.currencyCode !== 'EUR' && (
                  <>
                    <dt>Valuta</dt>
                    <dd>{customer.currencyCode}</dd>
                  </>
                )}
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
            </TabPanel>
          )}

          {activeTab === 'contacts' && (
            <TabPanel tabId="contacts">
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
            </TabPanel>
          )}

          {activeTab === 'locations' && canViewLocations && id && (
            <TabPanel tabId="locations">
              <CustomerLocationsPanel customerId={id} />
            </TabPanel>
          )}
        </>
      )}

      {showNumberDialog && (
        <Modal
          title="Klantnummer wijzigen"
          onClose={() => setShowNumberDialog(false)}
          busy={numberBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowNumberDialog(false)} disabled={numberBusy}>
                Annuleren
              </Button>
              <Button type="submit" form="change-number-form" disabled={numberBusy}>
                {numberBusy ? 'Opslaan...' : 'Wijzigen'}
              </Button>
            </>
          }
        >
          <form id="change-number-form" onSubmit={handleChangeNumber}>
            {numberError && (
              <p className="customer-import-message customer-import-message-error" role="alert">
                {numberError}
              </p>
            )}
            <FormField
              label="Nieuw klantnummer"
              htmlFor="change-number"
              required
              error={numberLocalErrors.customerNumber ?? getFieldError(numberFieldErrors, 'customerNumber')}
            >
              <input
                id="change-number"
                value={newNumber}
                onChange={(e) => setNewNumber(e.target.value)}
                maxLength={30}
                aria-invalid={
                  numberLocalErrors.customerNumber ?? getFieldError(numberFieldErrors, 'customerNumber')
                    ? 'true'
                    : undefined
                }
              />
            </FormField>
            <FormField
              label="Reden"
              htmlFor="change-number-reason"
              required
              hint="Wordt vastgelegd in de historiek."
              error={numberLocalErrors.reason ?? getFieldError(numberFieldErrors, 'reason')}
            >
              <textarea
                id="change-number-reason"
                value={numberReason}
                onChange={(e) => setNumberReason(e.target.value)}
                rows={3}
                maxLength={500}
                aria-invalid={numberLocalErrors.reason ?? getFieldError(numberFieldErrors, 'reason') ? 'true' : undefined}
              />
            </FormField>
          </form>
        </Modal>
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

      {showActiveConfirm && (
        <ConfirmDialog
          title={showActiveConfirm === 'deactivate' ? 'Klant deactiveren' : 'Klant heractiveren'}
          message={
            showActiveConfirm === 'deactivate'
              ? `'${customer.name}' deactiveren? Bestaande opdrachten, facturen en historiek blijven behouden, maar er kunnen geen nieuwe opdrachten voor deze klant worden aangemaakt.`
              : `'${customer.name}' heractiveren? De klant is daarna weer kiesbaar voor nieuwe opdrachten.`
          }
          confirmLabel={showActiveConfirm === 'deactivate' ? 'Deactiveren' : 'Heractiveren'}
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const activate = showActiveConfirm === 'activate'
            const ok = await mutations.setActive(id, activate)
            if (ok) {
              toast.showSuccess(activate ? 'Klant geheractiveerd.' : 'Klant gedeactiveerd.')
              setShowActiveConfirm(null)
              reload()
            }
          }}
          onCancel={() => setShowActiveConfirm(null)}
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
