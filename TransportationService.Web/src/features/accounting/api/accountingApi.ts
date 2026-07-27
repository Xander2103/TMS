import { apiClient } from '../../../api/apiClient'

export interface LedgerAccount {
  id: string
  accountNumber: string
  name: string
  externalCode: string | null
  description: string | null
  isActive: boolean
}

export interface LedgerAccountInput {
  accountNumber: string
  name: string
  externalCode?: string | null
  description?: string | null
  isActive: boolean
}

export type SalesCategorySystemRole = 'None' | 'Transport' | 'Surcharge' | 'Diesel'

export interface SalesCategory {
  id: string
  code: string
  name: string
  systemRole: SalesCategorySystemRole
  ledgerAccountId: string | null
  ledgerAccountNumber: string | null
  ledgerAccountName: string | null
  isActive: boolean
  sortOrder: number
}

export interface SalesCategoryInput {
  code: string
  name: string
  systemRole: SalesCategorySystemRole
  ledgerAccountId: string | null
  isActive: boolean
  sortOrder: number
}

export interface AccountingHealth {
  unmappedCategories: SalesCategory[]
}

export const listLedgerAccounts = (includeInactive = false): Promise<LedgerAccount[]> =>
  apiClient.getJson(`/api/accounting/ledger-accounts${includeInactive ? '?includeInactive=true' : ''}`)
export const createLedgerAccount = (input: LedgerAccountInput): Promise<LedgerAccount> =>
  apiClient.postJson('/api/accounting/ledger-accounts', input)
export const updateLedgerAccount = (id: string, input: LedgerAccountInput): Promise<LedgerAccount> =>
  apiClient.putJson(`/api/accounting/ledger-accounts/${id}`, input)
export const deleteLedgerAccount = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/accounting/ledger-accounts/${id}`)

export const listSalesCategories = (includeInactive = false): Promise<SalesCategory[]> =>
  apiClient.getJson(`/api/accounting/sales-categories${includeInactive ? '?includeInactive=true' : ''}`)
export const createSalesCategory = (input: SalesCategoryInput): Promise<SalesCategory> =>
  apiClient.postJson('/api/accounting/sales-categories', input)
export const updateSalesCategory = (id: string, input: SalesCategoryInput): Promise<SalesCategory> =>
  apiClient.putJson(`/api/accounting/sales-categories/${id}`, input)

export const getAccountingHealth = (): Promise<AccountingHealth> => apiClient.getJson('/api/accounting/health')

export const SYSTEM_ROLE_LABELS: Record<SalesCategorySystemRole, string> = {
  None: 'Handmatig te kiezen',
  Transport: 'Transportlijn (automatisch)',
  Surcharge: 'Diensten & toeslagen (automatisch)',
  Diesel: 'Dieseltoeslag (automatisch)',
}
