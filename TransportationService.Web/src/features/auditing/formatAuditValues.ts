/**
 * Formats one audit old/new-values JSON blob for display, masking values of sensitive keys.
 *
 * Defence-in-depth: the backend already writes confidential employee fields partially masked
 * into the audit log (EmployeeService.MaskConfidential) and never audits password/token
 * material by convention. This display-side mask catches anything that slips through for
 * viewers who hold audit_logs.view but not the confidential-data permissions.
 */
const SENSITIVE_KEY_PATTERN =
  /password|secret|token|securitystamp|iban|bic|nationalregister|identitycard|creditcard/

const MASK = '••••'

export function isSensitiveAuditKey(key: string): boolean {
  return SENSITIVE_KEY_PATTERN.test(key.toLowerCase().replace(/[^a-z0-9]/gi, ''))
}

export function formatAuditValues(json: string | null): string {
  if (!json) return ''
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    return Object.entries(parsed)
      .map(([key, value]) => {
        if (value !== null && value !== undefined && value !== '' && isSensitiveAuditKey(key)) {
          return `${key}: ${MASK}`
        }
        return `${key}: ${value ?? '—'}`
      })
      .join(' · ')
  } catch {
    return json
  }
}
