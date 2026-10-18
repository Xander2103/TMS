import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { addRecentItem } from '../../../hooks/recentItems'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { FormField } from '../../../components/ui/FormField'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { AttentionList } from '../../../components/ui/AttentionList'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { useDetailTab } from '../../../hooks/useDetailTab'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { getReferenceConflict, type ReferenceCount } from '../../../api/conflicts'
import { useAuth } from '../../auth/authContextValue'
import {
  addCustomerContact,
  changeCustomerNumber,
  deleteCustomer,
  getCustomerFiscalWarnings,
  getCustomerReferences,
  removeCustomerContact,
  updateCustomerContact,
  type CustomerFiscalWarning,
} from '../api/customersApi'
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
import { CustomerOverview } from '../components/CustomerOverview'
import { buildCustomerAttention } from '../utils/customerAttention'
import { CombinedDiscountsPanel } from '../../tarification/components/CombinedDiscountsPanel'
import { CUSTOMER_NOT_FOUND, useCustomer } from '../hooks/useCustomer'
import { useCustomerMutations } from '../hooks/useCustomerMutations'
import type { CustomerDetail } from '../types'
import { useLocale } from '../../../i18n/localeContext'
import './../components/customers.css'
import '../customers.css'
import { ActionMenu } from '../../../components/ui/ActionMenu'

const TAB_LABEL_KEYS: Record<TabId, string> = {
  overzicht: 'customers.detail.tabOverview',
  adressen: 'customers.detail.tabLocations',
  contactpersonen: 'customers.contacts.title',
  communicatie: 'customers.form.sections.communicatie',
  tarieven: 'customers.form.sections.tarieven',
  documenten: 'customers.detail.tabDocuments',
  historiek: 'customers.form.sections.historiek',
  berichten: 'customers.detail.tabMessages',
}

type TabId = 'overzicht' | 'adressen' | 'contactpersonen' | 'communicatie' | 'tarieven' | 'documenten' | 'historiek' | 'berichten'

