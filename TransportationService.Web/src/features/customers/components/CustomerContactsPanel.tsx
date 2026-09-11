import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Badge } from '../../../components/ui/Badge'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { PanelHeader } from '../../../components/ui/PanelHeader'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useAuth } from '../../auth/authContextValue'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import {
  CONTACT_LANGUAGES,
  CONTACT_LANGUAGE_KEYS,
  NOTIFICATION_GROUP_KEYS,
  NOTIFICATION_OPTION_KEYS,
  getContactNotifications,
  getNotificationOptions,
  setContactNotifications,
  type CustomerNotificationGroup,
  type CustomerNotificationOption,
} from '../api/customerNotificationsApi'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import {
  CUSTOMER_CONTACT_TYPE_LABEL_KEYS,
  CUSTOMER_CONTACT_TYPES,
  type CustomerContact,
  type CustomerContactInput,
  type CustomerContactType,
} from '../types'

interface CustomerContactsPanelProps {
  /** Needed to store "Ontvangt meldingen" against the contact (sprint 3). */
  customerId: string
  contacts: CustomerContact[]
  isSubmitting: boolean
  /**
   * Renders the "Contactpersonen" heading in the toolbar. The edit form hosts this panel
   * inside a FormSection that already carries that title, so it passes `false`.
   */
  showTitle?: boolean
  /** Creates the contact; resolves with it (or null) — may also throw an API error. */
  onAdd: (input: CustomerContactInput) => Promise<CustomerContact | null>
  /** Updates the contact fields only; resolves true (or false) — may also throw an API error. */
  onUpdate: (contactId: string, input: CustomerContactInput) => Promise<boolean>
  onRemove: (contactId: string) => Promise<boolean>
  /**
   * Called once ALL writes of a contact save (fields, then notification choices) succeeded.
   * The host reloads here — never from inside `onAdd`/`onUpdate`, which would refetch (and
   * possibly unmount this panel) while the notification PUT is still to be sent.
   */
  onChanged?: () => void
}

type DialogState = { mode: 'create' } | { mode: 'edit'; contact: CustomerContact } | null

function contactDisplayName(contact: CustomerContact): string {
  return contact.displayName?.trim() || `${contact.firstName} ${contact.lastName}`
}

/** Vertaald label voor een contacttype; onbekende (nieuwe) enumwaarden vallen terug op de code. */
function contactTypeLabel(t: TranslateFn, type: CustomerContactType): string {
  const key = CUSTOMER_CONTACT_TYPE_LABEL_KEYS[type]
  return key ? t(key) : type
}

/** Order-insensitive comparison of two option-key sets. */
function sameKeys(a: readonly string[], b: readonly string[]): boolean {
  if (a.length !== b.length) return false
  const set = new Set(a)
  return b.every((key) => set.has(key))
}

