import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { VAT_TREATMENT_LABEL_KEYS } from '../../customers/types'
import type { VatTreatment } from '../../customers/types'
import type { OrderCustomerChangeImpact } from '../api/transportOrdersApi'

interface CustomerChangeImpactListProps {
  impact: OrderCustomerChangeImpact
  /** Hide the entity/language/treatment lines when a parent (dossier) already shows them. */
  compact?: boolean
}

function treatmentLabel(t: (key: string, params?: Record<string, string | number>) => string, treatment: string | null): string {
  if (!treatment) return '—'
  const key = VAT_TREATMENT_LABEL_KEYS[treatment as VatTreatment]
  return key ? t(key) : treatment
}

/**
 * Sprint 6: the per-order consequences of a customer change, worded for the operator. Every
 * number comes from the backend preview — nothing here is guessed on the client.
 */
export function CustomerChangeImpactList({ impact, compact = false }: CustomerChangeImpactListProps) {
  const { t } = useLocale()
  const k = 'transportOrders.customerChange.impact.'

  if (impact.blockedReason) {
    return (
      <div className="to-impact-blocked" role="alert">
        <strong>{t('transportOrders.customerChange.blocked')}</strong>
        <p>{impact.blockedReason}</p>
        {impact.owningDossierId && impact.owningDossierNumber && (
          <Link to={`/dossiers/${impact.owningDossierId}`}>
            {t('transportOrders.customerChange.openDossier', { number: impact.owningDossierNumber })}
          </Link>
        )}
      </div>
    )
  }

  return (
    <ul className="to-impact-list">
      {impact.automaticLinesInvalidated > 0 && <li>{t(`${k}autoInvalidated`, { count: impact.automaticLinesInvalidated })}</li>}
      {impact.manualLinesKept > 0 && <li>{t(`${k}manualKept`, { count: impact.manualLinesKept })}</li>}
      {impact.adjustedLinesFlaggedForReview > 0 && (
        <li className="to-impact-warning">{t(`${k}adjustedReview`, { count: impact.adjustedLinesFlaggedForReview })}</li>
      )}
      <li className={impact.needsPricingReview ? 'to-impact-warning' : undefined}>
        {impact.needsPricingReview ? t(`${k}needsReview`) : t(`${k}priceRecalc`)}
      </li>
      {impact.draftInvoiceLinesReleased > 0 && (
        <li className="to-impact-warning">{t(`${k}draftReleased`, { count: impact.draftInvoiceLinesReleased })}</li>
      )}
      {!compact && (
        <>
          <li>{impact.legalEntityChanges ? t(`${k}entityChanges`) : t(`${k}entitySame`)}</li>
          {impact.newInvoiceLanguage && <li>{t(`${k}language`, { language: impact.newInvoiceLanguage.toUpperCase() })}</li>}
          {impact.newVatTreatment && <li>{t(`${k}vatTreatment`, { treatment: treatmentLabel(t, impact.newVatTreatment) })}</li>}
        </>
      )}
      <li className="customer-form-muted">
        {t(`${k}kept`, { stops: impact.stopsKept, goods: impact.goodsKept, documents: impact.documentsKept })}
      </li>
    </ul>
  )
}
