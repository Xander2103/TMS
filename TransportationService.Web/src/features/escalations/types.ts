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

/** Vertaalsleutels per escalatiesoort; renderen als t(ESCALATION_KIND_LABELS[kind]). */
export const ESCALATION_KIND_LABELS: Record<EscalationKind, string> = {
  NegativeStockUnresolved: 'escalations.kind.NegativeStockUnresolved',
  CriticalStockUnhandled: 'escalations.kind.CriticalStockUnhandled',
  TaskOverdue: 'escalations.kind.TaskOverdue',
  AcknowledgementMissing: 'escalations.kind.AcknowledgementMissing',
  ReturnOverdue: 'escalations.kind.ReturnOverdue',
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
