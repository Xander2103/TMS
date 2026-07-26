import type { BadgeTone } from '../../components/ui/Badge'
import type { PricingAgreement } from './api/pricingApi'

export type AgreementStatus = 'Actief' | 'Toekomstig' | 'Verlopen' | 'Inactief'

const today = () => new Date().toISOString().slice(0, 10)

/** Actief = IsActive && window covers today; Toekomstig/Verlopen from the window; else Inactief. */
export function agreementStatus(agreement: Pick<PricingAgreement, 'isActive' | 'effectiveFrom' | 'effectiveUntil'>): AgreementStatus {
  if (!agreement.isActive) return 'Inactief'
  const now = today()
  if (agreement.effectiveFrom > now) return 'Toekomstig'
  if (agreement.effectiveUntil && agreement.effectiveUntil < now) return 'Verlopen'
  return 'Actief'
}

export const AGREEMENT_STATUS_TONE: Record<AgreementStatus, BadgeTone> = {
  Actief: 'success',
  Toekomstig: 'info',
  Verlopen: 'neutral',
  Inactief: 'neutral',
}

export function agreementSamenstelling(agreement: Pick<PricingAgreement, 'isShared' | 'customerId' | 'customerName'>): string {
  if (agreement.isShared) return 'Gedeeld'
  if (agreement.customerId) return `Klant: ${agreement.customerName ?? '—'}`
  return 'Algemeen'
}
