import { apiClient } from '../../../api/apiClient'

/** Hoe een klantdoorrekening bij incidenten wordt afgehandeld. */
export type ChargePolicyMode = 'Never' | 'Propose' | 'Auto'

export const CHARGE_POLICY_MODE_LABELS: Record<ChargePolicyMode, string> = {
  Never: 'Nooit doorrekenen',
  Propose: 'Voorstellen ter goedkeuring',
  Auto: 'Automatisch doorrekenen',
}

/** Eén doorrekenbeleid; null bij customerId/incidentType = "geldt voor alle". */
export interface ChargePolicy {
  id: string
  customerId: string | null
  customerName: string | null
  incidentType: string | null
  mode: ChargePolicyMode
  defaultAmount: number | null
  defaultDescription: string | null
}

/** Opslag-payload; PUT vervangt de volledige beleidsset (replace-all). */
export interface ChargePolicyInput {
  customerId: string | null
  incidentType: string | null
  mode: ChargePolicyMode
  defaultAmount: number | null
  defaultDescription: string | null
}

export function listChargePolicies(): Promise<ChargePolicy[]> {
  return apiClient.getJson<ChargePolicy[]>('/api/settings/charge-policies')
}

/** Replace-all; de server valideert dat Auto een positief standaardbedrag heeft. */
export function saveChargePolicies(policies: ChargePolicyInput[]): Promise<unknown> {
  return apiClient.putJson<unknown, ChargePolicyInput[]>('/api/settings/charge-policies', policies)
}
