import { ApiError, apiClient } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'

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

export interface SalesCategoryWave2Fields {
  /** Invoice-line text for this sales code; null falls back to name. */
  invoiceDescriptionNl?: string | null
  /** Default managed unit for manual invoice lines with this code. */
  defaultUnitCode?: string | null
  /** UNCL5305 VAT category forced by this code; null = customer VAT treatment decides. */
  vatCategoryOverride?: string | null
}

export interface SalesCategory extends SalesCategoryWave2Fields {
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
  invoiceDescriptionNl?: string | null
  defaultUnitCode?: string | null
  vatCategoryOverride?: string | null
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

/**
 * Downloads the XLSX accounting export for the invoice-date window. The backend blocks with a
 * clear Dutch message when any Sent/Paid line in the window misses a frozen ledger account.
 */
export async function downloadAccountingExport(from: string, to: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/accounting/export?from=${from}&to=${to}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    let message = 'De boekhoudexport kon niet worden gemaakt.'
    try {
      const data = (await response.json()) as { detail?: string; message?: string }
      message = data.detail ?? data.message ?? message
    } catch {
      // keep fallback
    }
    throw new ApiError(message, response.status)
  }
  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `boekhoudexport-${from}-${to}.xlsx`
  anchor.click()
  URL.revokeObjectURL(url)
}

/** Vertaalsleutels — renderen als t(SYSTEM_ROLE_LABELS[role]). */
export const SYSTEM_ROLE_LABELS: Record<SalesCategorySystemRole, string> = {
  None: 'accounting.systemRole.None',
  Transport: 'accounting.systemRole.Transport',
  Surcharge: 'accounting.systemRole.Surcharge',
  Diesel: 'accounting.systemRole.Diesel',
}
