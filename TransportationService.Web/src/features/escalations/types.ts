/** Escalatiesoorten zoals de backend ze kent (EscalationPolicyDto.kind). */
export type EscalationKind =
  | 'NegativeStockUnresolved'
  | 'CriticalStockUnhandled'
  | 'TaskOverdue'
  | 'AcknowledgementMissing'
  | 'ReturnOverdue'

export const ESCALATION_KINDS: EscalationKind[] = [
  'NegativeStockUnresolved',
  'CriticalStockUnhandled',
  'TaskOverdue',
  'AcknowledgementMissing',
  'ReturnOverdue',
]

export const ESCALATION_KIND_LABELS: Record<EscalationKind, string> = {
  NegativeStockUnresolved: 'Negatieve voorraad onopgelost',
  CriticalStockUnhandled: 'Kritieke voorraad onbehandeld',
  TaskOverdue: 'Taak achterstallig',
  AcknowledgementMissing: 'Bevestiging ontbreekt',
  ReturnOverdue: 'Retour te laat',
}

/** Mirrors GET /api/escalation-policies (camelCase JSON). */
export interface EscalationPolicy {
  id: string
  kind: EscalationKind
  delayHours: number
  targetPermissionCode: string
  isActive: boolean
}

/** Body of PUT /api/escalation-policies/{kind}. */
export interface UpdateEscalationPolicyInput {
  delayHours: number
  targetPermissionCode: string
  isActive: boolean
}
