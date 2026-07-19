import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { apiClient, ApiError } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import './edi.css'

type EdiDirection = 'Inbound' | 'Outbound'
type EdiStatus = 'Received' | 'Processed' | 'Failed' | 'DeadLettered' | 'Duplicate'

interface EdiRow {
  id: string
  direction: EdiDirection
  partnerCode: string
  messageType: string
  externalReference: string | null
  status: EdiStatus
  attemptCount: number
  processedAt: string | null
  errorDetail: string | null
  resultEntityType: string | null
  resultEntityId: string | null
  createdAt: string
}

interface Partner {
  id: string
  code: string
  name: string
  customerId: string | null
  isActive: boolean
  locations: Array<{ id: string; externalLocationCode: string; locationId: string }>
}

const STATUS_TONE: Record<EdiStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Received: 'info',
  Processed: 'success',
  Failed: 'danger',
  DeadLettered: 'danger',
  Duplicate: 'neutral',
}

const STATUS_LABELS: Record<EdiStatus, string> = {
  Received: 'Ontvangen',
  Processed: 'Verwerkt',
  Failed: 'Mislukt',
  DeadLettered: 'Dead letter',
  Duplicate: 'Duplicaat',
}

const PAGE_SIZE = 25

/** EDI inbox/outbox with replay, partner administration and the development simulator. */
export function EdiPage() {
  const { showSuccess, showError } = useToast()

  const [statusFilter, setStatusFilter] = useState<EdiStatus | ''>('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [messages, setMessages] = useState<PagedResult<EdiRow> | null>(null)
  const [partners, setPartners] = useState<Partner[]>([])
  const [detail, setDetail] = useState<Record<string, unknown> | null>(null)
  const [busy, setBusy] = useState(false)

  const [partnerCode, setPartnerCode] = useState('')
  const [partnerName, setPartnerName] = useState('')

  useEffect(() => {
    let mounted = true
    const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (statusFilter) query.set('status', statusFilter)
    apiClient
      .getJson<PagedResult<EdiRow>>(`/api/edi/messages?${query.toString()}`)
      .then((data) => {
        if (mounted) setMessages(data)
      })
      .catch(() => {
        if (mounted) showError('De EDI-berichten konden niet worden geladen.')
      })
    apiClient
      .getJson<Partner[]>('/api/edi/partners')
      .then((data) => {
        if (mounted) setPartners(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, statusFilter, reloadToken])

  async function replay(id: string) {
    setBusy(true)
    try {
      await apiClient.postJson(`/api/edi/messages/${id}/replay`, {})
      showSuccess('Bericht opnieuw verwerkt.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De verwerking mislukte opnieuw.')
    } finally {
      setBusy(false)
    }
  }

  async function openDetail(id: string) {
    try {
      setDetail(await apiClient.getJson<Record<string, unknown>>(`/api/edi/messages/${id}`))
    } catch {
      showError('Het bericht kon niet worden geopend.')
    }
  }

  async function simulate(code: string) {
    setBusy(true)
    try {
      await apiClient.postJson('/api/edi/simulate', { partnerCode: code })
      showSuccess('Simulatiebericht verwerkt — bekijk het resultaat in de inbox.')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De simulatie mislukte.')
    } finally {
      setBusy(false)
    }
  }

  async function createPartner(event: FormEvent) {
    event.preventDefault()
    if (!partnerCode.trim() || !partnerName.trim()) {
      showError('Code en naam zijn verplicht.')
      return
    }
    setBusy(true)
    try {
      await apiClient.postJson('/api/edi/partners', {
        code: partnerCode.trim(),
        name: partnerName.trim(),
        customerId: null,
        externalCustomerIdentifier: null,
        isActive: true,
      })
      showSuccess('Partner opgeslagen — koppel nog een klant via de API voor inbound orders.')
      setPartnerCode('')
      setPartnerName('')
      setReloadToken((t) => t + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De partner kon niet worden opgeslagen.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'EDI' }]} />
      <PageHeader
        title="EDI"
        subtitle="Inkomende orders en uitgaande statussen per handelspartner — generiek JSON-profiel; partnerformaten volgen zodra er een specificatie is."
      />

      <div className="edi-filters">
        <select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value as EdiStatus | ''); setPage(1) }} aria-label="Status">
          <option value="">Alle statussen</option>
          {(Object.keys(STATUS_LABELS) as EdiStatus[]).map((status) => (
            <option key={status} value={status}>
              {STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      <table className="to-stops-table">
        <thead>
          <tr>
            <th>Ontvangen</th>
            <th>Richting</th>
            <th>Partner</th>
            <th>Type</th>
            <th>Externe ref.</th>
            <th>Status</th>
            <th>Fout</th>
            <th aria-label="Acties" />
          </tr>
        </thead>
        <tbody>
          {(messages?.items ?? []).map((row) => (
            <tr key={row.id}>
              <td>{row.createdAt.slice(0, 16).replace('T', ' ')}</td>
              <td>{row.direction === 'Inbound' ? '↓ in' : '↑ uit'}</td>
              <td>
                <code>{row.partnerCode}</code>
              </td>
              <td>{row.messageType}</td>
              <td>{row.externalReference ?? '—'}</td>
              <td>
                <Badge tone={STATUS_TONE[row.status]}>{STATUS_LABELS[row.status]}</Badge>
                {row.attemptCount > 1 && ` (${row.attemptCount}×)`}
              </td>
              <td className="edi-error" title={row.errorDetail ?? undefined}>
                {row.errorDetail ?? '—'}
              </td>
              <td>
                <Button variant="ghost" onClick={() => void openDetail(row.id)}>
                  Detail
                </Button>
                {(row.status === 'Failed' || row.status === 'DeadLettered') && (
                  <Button variant="ghost" onClick={() => void replay(row.id)} disabled={busy}>
                    Replay
                  </Button>
                )}
              </td>
            </tr>
          ))}
          {messages !== null && messages.items.length === 0 && (
            <tr>
              <td colSpan={8}>Nog geen EDI-berichten.</td>
            </tr>
          )}
        </tbody>
      </table>
      {messages && <Pagination page={page} pageSize={PAGE_SIZE} totalCount={messages.totalCount} onPageChange={setPage} />}

      <section className="to-section">
        <h2>Handelspartners</h2>
        <ul className="edi-partners">
          {partners.map((partner) => (
            <li key={partner.id}>
              <code>{partner.code}</code> — {partner.name}
              {!partner.isActive && <Badge tone="neutral">inactief</Badge>}
              {!partner.customerId && <Badge tone="warning">geen klant gekoppeld</Badge>}
              <span className="edi-partner-locations">{partner.locations.length} locatiemapping(s)</span>
              <Button variant="ghost" onClick={() => void simulate(partner.code)} disabled={busy || !partner.customerId}>
                ▶ Testbericht
              </Button>
            </li>
          ))}
          {partners.length === 0 && <li>Nog geen partners.</li>}
        </ul>
        <form className="edi-partner-form" onSubmit={createPartner} noValidate>
          <FormField label="Code" htmlFor="edi-code">
            <input id="edi-code" value={partnerCode} onChange={(e) => setPartnerCode(e.target.value)} disabled={busy} maxLength={50} />
          </FormField>
          <FormField label="Naam" htmlFor="edi-name">
            <input id="edi-name" value={partnerName} onChange={(e) => setPartnerName(e.target.value)} disabled={busy} maxLength={200} />
          </FormField>
          <Button type="submit" variant="secondary" disabled={busy}>
            + Partner
          </Button>
        </form>
      </section>

      {detail && (
        <Modal title="EDI-bericht" onClose={() => setDetail(null)}>
          <pre className="edi-payload">{JSON.stringify(detail, null, 2)}</pre>
        </Modal>
      )}
    </div>
  )
}
