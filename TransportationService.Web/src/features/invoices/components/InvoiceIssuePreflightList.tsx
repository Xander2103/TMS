import { useLocale } from '../../../i18n/localeContext'
import type { InvoiceIssuePreflight, InvoiceLine } from '../types'

interface InvoiceIssuePreflightListProps {
  preflight: InvoiceIssuePreflight | null
  /** Null while loading; a message when the preflight itself failed. */
  error: string | null
  lines: InvoiceLine[]
}

/**
 * ACC-M4: what still blocks issuing the draft, in words that name the fix. Every issue code has a
 * Dutch label with a corrective hint; the server message stays as the fallback so a code this UI
 * does not know yet is never hidden. Affected lines are named by their number ("lijn 3, 4").
 */
export function InvoiceIssuePreflightList({ preflight, error, lines }: InvoiceIssuePreflightListProps) {
  const { t } = useLocale()
  if (error) return <p className="inv-preflight inv-preflight-error" role="alert">{error}</p>
  if (!preflight) return <p className="inv-preflight placeholder-text">{t('invoices.common.busy')}</p>
  if (preflight.canIssue && preflight.issues.length === 0) {
    return <p className="inv-preflight inv-preflight-ok" role="status">{t('invoices.detail.preflightOk')}</p>
  }
  return (
    <div className="inv-preflight" role="alert" data-testid="invoice-issue-preflight">
      <strong>{t('invoices.detail.preflightTitle')}</strong>
      <ul>
        {preflight.issues.map((issue, index) => {
          // Locale keys cannot carry the code's dots (t() walks them as a path): "po.missing" → "po_missing".
          const key = `invoices.detail.preflightCodes.${issue.code.replace(/\./g, '_')}`
          const label = t(key)
          const numbers = (issue.lineIds ?? [])
            .map((id) => lines.find((line) => line.id === id)?.sequence)
            .filter((n): n is number => typeof n === 'number')
          return (
            <li key={`${issue.code}-${index}`}>
              {label === key ? issue.message : label}
              {numbers.length > 0 && <> — {t('invoices.detail.preflightLines', { lines: numbers.join(', ') })}</>}
              {label !== key && issue.message && issue.message !== label && (
                <div className="customer-form-muted">{issue.message}</div>
              )}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
