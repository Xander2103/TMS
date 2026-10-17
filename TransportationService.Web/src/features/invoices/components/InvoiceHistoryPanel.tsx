import { useEffect, useState } from 'react'
import type { BadgeTone } from '../../../components/ui/Badge'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { useLocale } from '../../../i18n/localeContext'
import { getInvoiceHistory } from '../api/invoicesApi'
import type { InvoiceHistoryEvent } from '../types'

const TONE: Record<string, { label: string; tone: BadgeTone }> = {
  'invoice.issued': { label: 'Definitief', tone: 'info' },
  'invoice.paid': { label: 'Betaling', tone: 'success' },
  'invoice.cancelled': { label: 'Annulering', tone: 'danger' },
  'invoice.credited': { label: 'Creditering', tone: 'warning' },
  'creditnote.created': { label: 'Creditering', tone: 'warning' },
  'peppol.delivered': { label: 'Peppol', tone: 'success' },
  'peppol.failed': { label: 'Peppol', tone: 'danger' },
  'peppol.rejected': { label: 'Peppol', tone: 'danger' },
}

/**
 * ACC-M7: the invoice's financial story on the shared history timeline — WHEN / WHO / WHAT,
 * oldest first (created → edited → issued → Peppol → credited → paid). The server merges the audit
 * rows and the Peppol transmission events into Dutch sentences; this panel only renders them.
 * Readable with invoices.view — the tenant-wide audit right is not needed.
 */
export function InvoiceHistoryPanel({ invoiceId, refreshToken = 0, showPeppolHint = false }: { invoiceId: string; refreshToken?: number; showPeppolHint?: boolean }) {
  const { t } = useLocale()
  const [loaded, setLoaded] = useState<{ key: string; events: InvoiceHistoryEvent[] | null; error: string | null } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const key = `${invoiceId}:${refreshToken}`
    getInvoiceHistory(invoiceId, { signal: controller.signal })
      .then((events) => setLoaded({ key, events, error: null }))
      .catch(() => {
        if (!controller.signal.aborted) setLoaded({ key, events: null, error: t('invoices.history.loadError') })
      })
    return () => controller.abort()
  }, [invoiceId, refreshToken, t])

  const current = loaded?.key === `${invoiceId}:${refreshToken}` ? loaded : null
  const items: HistoryItem[] | null = current?.events
    ? current.events.map((event) => ({
        id: event.id,
        timestamp: event.timestamp,
        actorName: event.actorKind === 'User' ? event.actorName : null,
        action: event.title,
        detail: event.detail,
        category: event.type.startsWith('peppol.') && !TONE[event.type] ? { label: 'Peppol', tone: 'neutral' } : (TONE[event.type] ?? null),
      }))
    : null

  return (
    <section className="inv-section inv-history" aria-labelledby="inv-history-title">
      <h3 id="inv-history-title">{t('invoices.detail.historyTitle')}</h3>
      {showPeppolHint && items?.some((item) => item.id.startsWith('peppol:')) && (
        <p className="customer-form-muted">{t('invoices.history.peppolHint')}</p>
      )}
      <HistoryTimeline
        items={items}
        loading={current === null}
        error={current?.error ?? null}
        emptyMessage={t('invoices.history.empty')}
        actorLine={(item) => t('ui.history.actorLine', { action: item.action, name: item.actorName ?? t('invoices.history.actorSystem') })}
      />
    </section>
  )
}
