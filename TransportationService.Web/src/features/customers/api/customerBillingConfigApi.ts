import { apiClient } from '../../../api/apiClient'
import type {
  CustomerDieselSurcharge,
  CustomerPoPolicy,
  PurchaseOrderPolicy,
  SaveCustomerPoNumberInput,
} from '../types'

// Dieseltoeslag: GET vereist customers.view, PUT customers.manage_surcharge.

export function getDieselSurcharge(customerId: string): Promise<CustomerDieselSurcharge> {
  return apiClient.getJson<CustomerDieselSurcharge>(`/api/customers/${customerId}/diesel-surcharge`)
}

export function saveDieselSurcharge(
  customerId: string,
  input: CustomerDieselSurcharge,
): Promise<CustomerDieselSurcharge> {
  return apiClient.putJson<CustomerDieselSurcharge, CustomerDieselSurcharge>(
    `/api/customers/${customerId}/diesel-surcharge`,
    input,
  )
}

// PO-beleid en PO-historiek: GET vereist customers.view, mutaties customers.manage_po.

export function getPoPolicy(customerId: string): Promise<CustomerPoPolicy> {
  return apiClient.getJson<CustomerPoPolicy>(`/api/customers/${customerId}/po-policy`)
}

export function setPoPolicy(customerId: string, policy: PurchaseOrderPolicy): Promise<CustomerPoPolicy> {
  return apiClient.putJson<CustomerPoPolicy, { policy: PurchaseOrderPolicy }>(
    `/api/customers/${customerId}/po-policy`,
    { policy },
  )
}

export function addPoNumber(customerId: string, input: SaveCustomerPoNumberInput): Promise<CustomerPoPolicy> {
  return apiClient.postJson<CustomerPoPolicy, SaveCustomerPoNumberInput>(
    `/api/customers/${customerId}/po-numbers`,
    input,
  )
}

export function updatePoNumber(
  customerId: string,
  poId: string,
  input: SaveCustomerPoNumberInput,
): Promise<CustomerPoPolicy> {
  return apiClient.putJson<CustomerPoPolicy, SaveCustomerPoNumberInput>(
    `/api/customers/${customerId}/po-numbers/${poId}`,
    input,
  )
}

export function deletePoNumber(customerId: string, poId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/customers/${customerId}/po-numbers/${poId}`)
}
