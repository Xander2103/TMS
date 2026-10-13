import type { BadgeTone } from '../../components/ui/Badge'
import type { PricingAgreement } from './api/pricingApi'

/** Stabiele statuscodes (§82): logica en tone-maps vergelijken hierop, nooit op vertaalde labels. */
export type AgreementStatus = 'Active' | 'Future' | 'Expired' | 'Inactive'

const today = () => new Date().toISOString().slice(0, 10)

/** Active = IsActive && window covers today; Future/Expired from the window; else Inactive. */
export function agreementStatus(agreement: Pick<PricingAgreement, 'isActive' | 'effectiveFrom' | 'effectiveUntil'>): AgreementStatus {
  if (!agreement.isActive) return 'Inactive'
  const now = today()
  if (agreement.effectiveFrom > now) return 'Future'
  if (agreement.effectiveUntil && agreement.effectiveUntil < now) return 'Expired'
  return 'Active'
}

/** Vertaalsleutels — renderen als t(AGREEMENT_STATUS_LABELS[status]). */
export const AGREEMENT_STATUS_LABELS: Record<AgreementStatus, string> = {
  Active: 'tarification.status.Active',
  Future: 'tarification.status.Future',
  Expired: 'tarification.status.Expired',
  Inactive: 'tarification.status.Inactive',
}

export const AGREEMENT_STATUS_TONE: Record<AgreementStatus, BadgeTone> = {
  Active: 'success',
  Future: 'info',
  Expired: 'neutral',
  Inactive: 'neutral',
}

export interface AgreementComposition {
  key: string
  params?: Record<string, string | number>
}

/** Vertaalsleutel + params voor de samenstellingsbadge — renderen als t(result.key, result.params). */
export function agreementSamenstelling(
  agreement: Pick<PricingAgreement, 'isShared' | 'customerId' | 'customerName'>,
): AgreementComposition {
  if (agreement.isShared) return { key: 'tarification.composition.shared' }
  if (agreement.customerId) {
    return { key: 'tarification.composition.customer', params: { name: agreement.customerName ?? '—' } }
  }
  return { key: 'tarification.composition.general' }
}
