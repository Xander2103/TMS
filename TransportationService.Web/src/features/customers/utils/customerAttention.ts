import type { AttentionItem } from '../../../components/ui/AttentionList'
import type { TranslateFn } from '../../../i18n/localeContext'
import type { CustomerFiscalWarning } from '../api/customersApi'
import type { CustomerDetail } from '../types'

const KNOWN_FISCAL_CODES = new Set(['vat-number-missing', 'domestic-vat-foreign-customer', 'intra-community-same-country'])

interface AttentionHandlers {
  /** Opens the edit form on a section; omitted when the user may not edit. */
  onComplete?: (section: 'klantgegevens' | 'fiscaal' | 'facturatie') => void
  /** Opens the unblock confirmation; omitted when the user may not edit. */
  onUnblock?: () => void
}

/**
 * B17: what a planner or accountant must know before working with this customer, strongest
 * first. Only facts the data actually supports:
 * - critical: the customer is blocked;
 * - warning: the backend's advisory fiscal notices, Peppol switched on without an id or with an
 *   id the network did not find, and no way at all to deliver an invoice;
 * - info: the commercial requirements every order must meet (PO, signed delivery note,
 *   customer reference) — normal configuration, but what a planner must not forget.
 * A customer with nothing to act on gets an empty list (the strip then renders nothing).
 */
export function buildCustomerAttention(
  customer: CustomerDetail,
  fiscalWarnings: CustomerFiscalWarning[],
  t: TranslateFn,
  handlers: AttentionHandlers = {},
): AttentionItem[] {
  const complete = (section: 'klantgegevens' | 'fiscaal' | 'facturatie') =>
    handlers.onComplete ? { label: t('customers.attention.complete'), onSelect: () => handlers.onComplete!(section) } : undefined
  const items: AttentionItem[] = []

  if (customer.isBlocked) {
    items.push({
      key: 'blocked',
      severity: 'critical',
      text: t('customers.attention.blocked'),
      detail: customer.blockReason ?? undefined,
      action: handlers.onUnblock ? { label: t('customers.detail.unblock'), onSelect: handlers.onUnblock } : undefined,
    })
  }

  for (const warning of fiscalWarnings) {
    items.push({
      key: `fiscal-${warning.code}`,
      severity: 'warning',
      text: KNOWN_FISCAL_CODES.has(warning.code)
        ? t(`customers.fiscalWarnings.${warning.code}`, { country: customer.vatCountryCode ?? customer.countryCode ?? '—' })
        : warning.message,
      action: complete('fiscaal'),
    })
  }

  if (customer.peppolEnabled && !customer.peppolId) {
    items.push({ key: 'peppol-id', severity: 'warning', text: t('customers.attention.peppolIdMissing'), action: complete('fiscaal') })
  } else if (customer.peppolEnabled && customer.peppolValidationStatus === 'NotFound') {
    items.push({ key: 'peppol-not-found', severity: 'warning', text: t('customers.attention.peppolNotFound'), action: complete('fiscaal') })
  }

  if (!customer.peppolEnabled && !customer.invoiceEmail && !customer.email) {
    items.push({ key: 'no-invoice-channel', severity: 'warning', text: t('customers.attention.noInvoiceChannel'), action: complete('facturatie') })
  }

  if (customer.purchaseOrderRequired) {
    items.push({ key: 'po', severity: 'info', text: t('customers.detail.requirementPurchaseOrder') })
  }
  if (customer.signedDeliveryNoteRequired) {
    items.push({ key: 'signed-note', severity: 'info', text: t('customers.detail.requirementSignedNote') })
  }
  if (customer.customerReferenceRequired) {
    items.push({ key: 'reference', severity: 'info', text: t('customers.detail.requirementCustomerReference') })
  }

  return items
}
