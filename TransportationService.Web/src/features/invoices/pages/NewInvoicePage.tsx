import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import { createInvoice, listUninvoicedOrders } from '../api/invoicesApi'
import { euro, type ManualLineInput, type UninvoicedOrder } from '../types'
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
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

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

  function handleCustomerChange(value: string) {
    setCustomerId(value)
    // Reset synchronously with the user event, not inside the effect.
    setOrders(null)
    setSelectedOrderIds([])
  }

  function toggleOrder(id: string) {
    setSelectedOrderIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  function setManual(key: string, patch: Partial<ManualRow>) {
    setManualLines((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

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
