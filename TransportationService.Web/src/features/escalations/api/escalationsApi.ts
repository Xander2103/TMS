import { apiClient } from '../../../api/apiClient'
import type { EscalationKind, EscalationPolicy, UpdateEscalationPolicyInput } from '../types'

export function listEscalationPolicies(): Promise<EscalationPolicy[]> {
  return apiClient.getJson<EscalationPolicy[]>('/api/escalation-policies')
}

export function updateEscalationPolicy(
  kind: EscalationKind,
  input: UpdateEscalationPolicyInput,
): Promise<EscalationPolicy> {
  return apiClient.putJson<EscalationPolicy, UpdateEscalationPolicyInput>(`/api/escalation-policies/${kind}`, input)
}
