import { apiClient } from '../../../api/apiClient'

/** Welk transportdocument een regel afdwingt. */
export type DocumentRuleKind = 'DeliveryNote' | 'Cmr' | 'None'

export const DOCUMENT_RULE_KIND_LABELS: Record<DocumentRuleKind, string> = {
  DeliveryNote: 'Leveringsbon',
  Cmr: 'CMR',
  None: 'Geen document',
}

/** Eén tenant-documentregel; null bij een match-veld = "maakt niet uit". */
export interface DocumentRule {
  id: string
  priority: number
  matchCrossBorder: boolean | null
  matchAdr: boolean | null
  matchActivityTypeId: string | null
  documentKind: DocumentRuleKind
}

/** Opslag-payload; PUT vervangt de volledige regelset (replace-all). */
export interface DocumentRuleInput {
  priority: number
  matchCrossBorder: boolean | null
  matchAdr: boolean | null
  matchActivityTypeId: string | null
  documentKind: DocumentRuleKind
}

export function listDocumentRules(): Promise<DocumentRule[]> {
  return apiClient.getJson<DocumentRule[]>('/api/settings/document-rules')
}

/** Replace-all; de pagina herlaadt de regels na het opslaan (respons kan leeg zijn). */
export function saveDocumentRules(rules: DocumentRuleInput[]): Promise<unknown> {
  return apiClient.putJson<unknown, DocumentRuleInput[]>('/api/settings/document-rules', rules)
}
