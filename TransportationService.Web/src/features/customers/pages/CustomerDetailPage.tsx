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
import { changeCustomerNumber, removeCustomerContact } from '../api/customersApi'
import { getCustomerMessagesUnreadCount } from '../api/customerMessagesApi'
import { CustomerForm } from '../components/CustomerForm'
import { CustomerContactsPanel } from '../components/CustomerContactsPanel'
import { CustomerDayDocumentsCard } from '../components/CustomerDayDocumentsCard'
import { CustomerHistoryPanel } from '../components/CustomerHistoryPanel'
import { CustomerMessagesPanel } from '../components/CustomerMessagesPanel'
import { CustomerAddressesPanel } from '../components/CustomerAddressesPanel'
import { CustomerCommunicationPanel } from '../components/CustomerCommunicationPanel'
import { CustomerBillingPanel } from '../components/CustomerBillingPanel'
import { CustomerPriceAdjustmentsPanel } from '../components/CustomerPriceAdjustmentsPanel'
import { CustomerUnitPricingPanel } from '../components/CustomerUnitPricingPanel'
import { CustomerUnitsPanel } from '../components/CustomerUnitsPanel'
import { CustomerFiscalWarnings } from '../components/CustomerFiscalWarnings'
import { CombinedDiscountsPanel } from '../../tarification/components/CombinedDiscountsPanel'
import { useCustomer } from '../hooks/useCustomer'
import { useCustomerMutations } from '../hooks/useCustomerMutations'
import { useLocale } from '../../../i18n/localeContext'
import { VAT_TREATMENT_LABEL_KEYS } from '../types'
import './../components/customers.css'

