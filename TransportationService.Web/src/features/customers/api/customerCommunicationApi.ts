import { apiClient } from '../../../api/apiClient'
import type { CustomerCommunicationRule, SaveCommunicationRuleInput } from '../types'

/** Communicatieregels van een klant (GET vereist customers.view; mutaties customers.manage_communication). */
export function listCommunicationRules(customerId: string): Promise<CustomerCommunicationRule[]> {
  return apiClient.getJson<CustomerCommunicationRule[]>(`/api/customers/${customerId}/communication-rules`)
}

export function createCommunicationRule(
  customerId: string,
  input: SaveCommunicationRuleInput,
): Promise<CustomerCommunicationRule> {
  return apiClient.postJson<CustomerCommunicationRule, SaveCommunicationRuleInput>(
    `/api/customers/${customerId}/communication-rules`,
    input,
  )
}

export function updateCommunicationRule(
  customerId: string,
  ruleId: string,
  input: SaveCommunicationRuleInput,
): Promise<CustomerCommunicationRule> {
  return apiClient.putJson<CustomerCommunicationRule, SaveCommunicationRuleInput>(
    `/api/customers/${customerId}/communication-rules/${ruleId}`,
    input,
  )
}

export function deleteCommunicationRule(customerId: string, ruleId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/customers/${customerId}/communication-rules/${ruleId}`)
}
