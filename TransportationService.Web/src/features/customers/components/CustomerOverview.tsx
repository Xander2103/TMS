import type { ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { DetailCard } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { useLocale } from '../../../i18n/localeContext'
import {
  CUSTOMER_DOCUMENT_STRATEGY_LABEL_KEYS,
  VAT_TREATMENT_LABEL_KEYS,
  type CustomerDetail,
  type CustomerDocumentStrategy,
  type PeppolValidationStatus,
} from '../types'
import { formatCustomerAddress } from '../utils/customerAddress'
import { CustomerOpenDossiersCard, CustomerRecentInvoicesCard } from './CustomerActivityCards'

const PEPPOL_STATUS_KEYS: Record<PeppolValidationStatus, string> = {
  Found: 'customers.peppolStatus.auto',
  NotFound: 'customers.peppolStatus.notFound',
  Unknown: 'customers.peppolStatus.notValidated',
}

interface CustomerOverviewProps {
  customer: CustomerDetail
  /** Opens the edit form on a section; omitted when the user may not edit. */
  onEdit?: (section: 'klantgegevens' | 'fiscaal') => void
  canViewDossiers: boolean
  canViewInvoices: boolean
}

/**
 * B17 overview — read first: who the customer is and how to reach them, how they are invoiced,
 * and what is running for them (open dossiers, latest invoices). Four calm cards instead of a
 * dashboard; each identity card has its own quiet "Bewerken" that opens the form on its section.
 * The activity cards are only mounted when the user may see that data, so no request is made
 * for data the user has no right to.
 */
export function CustomerOverview({ customer, onEdit, canViewDossiers, canViewInvoices }: CustomerOverviewProps) {
  const { t } = useLocale()
  const primaryContact = customer.contacts.find((c) => c.isPrimary && c.isActive) ?? null
  const editButton = (section: 'klantgegevens' | 'fiscaal', cardTitle: string) =>
    onEdit ? (
      <Button variant="ghost" aria-label={t('customers.overview.editCard', { title: cardTitle })} onClick={() => onEdit(section)}>
        {t('ui.actions.edit')}
      </Button>
    ) : undefined
  const link = (href: string, text: string): ReactNode => <a href={href}>{text}</a>

  const peppol = customer.peppolEnabled
    ? `${customer.peppolScheme ?? '?'}:${customer.peppolId ?? '—'} · ${t(PEPPOL_STATUS_KEYS[customer.peppolValidationStatus])}`
    : customer.peppolId
      ? `${customer.peppolScheme ?? '?'}:${customer.peppolId} · ${t('customers.overview.peppolOff')}`
      : t('customers.detail.peppolNotConfigured')

  return (
    <div className="customer-overview">
      <div className="customer-overview-col">
        <DetailCard
          title={t('customers.form.sections.klantgegevens')}
          action={editButton('klantgegevens', t('customers.form.sections.klantgegevens'))}
        >
          <FactList
            facts={[
              { label: t('customers.fields.customerNumber'), value: <code>{customer.customerNumber}</code> },
              customer.legalName && customer.legalName !== customer.name ? { label: t('customers.overview.legalName'), value: customer.legalName } : null,
              customer.nickname ? { label: t('customers.fields.nickname'), value: customer.nickname } : null,
              customer.categoryName ? { label: t('customers.form.category'), value: customer.categoryName } : null,
              { label: t('customers.overview.primaryAddress'), value: formatCustomerAddress(customer) },
              { label: t('customers.contacts.email'), value: customer.email ? link(`mailto:${customer.email}`, customer.email) : null },
              { label: t('customers.contacts.phone'), value: customer.phoneNumber ? link(`tel:${customer.phoneNumber}`, customer.phoneNumber) : null },
              customer.website ? { label: t('customers.overview.website'), value: customer.website } : null,
              primaryContact
                ? {
                    label: t('customers.overview.primaryContact'),
                    value: [
                      primaryContact.displayName || `${primaryContact.firstName} ${primaryContact.lastName}`.trim(),
                      primaryContact.role,
                      primaryContact.phoneNumber ?? primaryContact.mobilePhone,
                      primaryContact.email,
                    ]
                      .filter(Boolean)
                      .join(' · '),
                  }
                : null,
              customer.notes ? { label: t('customers.fields.notes'), value: customer.notes, wide: true } : null,
            ]}
          />
        </DetailCard>

        <DetailCard title={t('customers.overview.billingTitle')} action={editButton('fiscaal', t('customers.overview.billingTitle'))}>
          <FactList
            facts={[
              { label: t('customers.fields.vatNumber'), value: customer.vatNumber },
              customer.companyNumber ? { label: t('customers.fields.companyNumber'), value: customer.companyNumber } : null,
              {
                label: t('customers.form.vatTreatment'),
                value: `${t(VAT_TREATMENT_LABEL_KEYS[customer.vatTreatment])}${
                  customer.defaultVatRatePercent !== null ? ` · ${customer.defaultVatRatePercent}%` : ''
                }`,
              },
              { label: 'Peppol', value: peppol },
              { label: t('customers.form.invoiceEmail'), value: customer.invoiceEmail },
              {
                label: t('customers.detail.paymentTermLabel'),
                // 0 = no own term: the billing entity / company term applies.
                value: customer.paymentTermDays
                  ? t('customers.detail.paymentTermDays', { days: customer.paymentTermDays })
                  : t('customers.detail.paymentTermFallback'),
              },
              customer.invoiceGrouping
                ? { label: t('customers.overview.invoiceGrouping'), value: t(`customers.invoiceGrouping.${customer.invoiceGrouping}`) }
                : null,
              customer.documentStrategy && customer.documentStrategy in CUSTOMER_DOCUMENT_STRATEGY_LABEL_KEYS
                ? {
                    label: t('customers.overview.documentStrategy'),
                    value: t(CUSTOMER_DOCUMENT_STRATEGY_LABEL_KEYS[customer.documentStrategy as CustomerDocumentStrategy]),
                  }
                : null,
              customer.buyerReference ? { label: t('customers.fields.buyerReference'), value: customer.buyerReference } : null,
              customer.iban ? { label: t('customers.fields.iban'), value: `${customer.iban}${customer.bic ? ` (${customer.bic})` : ''}` } : null,
              customer.currencyCode && customer.currencyCode !== 'EUR' ? { label: t('customers.fields.currencyCode'), value: customer.currencyCode } : null,
              customer.vatNotes ? { label: t('customers.form.vatNotes'), value: customer.vatNotes, wide: true } : null,
            ]}
          />
        </DetailCard>
      </div>

      {(canViewDossiers || canViewInvoices) && (
        <div className="customer-overview-col">
          {canViewDossiers && <CustomerOpenDossiersCard customerId={customer.id} />}
          {canViewInvoices && <CustomerRecentInvoicesCard customerId={customer.id} customerName={customer.name} />}
        </div>
      )}
    </div>
  )
}