export function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { customer, isLoading, error, reload } = useCustomer(id)
  const mutations = useCustomerMutations()
  const canEdit = hasPermission('customers.edit')
  const canDelete = hasPermission('customers.delete')
  const canDeactivate = hasPermission('customers.deactivate')
  const canViewLocations = hasPermission('locations.view')
  const canViewBilling = hasAnyPermission(['customers.view'])
  const canViewTariffs = hasAnyPermission(['tariffs.view', 'tariffs.manage'])
  const canOverrideNumber = hasPermission('customers.override_number')
  const canViewMessages = hasPermission('customer_messages.view')
  const canViewOrders = hasAnyPermission(['orders.view', 'orders.manage'])

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
  const [unreadMessages, setUnreadMessages] = useState(0)

  useEffect(() => {
    if (customer) {
      addRecentItem({ category: 'Klanten', title: customer.name, route: `/customers/${customer.id}` })
    }
  }, [customer])

  useEffect(() => {
    if (!canViewMessages || !id) return
    let mounted = true
    getCustomerMessagesUnreadCount(id)
      .then((data) => {
        if (mounted) setUnreadMessages(data.count)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [id, canViewMessages])

  if (isLoading) return <LoadingState message={t('customers.detail.loading')} />
  if (error || !customer) return <ErrorState message={error ? t(error) : t('customers.detail.notFound')} />

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
    if (!newNumber.trim()) localErrors.customerNumber = t('customers.detail.newNumberRequired')
    else if (newNumber.trim().length > 30) localErrors.customerNumber = t('customers.detail.numberMaxLength')
    if (!numberReason.trim()) localErrors.reason = t('customers.detail.reasonRequired')
    setNumberLocalErrors(localErrors)
    if (localErrors.customerNumber || localErrors.reason) return

    setNumberBusy(true)
    setNumberError(null)
    setNumberFieldErrors({})
    try {
      await changeCustomerNumber(id, { customerNumber: newNumber.trim(), reason: numberReason.trim() })
      toast.showSuccess(t('customers.detail.numberChanged'))
      setShowNumberDialog(false)
      reload()
    } catch (err) {
      const described = describeApiError(err, t('customers.detail.numberChangeFailed'))
      setNumberError(described.message)
      setNumberFieldErrors(described.fieldErrors)
    } finally {
      setNumberBusy(false)
    }
  }

  // One wiring for the contacts panel, shared by the edit form and the read-only tab.
  // Delete goes straight to the API so a backend refusal (e.g. contact still referenced by a
  // communication rule) surfaces its Dutch message via the error toast.
  const contactsPanel = (
    <CustomerContactsPanel
      customerId={id ?? ''}
      contacts={customer.contacts}
      isSubmitting={mutations.isSubmitting}
      onAdd={async (input) => {
        if (!id) return null
        const created = await mutations.addContact(id, input)
        if (created) {
          toast.showSuccess(t('customers.contacts.added'))
          reload()
        }
        return created
      }}
      onUpdate={async (contactId, input) => {
        if (!id) return false
        const ok = await mutations.updateContact(id, contactId, input)
        if (ok) {
          toast.showSuccess(t('customers.contacts.updated'))
          reload()
        }
        return ok
      }}
      onRemove={async (contactId) => {
        if (!id) return false
        try {
          await removeCustomerContact(id, contactId)
          toast.showSuccess(t('customers.contacts.removed'))
          reload()
          return true
        } catch (err) {
          toast.showError(describeApiError(err, t('customers.contacts.removeFailed')).message)
          return false
        }
      }}
    />
  )

  async function handleBlock(event: FormEvent) {
    event.preventDefault()
    if (!id) return
    const ok = await mutations.setBlocked(id, true, blockReason.trim() || null)
    if (ok) {
      toast.showSuccess(t('customers.detail.blocked'))
      setShowBlockDialog(false)
      setBlockReason('')
      reload()
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.customers'), to: '/customers' }, { label: customer.name }]} />
      <BackButton to="/customers" label={t('customers.detail.backToCustomers')} />
      <PageHeader
        title={customer.name}
        action={
          !isEditing && (
            <div className="customer-detail-toolbar">
              {canEdit && (
                <Button variant="secondary" onClick={() => setIsEditing(true)}>
                  {t('ui.actions.edit')}
                </Button>
              )}
              {canEdit &&
                (customer.isBlocked ? (
                  <Button variant="secondary" onClick={() => setShowUnblockConfirm(true)}>
                    {t('customers.detail.unblock')}
                  </Button>
                ) : (
                  <Button variant="secondary" onClick={() => setShowBlockDialog(true)}>
                    {t('customers.detail.block')}
                  </Button>
                ))}
              {canDeactivate &&
                (customer.isActive ? (
                  <Button variant="secondary" onClick={() => setShowActiveConfirm('deactivate')}>
                    {t('customers.detail.deactivate')}
                  </Button>
                ) : (
                  <Button variant="secondary" onClick={() => setShowActiveConfirm('activate')}>
                    {t('customers.detail.reactivate')}
                  </Button>
                ))}
              {canDelete && (
                <Button variant="danger" onClick={() => setShowDeleteConfirm(true)}>
                  {t('ui.actions.delete')}
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
          editPanels={{
            adressen:
              canViewLocations && id ? (
                <CustomerAddressesPanel customerId={id} />
              ) : (
                <p className="customer-form-muted">{t('customers.detail.noAddressRights')}</p>
              ),
            contactpersonen: contactsPanel,
            communicatie: id ? <CustomerCommunicationPanel customerId={id} contacts={customer.contacts} /> : null,
            historiek: id ? <CustomerHistoryPanel customerId={id} /> : null,
            tarieven:
              canViewBilling && id ? (
                <>
                  {/* Reading order: what prices this customer (basis → toeslagen → afwijkingen),
                      then technical unit/EDI mapping, then invoicing config. */}
                  {canViewTariffs ? (
                    <>
                      <CustomerUnitPricingPanel customerId={id} />
                      <CustomerPriceAdjustmentsPanel customerId={id} />
                      <CombinedDiscountsPanel customerId={id} />
                      <CustomerUnitsPanel customerId={id} />
                    </>
                  ) : (
                    <p className="customer-form-muted">{t('customers.detail.noTariffRights')}</p>
                  )}
                  <CustomerBillingPanel customerId={id} />
                </>
              ) : (
                <p className="customer-form-muted">{t('customers.detail.noTariffRights')}</p>
              ),
          }}
          onSubmit={async (values) => {
            if (!id) return
            const updated = await mutations.update(id, values)
            if (updated) {
              toast.showSuccess(t('customers.detail.updated'))
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
                { id: 'general', label: t('customers.detail.tabGeneral') },
                ...(canViewLocations ? [{ id: 'locations', label: t('customers.detail.tabLocations') }] : []),
                { id: 'contacts', label: t('customers.contacts.title'), badge: customer.contacts.length || undefined },
                { id: 'communication', label: t('customers.form.sections.communicatie') },
                ...(canViewBilling ? [{ id: 'billing', label: t('customers.form.sections.tarieven') }] : []),
                { id: 'history', label: t('customers.form.sections.historiek') },
                ...(canViewMessages
                  ? [{ id: 'messages', label: t('customers.detail.tabMessages'), badge: unreadMessages || undefined }]
                  : []),
              ]}
              activeId={activeTab}
              onChange={setActiveTab}
            />
          </div>

          {activeTab === 'general' && (
            <TabPanel tabId="general">
              <div className="customer-detail-layout">
            <div className="customer-summary">
              <h3>{t('customers.form.sections.klantgegevens')}</h3>
              <dl>
                <dt>{t('customers.fields.customerNumber')}</dt>
                <dd>
                  <span className="customer-number-line">
                    <code>{customer.customerNumber}</code>
                    {canOverrideNumber && (
                      <Button variant="secondary" onClick={openNumberDialog}>
                        {t('customers.detail.changeNumber')}
                      </Button>
                    )}
                  </span>
                </dd>
                <dt>{t('customers.detail.statusLabel')}</dt>
                <dd>
                  <StatusBadges
                    active={customer.isActive}
                    blocked={{ isBlocked: customer.isBlocked, reason: customer.blockReason }}
                  />
                </dd>
                {customer.nickname && (
                  <>
                    <dt>{t('customers.fields.nickname')}</dt>
                    <dd>{customer.nickname}</dd>
                  </>
                )}
                {customer.categoryName && (
                  <>
                    <dt>{t('customers.form.category')}</dt>
                    <dd>{customer.categoryName}</dd>
                  </>
                )}
                <dt>{t('customers.detail.addressLabel')}</dt>
                <dd>
                  {[customer.street, customer.houseNumber].filter(Boolean).join(' ')}
                  {customer.city ? `, ${[customer.postalCode, customer.city].filter(Boolean).join(' ')}` : ''}
                  {customer.countryCode ? ` (${customer.countryCode})` : ''}
                </dd>
                {customer.email && (
                  <>
                    <dt>{t('customers.contacts.email')}</dt>
                    <dd>{customer.email}</dd>
                  </>
                )}
                {customer.phoneNumber && (
                  <>
                    <dt>{t('customers.contacts.phone')}</dt>
                    <dd>{customer.phoneNumber}</dd>
                  </>
                )}
                <dt>{t('customers.detail.paymentTermLabel')}</dt>
                <dd>{t('customers.detail.paymentTermDays', { days: customer.paymentTermDays })}</dd>
                {customer.notes && (
                  <>
                    <dt>{t('customers.contacts.notes')}</dt>
                    <dd>{customer.notes}</dd>
                  </>
                )}
              </dl>
            </div>

            <div className="customer-summary customer-vat-summary">
              <h3>{t('customers.form.sections.fiscaal')}</h3>
              <dl>
                <dt>{t('customers.fields.vatNumber')}</dt>
                <dd>{customer.vatNumber ?? '—'}</dd>
                {customer.companyNumber && (
                  <>
                    <dt>{t('customers.fields.companyNumber')}</dt>
                    <dd>{customer.companyNumber}</dd>
                  </>
                )}
                <dt>{t('customers.form.vatTreatment')}</dt>
                <dd>{t(VAT_TREATMENT_LABEL_KEYS[customer.vatTreatment])}</dd>
                <dt>{t('customers.detail.defaultRateLabel')}</dt>
                <dd>
                  {customer.defaultVatRatePercent !== null
                    ? `${customer.defaultVatRatePercent}%`
                    : t('customers.form.companyDefault')}
                </dd>
                {customer.vatCountryCode && (
                  <>
                    <dt>{t('customers.fields.vatCountryCode')}</dt>
                    <dd>{customer.vatCountryCode}</dd>
                  </>
                )}
                <dt>Peppol</dt>
                <dd>
                  {customer.peppolId
                    ? `${customer.peppolScheme ?? '?'}:${customer.peppolId}`
                    : t('customers.detail.peppolNotConfigured')}
                </dd>
                {customer.iban && (
                  <>
                    <dt>{t('customers.fields.iban')}</dt>
                    <dd>{customer.iban}{customer.bic ? ` (${customer.bic})` : ''}</dd>
                  </>
                )}
                {customer.currencyCode && customer.currencyCode !== 'EUR' && (
                  <>
                    <dt>{t('customers.fields.currencyCode')}</dt>
                    <dd>{customer.currencyCode}</dd>
                  </>
                )}
                {customer.invoiceEmail && (
                  <>
                    <dt>{t('customers.form.invoiceEmail')}</dt>
                    <dd>{customer.invoiceEmail}</dd>
                  </>
                )}
                {(customer.customerReferenceRequired || customer.purchaseOrderRequired || customer.signedDeliveryNoteRequired) && (
                  <>
                    <dt>{t('customers.detail.requirementsLabel')}</dt>
                    <dd>
                      {[
                        customer.customerReferenceRequired ? t('customers.detail.requirementCustomerReference') : null,
                        customer.purchaseOrderRequired ? t('customers.detail.requirementPurchaseOrder') : null,
                        customer.signedDeliveryNoteRequired ? t('customers.detail.requirementSignedNote') : null,
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </dd>
                  </>
                )}
                {customer.vatNotes && (
                  <>
                    <dt>{t('customers.form.vatNotes')}</dt>
                    <dd>{customer.vatNotes}</dd>
                  </>
                )}
              </dl>
              <CustomerFiscalWarnings
                customerId={customer.id}
                countryCode={customer.vatCountryCode ?? customer.countryCode}
                refreshKey={`${customer.vatTreatment}|${customer.vatNumber ?? ''}|${customer.countryCode ?? ''}|${customer.vatCountryCode ?? ''}|${customer.defaultLegalEntityId ?? ''}`}
              />
            </div>
              </div>
              {canViewOrders && id && <CustomerDayDocumentsCard customerId={id} />}
            </TabPanel>
          )}

          {activeTab === 'contacts' && <TabPanel tabId="contacts">{contactsPanel}</TabPanel>}

          {activeTab === 'locations' && canViewLocations && id && (
            <TabPanel tabId="locations">
              <CustomerAddressesPanel customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'communication' && id && (
            <TabPanel tabId="communication">
              <CustomerCommunicationPanel customerId={id} contacts={customer.contacts} />
            </TabPanel>
          )}

          {activeTab === 'billing' && canViewBilling && id && (
            <TabPanel tabId="billing">
              {/* Reading order: what prices this customer (basis → toeslagen → afwijkingen),
                  then technical unit/EDI mapping, then invoicing config. */}
              {canViewTariffs ? (
                <>
                  <CustomerUnitPricingPanel customerId={id} />
                  <CustomerPriceAdjustmentsPanel customerId={id} />
                  <CombinedDiscountsPanel customerId={id} />
                  <CustomerUnitsPanel customerId={id} />
                </>
              ) : (
                <p className="customer-form-muted">{t('customers.detail.noTariffRights')}</p>
              )}
              <CustomerBillingPanel customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'history' && id && (
            <TabPanel tabId="history">
              <CustomerHistoryPanel customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'messages' && canViewMessages && id && (
            <TabPanel tabId="messages">
              <CustomerMessagesPanel customerId={id} onMarkedRead={() => setUnreadMessages(0)} />
            </TabPanel>
          )}
        </>
      )}

      {showNumberDialog && (
        <Modal
          title={t('customers.detail.changeNumberTitle')}
          onClose={() => setShowNumberDialog(false)}
          busy={numberBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowNumberDialog(false)} disabled={numberBusy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="change-number-form" disabled={numberBusy}>
                {numberBusy ? t('customers.common.saving') : t('customers.detail.changeAction')}
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
              label={t('customers.detail.newNumberField')}
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
              label={t('customers.detail.reasonField')}
              htmlFor="change-number-reason"
              required
              hint={t('customers.detail.reasonHint')}
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
          title={t('customers.detail.blockTitle')}
          onClose={() => setShowBlockDialog(false)}
          busy={mutations.isSubmitting}
          footer={
            <>
              <Button variant="secondary" onClick={() => setShowBlockDialog(false)} disabled={mutations.isSubmitting}>
                {t('ui.actions.cancel')}
              </Button>
              <Button variant="danger" type="submit" form="block-form" disabled={mutations.isSubmitting}>
                {t('customers.detail.block')}
              </Button>
            </>
          }
        >
          <form id="block-form" onSubmit={handleBlock}>
            <FormField label={t('customers.detail.reasonField')} htmlFor="block-reason" hint={t('customers.detail.blockReasonHint')}>
              <textarea id="block-reason" value={blockReason} onChange={(e) => setBlockReason(e.target.value)} rows={3} maxLength={500} />
            </FormField>
          </form>
        </Modal>
      )}

      {showUnblockConfirm && (
        <ConfirmDialog
          title={t('customers.detail.unblockTitle')}
          message={t('customers.detail.unblockMessage', { name: customer.name })}
          confirmLabel={t('customers.detail.unblock')}
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const ok = await mutations.setBlocked(id, false, null)
            if (ok) {
              toast.showSuccess(t('customers.detail.unblocked'))
              setShowUnblockConfirm(false)
              reload()
            }
          }}
          onCancel={() => setShowUnblockConfirm(false)}
        />
      )}

      {showActiveConfirm && (
        <ConfirmDialog
          title={showActiveConfirm === 'deactivate' ? t('customers.detail.deactivateTitle') : t('customers.detail.reactivateTitle')}
          message={
            showActiveConfirm === 'deactivate'
              ? t('customers.detail.deactivateMessage', { name: customer.name })
              : t('customers.detail.reactivateMessage', { name: customer.name })
          }
          confirmLabel={showActiveConfirm === 'deactivate' ? t('customers.detail.deactivate') : t('customers.detail.reactivate')}
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const activate = showActiveConfirm === 'activate'
            const ok = await mutations.setActive(id, activate)
            if (ok) {
              toast.showSuccess(activate ? t('customers.detail.reactivated') : t('customers.detail.deactivated'))
              setShowActiveConfirm(null)
              reload()
            }
          }}
          onCancel={() => setShowActiveConfirm(null)}
        />
      )}

      {showDeleteConfirm && (
        <ConfirmDialog
          title={t('customers.detail.deleteTitle')}
          message={t('customers.detail.deleteMessage', { name: customer.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            if (!id) return
            const ok = await mutations.remove(id)
            if (ok) {
              toast.showSuccess(t('customers.detail.deleted'))
              navigate('/customers')
            }
          }}
          onCancel={() => setShowDeleteConfirm(false)}
        />
      )}
    </div>
  )
}
