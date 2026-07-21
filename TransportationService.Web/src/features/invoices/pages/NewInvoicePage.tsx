import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import { getPoPolicy } from '../../customers/api/customerBillingConfigApi'
import { getActiveLegalEntity, getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import { createInvoice, getNextInvoiceNumber, listUninvoicedOrders } from '../api/invoicesApi'
import { euro, type ManualLineInput, type UninvoicedOrder } from '../types'
import { comparePeriods, dateToPeriod, formatPeriod, monthInputToPeriod, periodToMonthInput } from '../utils/invoicePeriod'
import './invoices.css'

interface ManualRow extends ManualLineInput {
  key: string
}

let manualKey = 0

/** Invoice builder: pick a customer, tick completed orders, add manual lines. */
export function NewInvoicePage() {
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()

  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [customerId, setCustomerId] = useState('')
  const [orders, setOrders] = useState<UninvoicedOrder[] | null>(null)
  const [selectedOrderIds, setSelectedOrderIds] = useState<string[]>([])
  const [manualLines, setManualLines] = useState<ManualRow[]>([])
  const [notes, setNotes] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [poPolicyRequired, setPoPolicyRequired] = useState(false)
  const [busy, setBusy] = useState(false)

  const [entities, setEntities] = useState<LegalEntityOption[]>([])
  const [legalEntityId, setLegalEntityId] = useState('')
  // The invoice date defaults to today on the backend, so the period starts on today's month.
  const [periodInput, setPeriodInput] = useState(() => {
    const today = dateToPeriod(null)
    return periodToMonthInput(today.year, today.month)
  })
  const [nextNumber, setNextNumber] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    // Entity picker: preselect the user's own active entity, else the configured default.
    Promise.all([
      getLegalEntityOptions().catch(() => [] as LegalEntityOption[]),
      getActiveLegalEntity().catch(() => ({ legalEntityId: null })),
    ])
      .then(([options, active]) => {
        if (!mounted) return
        const usable = options.filter((option) => option.isActive)
        setEntities(usable)
        const preferred =
          usable.find((option) => option.id === active.legalEntityId) ??
          usable.find((option) => option.isDefault) ??
          usable[0]
        if (preferred) setLegalEntityId(preferred.id)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  // Live "next number" preview; refetches (debounced) whenever entity or period changes.
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      const period = monthInputToPeriod(periodInput)
      if (!period) {
        setNextNumber(null)
        return
      }
      getNextInvoiceNumber(
        { legalEntityId: legalEntityId || undefined, year: period.year, month: period.month },
        { signal: controller.signal },
      )
        .then((preview) => setNextNumber(preview.invoiceNumber))
        .catch(() => {
          // Hide the preview on 404 (no active entity) or any other failure.
          if (!controller.signal.aborted) setNextNumber(null)
        })
    }, 250)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [legalEntityId, periodInput])

  useEffect(() => {
    if (!customerId) {
      return
    }
    let mounted = true
    listUninvoicedOrders(customerId)
      .then((data) => {
        if (!mounted) return
        setOrders(data)
        setSelectedOrderIds(data.map((o) => o.id)) // default: everything selected
      })
      .catch(() => {
        if (mounted) setOrders([])
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  // PO-beleid van de gekozen klant: waarschuw bij 'Verplicht' en vul een actief PO-nummer voor.
  // De reset bij het wisselen van klant gebeurt in handleCustomerChange (niet in dit effect).
  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getPoPolicy(customerId)
      .then((policy) => {
        if (!mounted) return
        setPoPolicyRequired(policy.policy === 'Required')
        if (policy.effectivePoNumber) {
          setPoNumber((current) => (current.trim() === '' ? policy.effectivePoNumber ?? '' : current))
        }
      })
      .catch(() => {
        if (mounted) setPoPolicyRequired(false)
      })
    return () => {
      mounted = false
    }
  }, [customerId])

  function handleCustomerChange(value: string) {
    setCustomerId(value)
    // Reset synchronously with the user event, not inside the effect.
    setOrders(null)
    setSelectedOrderIds([])
    setPoNumber('')
    setPoPolicyRequired(false)
  }

  function toggleOrder(id: string) {
    setSelectedOrderIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  function setManual(key: string, patch: Partial<ManualRow>) {
    setManualLines((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  const period = monthInputToPeriod(periodInput)
  // Warn (without blocking) when invoicing in a month before the invoice date's month (= today).
  const isPastPeriod = period !== null && comparePeriods(period, dateToPeriod(null)) < 0

  const estimatedSubtotal =
    (orders ?? [])
      .filter((o) => selectedOrderIds.includes(o.id))
      .reduce((sum, o) => sum + (o.agreedPrice ?? 0), 0) +
    manualLines.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0)

  async function handleCreate() {
    if (!customerId) {
      showError('Selecteer eerst een klant.')
      return
    }
    if (selectedOrderIds.length === 0 && manualLines.length === 0) {
      showError('Selecteer minstens één opdracht of voeg een lijn toe.')
      return
    }
    for (const line of manualLines) {
      if (!line.description.trim() || line.quantity <= 0) {
        showError('Elke handmatige lijn heeft een omschrijving en een hoeveelheid groter dan nul nodig.')
        return
      }
    }
    setBusy(true)
    try {
      const invoice = await createInvoice({
        customerId,
        invoiceDate: null,
        orderIds: selectedOrderIds,
        manualLines: manualLines.map((line) => ({
          description: line.description.trim(),
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          vatRatePercent: line.vatRatePercent,
        })),
        notes: notes.trim() || null,
        purchaseOrderNumber: poNumber.trim() || null,
        legalEntityId: legalEntityId || null,
        invoicePeriodYear: period?.year ?? null,
        invoicePeriodMonth: period?.month ?? null,
      })
      showSuccess(`Factuur ${invoice.invoiceNumber} aangemaakt.`)
      navigate(`/invoices/${invoice.id}`)
    } catch {
      showError('De factuur kon niet worden aangemaakt.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Facturen', to: '/invoices' }, { label: 'Nieuwe factuur' }]} />
      <PageHeader title="Nieuwe factuur" subtitle="Gebaseerd op afgeronde, nog niet gefactureerde opdrachten." />

      <div className="inv-builder">
        <FormField label="Klant" htmlFor="inv-customer" required>
          <select id="inv-customer" value={customerId} onChange={(e) => handleCustomerChange(e.target.value)} disabled={busy}>
            <option value="">Selecteer een klant…</option>
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.customerNumber})
              </option>
            ))}
          </select>
        </FormField>

        <div className="inv-entity-period">
          <FormField label="Facturerende entiteit" htmlFor="inv-legal-entity">
            <select
              id="inv-legal-entity"
              value={legalEntityId}
              onChange={(e) => setLegalEntityId(e.target.value)}
              disabled={busy}
            >
              {entities.length === 0 && <option value="">Geen entiteit beschikbaar</option>}
              {entities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.displayName}
                  {entity.isDefault ? ' (standaard)' : ''}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Factuurperiode" htmlFor="inv-period">
            <input
              id="inv-period"
              type="month"
              value={periodInput}
              onChange={(e) => setPeriodInput(e.target.value)}
              disabled={busy}
            />
          </FormField>
        </div>
        {nextNumber && (
          <p className="inv-next-number">
            Volgend factuurnummer: <strong>{nextNumber}</strong>
          </p>
        )}
        {isPastPeriod && period && (
          <p className="inv-period-warning">Je factureert in een eerdere periode ({formatPeriod(period.year, period.month)}).</p>
        )}

        <FormField label="PO-nummer" htmlFor="inv-po" hint="Optioneel referentienummer van de klant.">
          <input id="inv-po" value={poNumber} onChange={(e) => setPoNumber(e.target.value)} disabled={busy} maxLength={100} />
        </FormField>
        {poPolicyRequired && poNumber.trim() === '' && (
          <p className="inv-period-warning">Deze klant vereist een PO-nummer.</p>
        )}

        {customerId && orders !== null && (
          <section className="inv-section">
            <h3>Factureerbare opdrachten ({orders.length})</h3>
            {orders.length === 0 && (
              <p className="placeholder-text">Geen afgeronde, onggefactureerde opdrachten voor deze klant.</p>
            )}
            {orders.length > 0 && (
              <table className="inv-orders-table">
                <thead>
                  <tr>
                    <th aria-label="Selectie" />
                    <th>Nummer</th>
                    <th>Datum</th>
                    <th>Goederen</th>
                    <th>Route</th>
                    <th>Prijs</th>
                  </tr>
                </thead>
                <tbody>
                  {orders.map((order) => (
                    <tr key={order.id} className="inv-order-row" onClick={() => toggleOrder(order.id)}>
                      <td>
                        <input
                          type="checkbox"
                          checked={selectedOrderIds.includes(order.id)}
                          onChange={() => toggleOrder(order.id)}
                          onClick={(e) => e.stopPropagation()}
                          aria-label={`Selecteer ${order.orderNumber}`}
                        />
                      </td>
                      <td>
                        <code>{order.orderNumber}</code>
                      </td>
                      <td>{order.orderDate}</td>
                      <td className="inv-goods">{order.goodsDescription}</td>
                      <td>
                        {order.firstLoadingCity ?? '?'} → {order.lastUnloadingCity ?? '?'}
                      </td>
                      <td>{order.agreedPrice !== null ? euro(order.agreedPrice) : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        )}

        <section className="inv-section">
          <div className="inv-manual-head">
            <h3>Extra lijnen</h3>
            <Button
              variant="secondary"
              onClick={() =>
                setManualLines((rows) => [
                  ...rows,
                  { key: `m-${++manualKey}`, description: '', quantity: 1, unitPrice: 0, vatRatePercent: null },
                ])
              }
              disabled={busy}
            >
              + Lijn toevoegen
            </Button>
          </div>
          {manualLines.map((line) => (
            <div key={line.key} className="inv-manual-row">
              <input
                value={line.description}
                onChange={(e) => setManual(line.key, { description: e.target.value })}
                placeholder="Omschrijving (bv. wachturen)"
                disabled={busy}
                maxLength={500}
              />
              <input
                type="number"
                min={0.01}
                step="0.01"
                value={line.quantity}
                onChange={(e) => setManual(line.key, { quantity: Number(e.target.value) })}
                disabled={busy}
                aria-label="Hoeveelheid"
              />
              <input
                type="number"
                min={0}
                step="0.01"
                value={line.unitPrice}
                onChange={(e) => setManual(line.key, { unitPrice: Number(e.target.value) })}
                disabled={busy}
                aria-label="Eenheidsprijs"
              />
              <button
                type="button"
                className="inv-link inv-link-danger"
                onClick={() => setManualLines((rows) => rows.filter((r) => r.key !== line.key))}
                disabled={busy}
              >
                Verwijderen
              </button>
            </div>
          ))}
        </section>

        <FormField label="Notities" htmlFor="inv-notes">
          <textarea id="inv-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={4000} />
        </FormField>

        <div className="inv-builder-footer">
          <span className="inv-estimate">Geschat subtotaal (excl. btw): <strong>{euro(estimatedSubtotal)}</strong></span>
          <span className="inv-builder-actions">
            <Button variant="secondary" onClick={() => navigate('/invoices')} disabled={busy}>
              Annuleren
            </Button>
            <Button onClick={() => void handleCreate()} disabled={busy}>
              {busy ? 'Bezig…' : 'Factuur aanmaken'}
            </Button>
          </span>
        </div>
      </div>
    </div>
  )
}