export function CustomerContactsPanel({
  customerId,
  contacts,
  isSubmitting,
  showTitle = true,
  onAdd,
  onUpdate,
  onRemove,
  onChanged,
}: CustomerContactsPanelProps) {
  const { t } = useLocale()
  const [dialog, setDialog] = useState<DialogState>(null)
  const [removeTarget, setRemoveTarget] = useState<CustomerContact | null>(null)
  const [typeFilter, setTypeFilter] = useState<CustomerContactType | ''>('')
  const { hasPermission } = useAuth()
  const toast = useToast()
  const canViewDepartments = hasPermission('contact_departments.view')
  // "Ontvangt meldingen" writes communication rules, so it follows that permission.
  const canManageNotifications = hasPermission('customers.manage_communication')
  const departments = useLookupOptions('/api/contact-departments', { enabled: canViewDepartments })
  const departmentNames = useMemo(() => new Map(departments.options.map((d) => [d.id, d.name])), [departments.options])

  const visibleContacts = typeFilter ? contacts.filter((contact) => contact.contactType === typeFilter) : contacts

  const columns: Column<CustomerContact>[] = [
    {
      key: 'name',
      header: t('customers.contacts.columnName'),
      render: (contact) => (
        <span className="customer-contact-name">
          {contactDisplayName(contact)}{' '}
          {!contact.isActive && <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>}
        </span>
      ),
    },
    {
      key: 'type',
      header: t('customers.contacts.type'),
      render: (contact) => (
        <span className="customer-contact-name">
          {contactTypeLabel(t, contact.contactType)}
          {/* Primair geldt binnen het type: hoogstens één primaire contactpersoon per type. */}
          {contact.isPrimary && <Badge tone="info">{t('customers.contacts.primaryBadge')}</Badge>}
        </span>
      ),
    },
    { key: 'role', header: t('customers.contacts.role'), render: (contact) => contact.role ?? '—' },
    {
      key: 'department',
      header: t('customers.contacts.department'),
      render: (contact) => (contact.departmentId ? (departmentNames.get(contact.departmentId) ?? '—') : '—'),
    },
    { key: 'email', header: t('customers.contacts.email'), render: (contact) => contact.email ?? '—' },
    { key: 'phone', header: t('customers.contacts.phone'), render: (contact) => contact.phoneNumber ?? '—' },
    { key: 'mobile', header: t('customers.contacts.mobile'), render: (contact) => contact.mobilePhone ?? '—' },
    {
      key: 'actions',
      header: t('customers.contacts.columnActions'),
      render: (contact) => (
        <span className="customer-contact-actions">
          <Button variant="ghost" onClick={() => setDialog({ mode: 'edit', contact })}>
            {t('ui.actions.edit')}
          </Button>
          <Button variant="ghost" onClick={() => setRemoveTarget(contact)}>
            {t('ui.actions.delete')}
          </Button>
        </span>
      ),
    },
  ]

  /**
   * Save order is the contract: contact fields → notification choices → `onChanged`. Returns
   * the message to show inline when a step fails; the dialog then stays open with its values.
   */
  async function saveContact(
    current: Exclude<DialogState, null>,
    input: CustomerContactInput,
    notifications: NotificationSelection,
  ): Promise<string | null> {
    let contactId: string | null
    let created: CustomerContact | null = null
    try {
      if (current.mode === 'edit') {
        contactId = (await onUpdate(current.contact.id, input)) ? current.contact.id : null
      } else {
        created = await onAdd(input)
        contactId = created?.id ?? null
      }
    } catch (err) {
      return describeApiError(err, t('customers.contacts.saveFailed')).message
    }
    if (!contactId) return t('customers.contacts.saveFailed')

    // Only an actual change is written: an untouched card must never rewrite the routing
    // underneath (advanced rules stay exactly as the administrator left them). Choices that
    // never finished loading are never sent either — an empty set would unsubscribe everything.
    if (canManageNotifications && notifications.state === 'ready' && !sameKeys(notifications.keys, notifications.initialKeys)) {
      try {
        await setContactNotifications(customerId, contactId, notifications.keys)
      } catch (err) {
        // The contact itself exists since this attempt: a retry must UPDATE it, never create a
        // duplicate. The dialog keeps its typed values and ticks (state lives in the dialog).
        if (created) setDialog({ mode: 'edit', contact: created })
        return describeApiError(err, t('customers.notifications.saveFailed')).message
      }
    }

    toast.showSuccess(current.mode === 'edit' ? t('customers.contacts.updated') : t('customers.contacts.added'))
    setDialog(null)
    onChanged?.()
    return null
  }

  return (
    <div className="customer-contacts">
      <PanelHeader
        title={showTitle ? t('customers.contacts.title') : undefined}
        actions={
          <Button onClick={() => setDialog({ mode: 'create' })}>{t('customers.contacts.addContact')}</Button>
        }
      >
        <label htmlFor="ct-type-filter">{t('customers.contacts.type')}</label>
        <select
          id="ct-type-filter"
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as CustomerContactType | '')}
        >
          <option value="">{t('customers.contacts.allTypes')}</option>
          {CUSTOMER_CONTACT_TYPES.map((type) => (
            <option key={type} value={type}>
              {contactTypeLabel(t, type)}
            </option>
          ))}
        </select>
      </PanelHeader>

      <DataTable
        columns={columns}
        rows={visibleContacts}
        rowKey={(contact) => contact.id}
        isLoading={false}
        error={null}
        emptyMessage={t('customers.contacts.empty')}
      />

      {dialog && (
        <ContactDialog
          customerId={customerId}
          contact={dialog.mode === 'edit' ? dialog.contact : undefined}
          canManageNotifications={canManageNotifications}
          isSubmitting={isSubmitting}
          onClose={() => setDialog(null)}
          onSubmit={(input, notifications) => saveContact(dialog, input, notifications)}
        />
      )}

      {removeTarget && (
        <ConfirmDialog
          title={t('customers.contacts.removeTitle')}
          message={t('customers.contacts.removeMessage', {
            name: `${removeTarget.firstName} ${removeTarget.lastName}`,
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={isSubmitting}
          onConfirm={async () => {
            const ok = await onRemove(removeTarget.id)
            if (ok) setRemoveTarget(null)
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </div>
  )
}

type NotificationsState = 'loading' | 'ready' | 'error'

/** What the dialog hands back about "Ontvangt meldingen": the chosen keys, what was preloaded, and whether the preload finished. */
interface NotificationSelection {
  keys: string[]
  initialKeys: string[]
  state: NotificationsState
}

function ContactDialog({
  customerId,
  contact,
  canManageNotifications,
  isSubmitting,
  onSubmit,
  onClose,
}: {
  customerId: string
  contact?: CustomerContact
  canManageNotifications: boolean
  isSubmitting: boolean
  /** Resolves with an error message to show inline, or null when everything was saved. */
  onSubmit: (input: CustomerContactInput, notifications: NotificationSelection) => Promise<string | null>
  onClose: () => void
}) {
  const { t } = useLocale()
  const [firstName, setFirstName] = useState(contact?.firstName ?? '')
  const [lastName, setLastName] = useState(contact?.lastName ?? '')
  const [displayName, setDisplayName] = useState(contact?.displayName ?? '')
  const [nickname, setNickname] = useState(contact?.nickname ?? '')
  const [role, setRole] = useState(contact?.role ?? '')
  const [contactType, setContactType] = useState<CustomerContactType>(contact?.contactType ?? 'Algemeen')
  const [departmentId, setDepartmentId] = useState<string | null>(contact?.departmentId ?? null)
  const [email, setEmail] = useState(contact?.email ?? '')
  const [phoneNumber, setPhoneNumber] = useState(contact?.phoneNumber ?? '')
  const [mobilePhone, setMobilePhone] = useState(contact?.mobilePhone ?? '')
  const [preferredLanguageCode, setPreferredLanguageCode] = useState(contact?.preferredLanguageCode ?? '')
  const [isPrimary, setIsPrimary] = useState(contact?.isPrimary ?? false)
  const [isActive, setIsActive] = useState(contact?.isActive ?? true)
  const [notes, setNotes] = useState(contact?.notes ?? '')
  const [errors, setErrors] = useState<{ firstName?: string; lastName?: string; email?: string }>({})
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  // "Ontvangt meldingen" (sprint 3): the business question, not a routing rule.
  const [options, setOptions] = useState<CustomerNotificationOption[]>([])
  const [notificationKeys, setNotificationKeys] = useState<string[]>([])
  // What the contact already received when the dialog opened; saving compares against this.
  const [initialNotificationKeys, setInitialNotificationKeys] = useState<string[]>([])
  // A new contact has nothing to preload; an existing one is 'loading' until the GET settles.
  const [notificationsState, setNotificationsState] = useState<NotificationsState>(
    contact && canManageNotifications ? 'loading' : 'ready',
  )
  // The GET result initialises the keys exactly once; it must never overwrite user ticks. A
  // dialog that opened for a NEW contact has nothing to preload — also when the host promotes
  // it to edit mode after a partial save (contact created, routing PUT failed): the user's
  // ticks are the intended state, not whatever the server holds.
  const notificationsInitialised = useRef(!contact || !canManageNotifications)
  // A stored language outside the offered list (e.g. "it") must survive a save untouched.
  const storedLanguage = contact?.preferredLanguageCode ?? ''
  const hasOtherLanguage = storedLanguage !== '' && !(CONTACT_LANGUAGES as readonly string[]).includes(storedLanguage)
  const busy = isSubmitting || saving

  useEffect(() => {
    if (!canManageNotifications) return
    let active = true
    void getNotificationOptions()
      .then((data) => {
        if (active) setOptions(data)
      })
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [canManageNotifications])

  useEffect(() => {
    if (!contact || !canManageNotifications || notificationsInitialised.current) return
    let active = true
    void getContactNotifications(customerId, contact.id)
      .then((data) => {
        if (!active || notificationsInitialised.current) return
        notificationsInitialised.current = true
        setNotificationKeys(data.optionKeys)
        setInitialNotificationKeys(data.optionKeys)
        setNotificationsState('ready')
      })
      .catch(() => {
        // Unknown current choices: keep the boxes locked and never send a (destructive) empty set.
        if (active) setNotificationsState('error')
      })
    return () => {
      active = false
    }
  }, [customerId, contact, canManageNotifications])

  function toggleNotification(key: string, on: boolean) {
    setNotificationKeys((keys) => (on ? [...new Set([...keys, key])] : keys.filter((k) => k !== key)))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    const next: { firstName?: string; lastName?: string; email?: string } = {}
    if (!firstName.trim()) next.firstName = t('customers.contacts.firstNameRequired')
    if (!lastName.trim()) next.lastName = t('customers.contacts.lastNameRequired')
    // Notifications are delivered by e-mail only (CustomerContactSubscriptionService → rule
    // channel "Email"): a recipient without an address would silently receive nothing.
    if (notificationKeys.length > 0 && !email.trim()) next.email = t('customers.contacts.emailRequiredForNotifications')
    if (Object.keys(next).length > 0) {
      setErrors(next)
      return
    }
    setErrors({})
    setSubmitError(null)
    setSaving(true)
    const message = await onSubmit(
      {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        contactType,
        role: role.trim() || null,
        email: email.trim() || null,
        phoneNumber: phoneNumber.trim() || null,
        isPrimary,
        notes: notes.trim() || null,
        displayName: displayName.trim() || null,
        nickname: nickname.trim() || null,
        mobilePhone: mobilePhone.trim() || null,
        departmentId: departmentId || null,
        preferredLanguageCode: preferredLanguageCode.trim() || null,
        isActive,
      },
      { keys: notificationKeys, initialKeys: initialNotificationKeys, state: notificationsState },
    )
    // On success the host closes (unmounts) the dialog; on failure it stays open with the
    // entered values and the message, so nothing the user typed is lost.
    if (message !== null) {
      setSubmitError(message)
      setSaving(false)
    }
  }

  const notificationsLocked = notificationsState !== 'ready'

  return (
    <Modal
      title={contact ? t('customers.contacts.editTitle') : t('customers.contacts.newTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="contact-form" disabled={busy}>
            {busy ? t('customers.common.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="contact-form" onSubmit={handleSubmit} className="customer-form">
        {submitError && (
          <div className="form-span-all">
            <ValidationSummary message={submitError} />
          </div>
        )}
        <FormField label={t('customers.contacts.firstName')} htmlFor="ct-first" error={errors.firstName} required>
          <input id="ct-first" value={firstName} onChange={(e) => setFirstName(e.target.value)} aria-invalid={errors.firstName ? 'true' : undefined} maxLength={100} autoFocus />
        </FormField>
        <FormField label={t('customers.contacts.lastName')} htmlFor="ct-last" error={errors.lastName} required>
          <input id="ct-last" value={lastName} onChange={(e) => setLastName(e.target.value)} aria-invalid={errors.lastName ? 'true' : undefined} maxLength={100} />
        </FormField>
        <FormField label={t('customers.contacts.displayName')} htmlFor="ct-display" hint={t('customers.contacts.displayNameHint')}>
          <input id="ct-display" value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={200} />
        </FormField>
        <FormField label={t('customers.fields.nickname')} htmlFor="ct-nickname">
          <input id="ct-nickname" value={nickname} onChange={(e) => setNickname(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label={t('customers.contacts.role')} htmlFor="ct-role">
          <input id="ct-role" value={role} onChange={(e) => setRole(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label={t('customers.contacts.type')} htmlFor="ct-type" hint={t('customers.contacts.typeHint')}>
          <select id="ct-type" value={contactType} onChange={(e) => setContactType(e.target.value as CustomerContactType)}>
            {CUSTOMER_CONTACT_TYPES.map((type) => (
              <option key={type} value={type}>
                {contactTypeLabel(t, type)}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('customers.contacts.department')} htmlFor="ct-department">
          <LookupSelect
            id="ct-department"
            basePath="/api/contact-departments"
            viewPermission="contact_departments.view"
            managePermission="contact_departments.manage"
            singular="masterData.singular.departments"
            value={departmentId}
            onChange={setDepartmentId}
            placeholder={t('customers.contacts.noDepartment')}
          />
        </FormField>
        <FormField label={t('customers.contacts.email')} htmlFor="ct-email" error={errors.email}>
          <input id="ct-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={250} aria-invalid={errors.email ? true : undefined} />
        </FormField>
        <FormField label={t('customers.contacts.phone')} htmlFor="ct-phone">
          <input id="ct-phone" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} maxLength={30} />
        </FormField>
        <FormField label={t('customers.contacts.mobile')} htmlFor="ct-mobile">
          <input id="ct-mobile" value={mobilePhone} onChange={(e) => setMobilePhone(e.target.value)} maxLength={30} />
        </FormField>
        <FormField label={t('customers.form.preferredLanguage')} htmlFor="ct-language" hint={t('customers.contacts.languageSelectHint')}>
          <select id="ct-language" value={preferredLanguageCode} onChange={(e) => setPreferredLanguageCode(e.target.value)}>
            <option value="">{t('customers.form.sameAsPreferredLanguage')}</option>
            {CONTACT_LANGUAGES.map((code) => (
              <option key={code} value={code}>
                {t(CONTACT_LANGUAGE_KEYS[code])}
              </option>
            ))}
            {hasOtherLanguage && (
              <option value={storedLanguage}>{t('customers.notifications.languageOther', { code: storedLanguage })}</option>
            )}
          </select>
        </FormField>
        <FormField label={t('customers.contacts.notes')} htmlFor="ct-notes">
          <textarea id="ct-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} maxLength={1000} />
        </FormField>
        {canManageNotifications && options.length > 0 && (
          <fieldset className="customer-form-requirements form-span-all">
            <legend>{t('customers.notifications.receivesTitle')}</legend>
            <p className="customer-form-muted">{t('customers.notifications.receivesHint')}</p>
            {notificationsState === 'loading' && (
              <p className="customer-form-muted" aria-live="polite">
                {t('customers.contacts.notificationsLoading')}
              </p>
            )}
            {notificationsState === 'error' && (
              <p className="customer-import-message customer-import-message-error" role="alert">
                {t('customers.contacts.notificationsLoadFailed')}
              </p>
            )}
            {(['Transport', 'Facturatie', 'Algemeen'] as CustomerNotificationGroup[]).map((group) => {
              const groupOptions = options.filter((o) => o.group === group)
              if (groupOptions.length === 0) return null
              return (
                <div key={group} className="customer-notification-group">
                  <div className="nav-subgroup-label">{t(NOTIFICATION_GROUP_KEYS[group])}</div>
                  {groupOptions.map((option) => (
                    <label key={option.key} className="customer-form-checkbox">
                      <input
                        type="checkbox"
                        checked={notificationKeys.includes(option.key)}
                        disabled={notificationsLocked}
                        onChange={(e) => toggleNotification(option.key, e.target.checked)}
                      />
                      {t(NOTIFICATION_OPTION_KEYS[option.key] ?? option.key)}
                    </label>
                  ))}
                </div>
              )
            })}
          </fieldset>
        )}
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isPrimary} onChange={(e) => setIsPrimary(e.target.checked)} />
          {t('customers.contacts.primaryForType')}
        </label>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('ui.statusBadges.active')}
        </label>
      </form>
    </Modal>
  )
}