const TAB_ALIASES: Partial<Record<string, { tab: TabId }>> = {
  general: { tab: 'overzicht' },
  locations: { tab: 'adressen' },
  contacts: { tab: 'contactpersonen' },
  communication: { tab: 'communicatie' },
  billing: { tab: 'tarieven' },
  history: { tab: 'historiek' },
  messages: { tab: 'berichten' },
}

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
  const canViewDossiers = hasAnyPermission(['dossiers.view', 'dossiers.manage'])
  const canViewInvoices = hasPermission('invoices.view')

  // B17: one navigation layer, kept in ?tab=. The former in-memory ids (general, locations, …)
  // are accepted as aliases so any link built on them keeps working.
  const tabIds: TabId[] = [
    'overzicht',
    ...(canViewLocations ? (['adressen'] as const) : []),
    'contactpersonen',
    'communicatie',
    ...(canViewBilling ? (['tarieven'] as const) : []),
    ...(canViewOrders ? (['documenten'] as const) : []),
    'historiek',
    ...(canViewMessages ? (['berichten'] as const) : []),
  ]
  const { tab: activeTab, setTab: setActiveTab } = useDetailTab<TabId>({ ids: tabIds, defaultId: 'overzicht', aliases: TAB_ALIASES })
  // null = read mode; otherwise the form section the edit started on.
  const [editSection, setEditSection] = useState<string | null>(null)
  const isEditing = editSection !== null
  const setIsEditing = (value: boolean) => setEditSection(value ? 'klantgegevens' : null)
  const [fiscalWarnings, setFiscalWarnings] = useState<{ key: string; items: CustomerFiscalWarning[] } | null>(null)
  const [showBlockDialog, setShowBlockDialog] = useState(false)
  const [blockReason, setBlockReason] = useState('')
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [deleteBusy, setDeleteBusy] = useState(false)
  // Delete refused because history references the customer: the references, shown with the
  // "Deactiveren" alternative instead of a confirm that can only fail.
  const [deleteBlockedBy, setDeleteBlockedBy] = useState<ReferenceCount[] | null>(null)
  // "Gegevens herladen" after a stale save: the edit form is remounted (formKey) once the
  // refetched customer replaces the stale one we were showing (`reloadingFrom`).
  const [formKey, setFormKey] = useState(0)
  const [reloadingFrom, setReloadingFrom] = useState<CustomerDetail | null>(null)
  if (reloadingFrom && customer && customer !== reloadingFrom) {
    setReloadingFrom(null)
    setFormKey((key) => key + 1)
  }
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

  // Advisory fiscal notices feed the attention strip; re-fetched when a fiscal field changes.
  const fiscalKey = customer
    ? `${customer.id}|${customer.vatTreatment}|${customer.vatNumber ?? ''}|${customer.countryCode ?? ''}|${customer.vatCountryCode ?? ''}|${customer.defaultLegalEntityId ?? ''}`
    : null
  useEffect(() => {
    if (!customer || !fiscalKey) return
    let mounted = true
    getCustomerFiscalWarnings(customer.id)
      .then((items) => { if (mounted) setFiscalWarnings({ key: fiscalKey, items }) })
      .catch(() => { if (mounted) setFiscalWarnings({ key: fiscalKey, items: [] }) })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fiscalKey])

  // Stale-while-refetch: only the very first load (or a new id) shows the loading state. A
  // `reload()` after a panel save keeps the current customer on screen so the edit form, the
  // self-saving panels and any open dialog stay mounted (and edits elsewhere survive).
  if (isLoading) return <LoadingState message={t('customers.detail.loading')} />
  if (error === CUSTOMER_NOT_FOUND) return <ErrorState message={t(CUSTOMER_NOT_FOUND)} backTo="/customers" />
  if (error || !customer) return <ErrorState message={error ? t(error) : t('customers.detail.notFound')} onRetry={reload} backTo="/customers" />

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
  // Add/update go straight to the API and let a failure propagate: the panel keeps its dialog
  // open with the entered values and shows the message inline. The callbacks never reload —
  // the panel calls `onChanged` only after every write (contact + notifications) succeeded,
  // so a refetch can never race the notification PUT or unmount the dialog mid-save.
  // Delete goes straight to the API so a backend refusal (e.g. contact still referenced by a
  // communication rule) surfaces its Dutch message via the error toast.
  const renderContactsPanel = (showTitle: boolean) => (
    <CustomerContactsPanel
      customerId={id ?? ''}
      contacts={customer.contacts}
      isSubmitting={mutations.isSubmitting}
      showTitle={showTitle}
      onAdd={async (input) => {
        if (!id) return null
        return await addCustomerContact(id, input)
      }}
      onUpdate={async (contactId, input) => {
        if (!id) return false
        await updateCustomerContact(id, contactId, input)
        return true
      }}
      onChanged={reload}
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

  /** "Verwijderen": pre-check the references so a customer with history never gets a doomed confirm. */
  async function requestDelete() {
    if (!id) return
    mutations.clearError()
    setDeleteBusy(true)
    try {
      const references = await getCustomerReferences(id)
      if (references.length > 0) setDeleteBlockedBy(references)
      else setShowDeleteConfirm(true)
    } catch {
      // The pre-check is advisory: the DELETE itself still refuses (409) and is handled below.
      setShowDeleteConfirm(true)
    } finally {
      setDeleteBusy(false)
    }
  }

  async function confirmDelete() {
    if (!id) return
    setDeleteBusy(true)
    try {
      await deleteCustomer(id)
      toast.showSuccess(t('customers.detail.deleted'))
      navigate('/customers')
    } catch (err) {
      // Race: history got linked between the pre-check and the delete.
      const conflict = getReferenceConflict(err)
      if (conflict) {
        setShowDeleteConfirm(false)
        setDeleteBlockedBy(conflict.references)
      } else {
        toast.showError(describeApiError(err, t('customers.detail.deleteFailed')).message)
      }
    } finally {
      setDeleteBusy(false)
    }
  }

  /** After a stale save: drop the edit form's values and start again from the current data. */
  function reloadStaleForm() {
    if (!customer) return
    mutations.clearError()
    setReloadingFrom(customer)
    reload()
  }

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

  const attention = buildCustomerAttention(customer, fiscalWarnings?.key === fiscalKey ? fiscalWarnings.items : [], t, {
    onComplete: canEdit ? (section) => setEditSection(section) : undefined,
    onUnblock: canEdit ? () => setShowUnblockConfirm(true) : undefined,
  })

  // Reading order: what prices this customer (basis → toeslagen → afwijkingen), then planned
  // changes and discounts, then technical unit/EDI mapping and invoicing config (collapsed).
  const tariffsPanel =
    canViewBilling && id ? (
      <div className="customer-tariffs">
        {canViewTariffs ? (
          <>
            <CustomerUnitPricingPanel customerId={id} />
            <CustomerPriceAdjustmentsPanel customerId={id} />
            <CombinedDiscountsPanel customerId={id} />
          </>
        ) : (
          <p className="customer-form-muted">{t('customers.detail.noTariffRights')}</p>
        )}
        <CustomerBillingPanel customerId={id} />
        {canViewTariffs && <CustomerUnitsPanel customerId={id} />}
      </div>
    ) : (
      <p className="customer-form-muted">{t('customers.detail.noTariffRights')}</p>
    )

  const subtitle = [
    customer.customerNumber,
    customer.nickname,
    customer.categoryName,
    [customer.city, customer.countryCode].filter(Boolean).join(', ') || null,
  ]
    .filter(Boolean)
    .join(' · ')

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.customers'), to: '/customers' }, { label: customer.name }]} />
      <PageHeader
        title={customer.name}
        subtitle={subtitle}
        badges={
          <StatusBadges active={customer.isActive} blocked={{ isBlocked: customer.isBlocked, reason: customer.blockReason }} />
        }
        action={
          !isEditing && (
            <>
              {/* B03/B17: one primary (edit); number change, blocking, (de)activating and deleting
                  are rare and live behind "Meer", destructive last. */}
              <ActionMenu
                items={[
                  ...(canOverrideNumber ? [{ key: 'number', label: t('customers.detail.changeNumber'), onSelect: openNumberDialog }] : []),
                  ...(canEdit
                    ? [
                        customer.isBlocked
                          ? { key: 'unblock', label: t('customers.detail.unblock'), onSelect: () => setShowUnblockConfirm(true) }
                          : { key: 'block', label: t('customers.detail.block'), onSelect: () => setShowBlockDialog(true), danger: true },
                      ]
                    : []),
                  ...(canDeactivate
                    ? [
                        customer.isActive
                          ? { key: 'deactivate', label: t('customers.detail.deactivate'), onSelect: () => setShowActiveConfirm('deactivate'), danger: true }
                          : { key: 'activate', label: t('customers.detail.reactivate'), onSelect: () => setShowActiveConfirm('activate') },
                      ]
                    : []),
                  ...(canDelete
                    ? [{ key: 'delete', label: t('ui.actions.delete'), onSelect: () => void requestDelete(), danger: true, disabled: deleteBusy }]
                    : []),
                ]}
              />
              {canEdit && <Button onClick={() => setEditSection('klantgegevens')}>{t('ui.actions.edit')}</Button>}
            </>
          )
        }
      />

      {!isEditing && <AttentionList items={attention} />}

      {isEditing && reloadingFrom ? (
        <LoadingState message={t('customers.detail.loading')} />
      ) : isEditing ? (
        <CustomerForm
          key={formKey}
          mode="edit"
          initial={customer}
          initialSectionId={editSection ?? undefined}
          isSubmitting={mutations.isSubmitting}
          submitError={mutations.error}
          serverFieldErrors={mutations.fieldErrors}
          staleConflict={mutations.isConflict}
          onReload={reloadStaleForm}
          onCancel={() => {
            mutations.clearError()
            setIsEditing(false)
          }}
          editPanels={{
            adressen:
              canViewLocations && id ? (
                <CustomerAddressesPanel customerId={id} showTitle={false} />
              ) : (
                <p className="customer-form-muted">{t('customers.detail.noAddressRights')}</p>
              ),
            // The flat FormSection already carries the "Contactpersonen" heading.
            contactpersonen: renderContactsPanel(false),
            communicatie: id ? <CustomerCommunicationPanel customerId={id} contacts={customer.contacts} /> : null,
            historiek: id ? <CustomerHistoryPanel customerId={id} /> : null,
            tarieven: tariffsPanel,
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
          <Tabs
            tabs={tabIds.map((tabId) => ({
              id: tabId,
              label: t(TAB_LABEL_KEYS[tabId]),
              badge:
                tabId === 'contactpersonen'
                  ? customer.contacts.length || undefined
                  : tabId === 'berichten'
                    ? unreadMessages || undefined
                    : undefined,
            }))}
            activeId={activeTab}
            onChange={(next) => setActiveTab(next as TabId)}
          />

          {activeTab === 'overzicht' && (
            <TabPanel tabId="overzicht">
              <CustomerOverview
                customer={customer}
                onEdit={canEdit ? (section) => setEditSection(section) : undefined}
                canViewDossiers={canViewDossiers}
                canViewInvoices={canViewInvoices}
              />
            </TabPanel>
          )}

          {activeTab === 'contactpersonen' && <TabPanel tabId="contactpersonen">{renderContactsPanel(true)}</TabPanel>}

          {activeTab === 'adressen' && id && (
            <TabPanel tabId="adressen">
              <CustomerAddressesPanel customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'communicatie' && id && (
            <TabPanel tabId="communicatie">
              <CustomerCommunicationPanel customerId={id} contacts={customer.contacts} />
            </TabPanel>
          )}

          {activeTab === 'tarieven' && <TabPanel tabId="tarieven">{tariffsPanel}</TabPanel>}

          {/* "Documenten per dag" is an operational batch action (preview + download of one
              delivery day's delivery notes/CMRs for this customer); it moved off the overview
              into its own section so it no longer dominates the customer's identity. */}
          {activeTab === 'documenten' && id && (
            <TabPanel tabId="documenten">
              <CustomerDayDocumentsCard customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'historiek' && id && (
            <TabPanel tabId="historiek">
              <CustomerHistoryPanel customerId={id} />
            </TabPanel>
          )}

          {activeTab === 'berichten' && id && (
            <TabPanel tabId="berichten">
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
          busy={deleteBusy}
          onConfirm={confirmDelete}
          onCancel={() => setShowDeleteConfirm(false)}
        />
      )}

      {deleteBlockedBy && (
        <Modal
          title={t('customers.detail.deleteBlockedTitle')}
          onClose={() => setDeleteBlockedBy(null)}
          busy={mutations.isSubmitting}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDeleteBlockedBy(null)} disabled={mutations.isSubmitting}>
                {t('ui.actions.close')}
              </Button>
              {canDeactivate && customer.isActive && (
                <Button
                  disabled={mutations.isSubmitting}
                  onClick={async () => {
                    if (!id) return
                    const ok = await mutations.setActive(id, false)
                    if (ok) {
                      toast.showSuccess(t('customers.detail.deactivated'))
                      setDeleteBlockedBy(null)
                      reload()
                    }
                  }}
                >
                  {t('customers.detail.deactivate')}
                </Button>
              )}
            </>
          }
        >
          {mutations.error && (
            <p className="customer-import-message customer-import-message-error" role="alert">
              {mutations.error}
            </p>
          )}
          <p>{t('customers.detail.deleteBlockedIntro')}</p>
          <ul className="customer-delete-references">
            {deleteBlockedBy.map((reference) => (
              <li key={reference.key}>{reference.label}</li>
            ))}
          </ul>
          <p>
            {customer.isActive ? t('customers.detail.deleteBlockedHint') : t('customers.detail.deleteBlockedInactiveHint')}
          </p>
        </Modal>
      )}
    </div>
  )
}
