import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Badge } from '../../../components/ui/Badge'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
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
  onAdd: (input: CustomerContactInput) => Promise<CustomerContact | null>
  onUpdate: (contactId: string, input: CustomerContactInput) => Promise<boolean>
  onRemove: (contactId: string) => Promise<boolean>
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

export function CustomerContactsPanel({ customerId, contacts, isSubmitting, onAdd, onUpdate, onRemove }: CustomerContactsPanelProps) {
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

  return (
    <div className="customer-contacts">
      <div className="page-header">
        <h3 style={{ margin: 0 }}>{t('customers.contacts.title')}</h3>
        <Button variant="secondary" onClick={() => setDialog({ mode: 'create' })}>
          {t('customers.contacts.addContact')}
        </Button>
      </div>

      <div className="customer-locations-toolbar">
        <label className="customer-form-muted" htmlFor="ct-type-filter">
          {t('customers.contacts.type')}
        </label>
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
      </div>

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
          onSubmit={async (input, notifications) => {
            // A new contact has no id until it exists, so the notification choices are stored
            // right after the contact itself is created.
            const contactId =
              dialog.mode === 'edit'
                ? (await onUpdate(dialog.contact.id, input)) ? dialog.contact.id : null
                : ((await onAdd(input))?.id ?? null)
            if (!contactId) return
            // Only an actual change is written: an untouched card must never rewrite the
            // routing underneath (advanced rules stay exactly as the administrator left them).
            if (canManageNotifications && !sameKeys(notifications.keys, notifications.initialKeys)) {
              try {
                await setContactNotifications(customerId, contactId, notifications.keys)
              } catch (err) {
                // The contact itself is saved; say so instead of silently losing the choice.
                toast.showError(describeApiError(err, t('customers.notifications.saveFailed')).message)
              }
            }
            setDialog(null)
          }}
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

/** What the dialog hands back about "Ontvangt meldingen": the chosen keys and what was preloaded. */
interface NotificationSelection {
  keys: string[]
  initialKeys: string[]
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
  onSubmit: (input: CustomerContactInput, notifications: NotificationSelection) => void
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
  // "Ontvangt meldingen" (sprint 3): the business question, not a routing rule.
  const [options, setOptions] = useState<CustomerNotificationOption[]>([])
  const [notificationKeys, setNotificationKeys] = useState<string[]>([])
  // What the contact already received when the dialog opened; saving compares against this.
  const [initialNotificationKeys, setInitialNotificationKeys] = useState<string[]>([])
  // A stored language outside the offered list (e.g. "it") must survive a save untouched.
  const storedLanguage = contact?.preferredLanguageCode ?? ''
  const hasOtherLanguage = storedLanguage !== '' && !(CONTACT_LANGUAGES as readonly string[]).includes(storedLanguage)

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
    if (!contact || !canManageNotifications) return
    let active = true
    void getContactNotifications(customerId, contact.id)
      .then((data) => {
        if (!active) return
        setNotificationKeys(data.optionKeys)
        setInitialNotificationKeys(data.optionKeys)
      })
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [customerId, contact, canManageNotifications])

  function toggleNotification(key: string, on: boolean) {
    setNotificationKeys((keys) => (on ? [...new Set([...keys, key])] : keys.filter((k) => k !== key)))
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
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
    onSubmit({
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
    }, { keys: notificationKeys, initialKeys: initialNotificationKeys })
  }

  return (
    <Modal
      title={contact ? t('customers.contacts.editTitle') : t('customers.contacts.newTitle')}
      onClose={onClose}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={isSubmitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="contact-form" disabled={isSubmitting}>
            {isSubmitting ? t('customers.common.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="contact-form" onSubmit={handleSubmit} className="customer-form">
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
