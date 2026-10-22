import { apiClient, LONG_RUNNING_TIMEOUT_MS } from '../../../api/apiClient'

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

/** The four supported invoice languages for an approved sales-code description. */
export const SALES_CODE_LANGUAGES = ['nl', 'fr', 'en', 'de'] as const

/**
 * Sprint 5 — the commercial article master. Descriptions are APPROVED master data: the invoice
 * uses the customer's invoice language and never translates on the fly.
 */
export interface SalesCodeFields {
  invoiceDescriptionFr?: string | null
  invoiceDescriptionEn?: string | null
  invoiceDescriptionDe?: string | null
  /** "Meetellen in basis dieseltoeslag". */
  includeInDieselBase?: boolean
  /** Exceptional statutory classification; null = the customer's treatment decides. */
  vatTreatmentOverride?: string | null
  costCentre?: string | null
  defaultUnitPrice?: number | null
  defaultPricingBasis?: string | null
  notes?: string | null
  /** Per-invoicing-entity ledger overrides. */
  ledgerMappings?: SalesCategoryLedgerMapping[] | null
}

export interface SalesCategoryLedgerMapping {
  legalEntityId: string
  ledgerAccountId: string
  costCentre: string | null
}

export interface SalesCategory extends SalesCategoryWave2Fields, SalesCodeFields {
  id: string
  code: string
  name: string
  systemRole: SalesCategorySystemRole
  ledgerAccountId: string | null
  ledgerAccountNumber: string | null
  ledgerAccountName: string | null
  isActive: boolean
  sortOrder: number
  /** MD-M2: optimistic-concurrency token; echoed as `expectedVersion`. Absent on older payloads. */
  version?: number
}

export interface SalesCategoryInput extends SalesCodeFields {
  code: string
  name: string
  systemRole: SalesCategorySystemRole
  ledgerAccountId: string | null
  invoiceDescriptionNl?: string | null
  defaultUnitCode?: string | null
  vatCategoryOverride?: string | null
  isActive: boolean
  sortOrder: number
  /** MD-M2: the version the dialog was opened on; a stale one yields 409 concurrency_conflict. */
  expectedVersion?: number
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
/**
 * MD-H1: the inline default-ledger control on a category row. Changes ONLY the default ledger
 * account — never route it through the full PUT, which would rewrite every other field.
 */
export const setSalesCategoryLedgerAccount = (
  id: string,
  ledgerAccountId: string | null,
  expectedVersion?: number,
): Promise<SalesCategory> =>
  apiClient.putJson(`/api/accounting/sales-categories/${id}/ledger-account`, { ledgerAccountId, expectedVersion })

export const getAccountingHealth = (): Promise<AccountingHealth> => apiClient.getJson('/api/accounting/health')

/**
 * Downloads the XLSX accounting export for the invoice-date window. The backend blocks with a
 * clear Dutch message when any Sent/Paid line in the window misses a frozen ledger account.
 */
export function downloadAccountingExport(from: string, to: string): Promise<void> {
  // PERF-H7: through apiClient (bearer + refresh, ProblemDetails message) with the long-running deadline.
  return apiClient.downloadFile(`/api/accounting/export?from=${from}&to=${to}`, `boekhoudexport-${from}-${to}.xlsx`, {
    timeoutMs: LONG_RUNNING_TIMEOUT_MS,
  })
}

/** Vertaalsleutels — renderen als t(SYSTEM_ROLE_LABELS[role]). */
export const SYSTEM_ROLE_LABELS: Record<SalesCategorySystemRole, string> = {
  None: 'accounting.systemRole.None',
  Transport: 'accounting.systemRole.Transport',
  Surcharge: 'accounting.systemRole.Surcharge',
  Diesel: 'accounting.systemRole.Diesel',
}
