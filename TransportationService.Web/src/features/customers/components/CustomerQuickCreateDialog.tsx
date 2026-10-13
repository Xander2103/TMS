import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { getFieldError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { useCustomerMutations } from '../hooks/useCustomerMutations'
import { buildQuickCustomerInput, EMPTY_QUICK_CUSTOMER, type QuickCustomerFields } from '../customerQuickCreate'
import type { CustomerDetail } from '../types'
import './customer-quick-create.css'

interface CustomerQuickCreateDialogProps {
  /** What the planner typed in the customer search; pre-fills the name. */
  initialName?: string
  /** Resolves with the created customer, or null when the planner cancels (Escape, Annuleren, backdrop). */
  onClose: (created: CustomerDetail | null) => void
}

/**
 * "+ Nieuwe klant" from inside a dossier: a compact modal with the fields that matter at intake
 * (name, contact, address) that posts to the SAME `POST /api/customers` as the Klanten page via
 * `useCustomerMutations` — same validation, numbering, tenant and permission rules, no second
 * customer system. The host keeps its own form state untouched; it only receives the result.
 * Failure keeps the dialog open with the server's field errors; a submit in flight blocks a second one.
 */
export function CustomerQuickCreateDialog({ initialName = '', onClose }: CustomerQuickCreateDialogProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManageFiscal = hasPermission('customers.manage_fiscal')
  const { create, isSubmitting, error, fieldErrors } = useCustomerMutations()
  const [fields, setFields] = useState<QuickCustomerFields>({ ...EMPTY_QUICK_CUSTOMER, name: initialName.trim() })
  const [nameError, setNameError] = useState<string | null>(null)

  function set<K extends keyof QuickCustomerFields>(key: K, value: QuickCustomerFields[K]) {
    setFields((current) => ({ ...current, [key]: value }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // The dialog may sit inside a host page form: its submit must never bubble up to it.
    event.stopPropagation()
    if (isSubmitting) return
    if (!fields.name.trim()) {
      setNameError(t('customers.form.nameRequired'))
      return
    }
    setNameError(null)
    const created = await create(buildQuickCustomerInput(fields, { canManageFiscal }))
    if (created) onClose(created)
  }

  const fieldError = (key: string) => getFieldError(fieldErrors, key)

  return (
    <Modal
      title={t('customers.quickCreate.title')}
      onClose={() => onClose(null)}
      busy={isSubmitting}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(null)} disabled={isSubmitting}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="customer-quick-create" disabled={isSubmitting}>
            {isSubmitting ? t('customers.quickCreate.creating') : t('customers.quickCreate.create')}
          </Button>
        </>
      }
    >
      <form id="customer-quick-create" className="customer-quick-create" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <p className="customer-quick-create-intro">{t('customers.quickCreate.intro')}</p>
        <ValidationSummary message={error} />
        <div className="customer-quick-create-row">
          <FormField label={t('customers.quickCreate.name')} htmlFor="cqc-name" required error={nameError ?? fieldError('name')}>
            <input
              id="cqc-name"
              value={fields.name}
              placeholder={t('customers.quickCreate.namePlaceholder')}
              onChange={(e) => set('name', e.target.value)}
              disabled={isSubmitting}
              maxLength={200}
              autoFocus
              aria-invalid={nameError || fieldError('name') ? true : undefined}
            />
          </FormField>
          {canManageFiscal && (
            <FormField label={t('customers.quickCreate.vatNumber')} htmlFor="cqc-vat" error={fieldError('vatNumber')} hint={t('customers.quickCreate.vatHint')}>
              <input id="cqc-vat" value={fields.vatNumber} onChange={(e) => set('vatNumber', e.target.value)} disabled={isSubmitting} maxLength={30} />
            </FormField>
          )}
        </div>
        <div className="customer-quick-create-row">
          <FormField label={t('customers.quickCreate.email')} htmlFor="cqc-email" error={fieldError('email')}>
            <input id="cqc-email" type="email" value={fields.email} onChange={(e) => set('email', e.target.value)} disabled={isSubmitting} maxLength={200} />
          </FormField>
          <FormField label={t('customers.quickCreate.phone')} htmlFor="cqc-phone" error={fieldError('phoneNumber')}>
            <input id="cqc-phone" type="tel" value={fields.phoneNumber} onChange={(e) => set('phoneNumber', e.target.value)} disabled={isSubmitting} maxLength={50} />
          </FormField>
        </div>
        <fieldset className="customer-quick-create-address">
          <legend>{t('customers.quickCreate.addressLegend')}</legend>
          <div className="customer-quick-create-row customer-quick-create-row-street">
            <FormField label={t('customers.quickCreate.street')} htmlFor="cqc-street" error={fieldError('street')}>
              <input id="cqc-street" value={fields.street} onChange={(e) => set('street', e.target.value)} disabled={isSubmitting} maxLength={200} />
            </FormField>
            <FormField label={t('customers.quickCreate.houseNumber')} htmlFor="cqc-nr" error={fieldError('houseNumber')}>
              <input id="cqc-nr" value={fields.houseNumber} onChange={(e) => set('houseNumber', e.target.value)} disabled={isSubmitting} maxLength={20} />
            </FormField>
          </div>
          <div className="customer-quick-create-row customer-quick-create-row-city">
            <FormField label={t('customers.quickCreate.postalCode')} htmlFor="cqc-postal" error={fieldError('postalCode')}>
              <input id="cqc-postal" value={fields.postalCode} onChange={(e) => set('postalCode', e.target.value)} disabled={isSubmitting} maxLength={20} />
            </FormField>
            <FormField label={t('customers.quickCreate.city')} htmlFor="cqc-city" error={fieldError('city')}>
              <input id="cqc-city" value={fields.city} onChange={(e) => set('city', e.target.value)} disabled={isSubmitting} maxLength={100} />
            </FormField>
            <FormField label={t('customers.quickCreate.country')} htmlFor="cqc-country" error={fieldError('countryCode')}>
              <CountryCombobox id="cqc-country" value={fields.countryCode} onChange={(code) => set('countryCode', code)} disabled={isSubmitting} />
            </FormField>
          </div>
        </fieldset>
      </form>
    </Modal>
  )
}
