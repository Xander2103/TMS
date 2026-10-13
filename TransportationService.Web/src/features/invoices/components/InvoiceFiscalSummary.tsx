import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import type { InvoiceDetail, InvoiceLine } from '../types'
import { fiscalTreatmentLabel, isLineFiscalException, sourceLabel } from '../utils/invoiceFiscal'

/** Sprint 5: the invoice-level fiscal summary — treatment, language, statutory text, and whether it is frozen. */
export function InvoiceFiscalSummary({ invoice }: { invoice: InvoiceDetail }) {
  const { t } = useLocale()
  const treatment = fiscalTreatmentLabel(t, invoice.customerVatTreatment)
  if (!treatment && !invoice.languageCode) return null

  const language = invoice.languageCode
    ? ['nl', 'fr', 'en', 'de'].includes(invoice.languageCode.toLowerCase())
      ? t(`invoices.fiscal.languages.${invoice.languageCode.toLowerCase()}`)
      : invoice.languageCode.toUpperCase()
    : null
  const exceptions = invoice.lines.filter(isLineFiscalException).length

  return (
    <p className="inv-meta inv-fiscal" data-testid="invoice-fiscal-summary">
      {treatment && (
        <>
          {t('invoices.fiscal.treatment')}: <strong>{treatment}</strong>{' '}
          <span className="customer-form-muted">
            ({t('invoices.fiscal.sourceLabel')}: {t('invoices.fiscal.source.Customer')})
          </span>
        </>
      )}
      {language && <> · {t('invoices.fiscal.language')}: {language}</>}
      {invoice.vatLegalText && <> · {t('invoices.fiscal.legalText')}: <em>{invoice.vatLegalText}</em></>}
      {' '}
      <span className="customer-form-muted">
        {invoice.status === 'Draft' ? t('invoices.fiscal.draftNote') : t('invoices.fiscal.frozenNote')}
      </span>
      {exceptions > 0 && (
        <>
          {' '}
          <Badge tone="warning">{exceptions}</Badge>
        </>
      )}
    </p>
  )
}

/** The per-line exception marker: only rendered when the line's treatment does not come from the customer. */
export function InvoiceLineFiscalBadge({ line }: { line: InvoiceLine }) {
  const { t } = useLocale()
  if (!isLineFiscalException(line) || !line.vatTreatmentSource) return null
  const treatment = fiscalTreatmentLabel(t, line.vatTreatment) ?? '—'
  const source = sourceLabel(t, line.vatTreatmentSource)
  const detail = line.vatTreatmentSource === 'SalesCode' && line.salesCode ? `${source} ${line.salesCode}` : source
  return (
    <div>
      <Badge tone="warning">{t('invoices.fiscal.lineException', { treatment, source: detail })}</Badge>
      {line.vatLegalText && <span className="customer-form-muted"> {line.vatLegalText}</span>}
    </div>
  )
}
