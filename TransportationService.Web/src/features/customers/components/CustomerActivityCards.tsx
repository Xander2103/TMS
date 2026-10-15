import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { PagedResult } from '../../../api/types'
import { Badge } from '../../../components/ui/Badge'
import { DetailCard } from '../../../components/ui/DetailCard'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { searchDossiers } from '../../dossiers/api/dossiersApi'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem } from '../../dossiers/types'
import { searchInvoices } from '../../invoices/api/invoicesApi'
import { euro, INVOICE_STATUS_LABELS, INVOICE_STATUS_TONE, type InvoiceListItem } from '../../invoices/types'

/** How many rows a snapshot card shows; the footer link opens the full, filtered list. */
const SNAPSHOT_SIZE = 5

type Snapshot<T> = { forId: string; result: PagedResult<T> | null; failed: boolean }

/**
 * One small page of a server-side list for this customer. Never loads more than
 * SNAPSHOT_SIZE rows — the count comes from the envelope's totalCount. Keyed on the customer it
 * was fetched for, so a stale snapshot never shows under another customer.
 */
function useSnapshot<T>(customerId: string, load: (customerId: string) => Promise<PagedResult<T>>) {
  const [state, setState] = useState<Snapshot<T> | null>(null)
  useEffect(() => {
    let cancelled = false
    load(customerId)
      .then((result) => { if (!cancelled) setState({ forId: customerId, result, failed: false }) })
      .catch(() => { if (!cancelled) setState({ forId: customerId, result: null, failed: true }) })
    return () => { cancelled = true }
    // `load` is a module-level function per card; only the customer matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [customerId])
  return state?.forId === customerId ? state : null
}

const loadOpenDossiers = (customerId: string) =>
  searchDossiers({ customerId, status: 'Open', sort: 'date', dir: 'desc', page: 1, pageSize: SNAPSHOT_SIZE })

const loadRecentInvoices = (customerId: string) =>
  searchInvoices({ customerId, sort: 'date', dir: 'desc', page: 1, pageSize: SNAPSHOT_SIZE })

/** B17: the customer's open dossiers, newest first — mount only for users with dossier access. */
export function CustomerOpenDossiersCard({ customerId }: { customerId: string }) {
  const { t } = useLocale()
  const snapshot = useSnapshot<DossierListItem>(customerId, loadOpenDossiers)
  const total = snapshot?.result?.totalCount
  return (
    <DetailCard title={t('customers.overview.openDossiersTitle')} aside={total !== undefined ? String(total) : undefined}>
      {snapshot === null ? (
        <p className="ui-detail-empty">{t('customers.common.loading')}</p>
      ) : snapshot.failed ? (
        <p className="ui-detail-empty">{t('customers.overview.loadFailed')}</p>
      ) : snapshot.result!.items.length === 0 ? (
        <p className="ui-detail-empty">{t('customers.overview.openDossiersEmpty')}</p>
      ) : (
        <ul className="ui-detail-list">
          {snapshot.result!.items.map((dossier) => {
            const route = [dossier.firstLoadingCity, dossier.lastUnloadingCity].filter(Boolean).join(' → ')
            return (
              <li key={dossier.id}>
                <Link className="ui-id-link" to={`/dossiers/${dossier.id}`}>
                  {dossier.dossierNumber}
                </Link>
                <span className="ui-detail-list-meta">{formatDate(dossier.dossierDate ?? dossier.createdAt)}</span>
                <span className="ui-detail-list-main" title={route || undefined}>
                  {route || <span className="customer-form-muted">{dossier.customerReference ?? t('customers.overview.noRoute')}</span>}
                </span>
                <Badge tone={DOSSIER_STATUS_TONE[dossier.status]}>{t(DOSSIER_STATUS_LABELS[dossier.status])}</Badge>
              </li>
            )
          })}
        </ul>
      )}
      <p className="ui-detail-card-footer">
        <Link to={`/dossiers?status=Open&customerId=${customerId}`}>{t('customers.overview.allDossiers')} →</Link>
      </p>
    </DetailCard>
  )
}

/** B17: read-only snapshot of the latest invoices — mount only for users with `invoices.view`. */
export function CustomerRecentInvoicesCard({ customerId, customerName }: { customerId: string; customerName: string }) {
  const { t } = useLocale()
  const snapshot = useSnapshot<InvoiceListItem>(customerId, loadRecentInvoices)
  return (
    <DetailCard title={t('customers.overview.recentInvoicesTitle')}>
      {snapshot === null ? (
        <p className="ui-detail-empty">{t('customers.common.loading')}</p>
      ) : snapshot.failed ? (
        <p className="ui-detail-empty">{t('customers.overview.loadFailed')}</p>
      ) : snapshot.result!.items.length === 0 ? (
        <p className="ui-detail-empty">{t('customers.overview.recentInvoicesEmpty')}</p>
      ) : (
        <ul className="ui-detail-list">
          {snapshot.result!.items.map((invoice) => (
            <li key={invoice.id}>
              <Link className="ui-id-link" to={`/invoices/${invoice.id}`}>
                {invoice.invoiceNumber || t('customers.overview.draftInvoice')}
              </Link>
              <span className="ui-detail-list-meta">{formatDate(invoice.invoiceDate)}</span>
              <span className="ui-detail-list-main customer-invoice-amount">{euro(invoice.total, invoice.currency)}</span>
              <Badge tone={INVOICE_STATUS_TONE[invoice.status]}>{t(INVOICE_STATUS_LABELS[invoice.status])}</Badge>
            </li>
          ))}
        </ul>
      )}
      <p className="ui-detail-card-footer">
        <Link to={`/invoices?search=${encodeURIComponent(customerName)}`}>{t('customers.overview.allInvoices')} →</Link>
      </p>
    </DetailCard>
  )
}
