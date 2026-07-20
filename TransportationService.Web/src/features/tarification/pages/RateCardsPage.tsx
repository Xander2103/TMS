import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { euro } from '../../invoices/types'
import { searchCustomers } from '../../customers/api/customersApi'
import { createRateCard, deleteRateCard, listRateCards, quoteRate, updateRateCard } from '../api/rateCardsApi'
import {
  SURCHARGE_KIND_LABELS,
  type Quote,
  type RateCard,
  type RateSurchargeInput,
  type SurchargeKind,
} from '../types'

interface CardForm {
  customerId: string | null
  name: string
  effectiveFrom: string
  effectiveUntil: string
  baseAmount: string
  perKmRate: string
  perPalletRate: string
  perTonRate: string
  minimumAmount: string
  notes: string
  surcharges: { name: string; kind: SurchargeKind; value: string }[]
}

const EMPTY_FORM: CardForm = {
  customerId: null,
  name: '',
  effectiveFrom: '',
  effectiveUntil: '',
  baseAmount: '0',
  perKmRate: '',
  perPalletRate: '',
  perTonRate: '',
  minimumAmount: '',
  notes: '',
  surcharges: [],
}

function toForm(card: RateCard): CardForm {
  return {
    customerId: card.customerId,
    name: card.name,
    effectiveFrom: card.effectiveFrom,
    effectiveUntil: card.effectiveUntil ?? '',
    baseAmount: String(card.baseAmount),
    perKmRate: card.perKmRate !== null ? String(card.perKmRate) : '',
    perPalletRate: card.perPalletRate !== null ? String(card.perPalletRate) : '',
    perTonRate: card.perTonRate !== null ? String(card.perTonRate) : '',
    minimumAmount: card.minimumAmount !== null ? String(card.minimumAmount) : '',
    notes: card.notes ?? '',
    surcharges: card.surcharges.map((s) => ({ name: s.name, kind: s.kind, value: String(s.value) })),
  }
}

const num = (value: string): number | null => (value === '' ? null : Number(value))

/** Verkooptarieven per klant met geldigheidsperiode, toeslagen en een proefberekening. */
export function RateCardsPage() {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('tariffs.manage')

  const [cards, setCards] = useState<RateCard[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [customerFilter, setCustomerFilter] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])

  const [dialog, setDialog] = useState<{ card: RateCard | null } | null>(null)
  const [form, setForm] = useState<CardForm>(EMPTY_FORM)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<RateCard | null>(null)
  const [busy, setBusy] = useState(false)

  // Quote calculator
  const [quoteOpen, setQuoteOpen] = useState(false)
  const [quoteCustomerId, setQuoteCustomerId] = useState<string | null>(null)
  const [quoteDate, setQuoteDate] = useState('')
  const [quoteKm, setQuoteKm] = useState('')
  const [quotePallets, setQuotePallets] = useState('')
  const [quoteKg, setQuoteKg] = useState('')
  const [quote, setQuote] = useState<Quote | null>(null)
  const [quoteError, setQuoteError] = useState<string | null>(null)

  const reload = useCallback(() => {
    listRateCards(customerFilter ?? undefined)
      .then((data) => {
        setCards(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError('De tarievenkaarten konden niet worden geladen.')
        setLoaded(true)
      })
  }, [customerFilter])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    searchCustomers({ page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
  }, [])

  function openDialog(card: RateCard | null) {
    setForm(card ? toForm(card) : { ...EMPTY_FORM, customerId: customerFilter })
    setFieldErrors({})
    setFormError(null)
    setDialog({ card })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!dialog) return
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    const input = {
      customerId: form.customerId ?? '',
      name: form.name,
      effectiveFrom: form.effectiveFrom,
      effectiveUntil: form.effectiveUntil || null,
      baseAmount: Number(form.baseAmount || '0'),
      perKmRate: num(form.perKmRate),
      perPalletRate: num(form.perPalletRate),
      perTonRate: num(form.perTonRate),
      minimumAmount: num(form.minimumAmount),
      notes: form.notes || null,
      surcharges: form.surcharges
        .filter((s) => s.name.trim() !== '')
        .map((s): RateSurchargeInput => ({ name: s.name, kind: s.kind, value: Number(s.value || '0') })),
    }
    try {
      if (dialog.card) {
        await updateRateCard(dialog.card.id, input)
        toast.showSuccess('Tarievenkaart bijgewerkt.')
      } else {
        await createRateCard(input)
        toast.showSuccess('Tarievenkaart aangemaakt.')
      }
      setDialog(null)
      reload()
    } catch (err) {
      const described = describeApiError(err, 'De tarievenkaart kon niet worden opgeslagen.')
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function runQuote() {
    if (!quoteCustomerId || !quoteDate) {
      setQuoteError('Kies een klant en een datum.')
      return
    }
    setBusy(true)
    setQuoteError(null)
    setQuote(null)
    try {
      setQuote(
        await quoteRate({
          customerId: quoteCustomerId,
          date: quoteDate,
          distanceKm: num(quoteKm),
          palletCount: quotePallets === '' ? null : Number(quotePallets),
          weightKg: num(quoteKg),
        }),
      )
    } catch (err) {
      setQuoteError(describeApiError(err, 'De berekening is niet gelukt.').message)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<RateCard>[] = [
    { key: 'customer', header: 'Klant', render: (row) => row.customerName },
    { key: 'name', header: 'Naam', render: (row) => row.name },
    {
      key: 'period',
      header: 'Geldig',
      render: (row) => `${row.effectiveFrom} – ${row.effectiveUntil ?? '...'}`,
    },
    { key: 'base', header: 'Basis', render: (row) => euro(row.baseAmount) },
    {
      key: 'components',
      header: 'Componenten',
      render: (row) =>
        [
          row.perKmRate !== null ? `${row.perKmRate} €/km` : null,
          row.perPalletRate !== null ? `${row.perPalletRate} €/pallet` : null,
          row.perTonRate !== null ? `${row.perTonRate} €/ton` : null,
          row.minimumAmount !== null ? `min ${euro(row.minimumAmount)}` : null,
        ]
          .filter(Boolean)
          .join(' · ') || '—',
    },
    {
      key: 'surcharges',
      header: 'Toeslagen',
      render: (row) => (row.surcharges.length > 0 ? <Badge tone="info">{row.surcharges.length}</Badge> : '—'),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: '',
            render: (row: RateCard) => (
              <span style={{ display: 'inline-flex', gap: '0.4rem' }}>
                <Button variant="secondary" onClick={() => openDialog(row)}>
                  Bewerken
                </Button>
                <Button variant="secondary" onClick={() => setConfirmDelete(row)}>
                  Verwijderen
                </Button>
              </span>
            ),
          } satisfies Column<RateCard>,
        ]
      : []),
  ]

  return (
    <div>
      <PageHeader
        title="Verkooptarieven"
        subtitle="Tarievenkaarten per klant met geldigheidsperiode en toeslagen."
        action={
          <span style={{ display: 'inline-flex', gap: '0.5rem' }}>
            <Button
              variant="secondary"
              onClick={() => {
                setQuote(null)
                setQuoteError(null)
                setQuoteCustomerId(customerFilter)
                setQuoteOpen(true)
              }}
            >
              Prijs berekenen
            </Button>
            {canManage && <Button onClick={() => openDialog(null)}>Nieuwe kaart</Button>}
          </span>
        }
      />

      <div style={{ maxWidth: '20rem', marginBottom: '1rem' }}>
        <SearchableSelect
          value={customerFilter}
          onChange={setCustomerFilter}
          options={customerOptions}
          placeholder="Filter op klant..."
          ariaLabel="Filter op klant"
        />
      </div>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && cards.length === 0 && (
        <EmptyState message="Nog geen tarievenkaarten. Maak per klant een kaart met een geldigheidsperiode aan." />
      )}
      {!error && cards.length > 0 && <DataTable columns={columns} rows={cards} rowKey={(row) => row.id} />}

      {dialog && (
        <Modal
          title={dialog.card ? 'Tarievenkaart bewerken' : 'Nieuwe tarievenkaart'}
          onClose={() => setDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialog(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="rate-card-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="rate-card-form" onSubmit={(event) => void submit(event)}>
            <ValidationSummary message={formError} fieldErrors={fieldErrors} />
            <FormField label="Klant" htmlFor="rc-customer" required error={getFieldError(fieldErrors, 'customerId')}>
              <SearchableSelect
                id="rc-customer"
                value={form.customerId}
                onChange={(value) => setForm((f) => ({ ...f, customerId: value }))}
                options={customerOptions}
              />
            </FormField>
            <FormField label="Naam" htmlFor="rc-name" required error={getFieldError(fieldErrors, 'name')}>
              <input
                id="rc-name"
                value={form.name}
                onChange={(event) => setForm((f) => ({ ...f, name: event.target.value }))}
                maxLength={200}
              />
            </FormField>
            <FormField label="Geldig van" htmlFor="rc-from" required error={getFieldError(fieldErrors, 'effectiveFrom')}>
              <input
                id="rc-from"
                type="date"
                value={form.effectiveFrom}
                onChange={(event) => setForm((f) => ({ ...f, effectiveFrom: event.target.value }))}
              />
            </FormField>
            <FormField label="Geldig tot en met" htmlFor="rc-until" error={getFieldError(fieldErrors, 'effectiveUntil')}>
              <input
                id="rc-until"
                type="date"
                value={form.effectiveUntil}
                onChange={(event) => setForm((f) => ({ ...f, effectiveUntil: event.target.value }))}
              />
            </FormField>
            <FormField label="Basisbedrag (€)" htmlFor="rc-base" error={getFieldError(fieldErrors, 'baseAmount')}>
              <input
                id="rc-base"
                type="number"
                min={0}
                step="0.01"
                value={form.baseAmount}
                onChange={(event) => setForm((f) => ({ ...f, baseAmount: event.target.value }))}
              />
            </FormField>
            <FormField label="Prijs per km (€)" htmlFor="rc-km">
              <input
                id="rc-km"
                type="number"
                min={0}
                step="0.0001"
                value={form.perKmRate}
                onChange={(event) => setForm((f) => ({ ...f, perKmRate: event.target.value }))}
              />
            </FormField>
            <FormField label="Prijs per pallet (€)" htmlFor="rc-pallet">
              <input
                id="rc-pallet"
                type="number"
                min={0}
                step="0.01"
                value={form.perPalletRate}
                onChange={(event) => setForm((f) => ({ ...f, perPalletRate: event.target.value }))}
              />
            </FormField>
            <FormField label="Prijs per ton (€)" htmlFor="rc-ton">
              <input
                id="rc-ton"
                type="number"
                min={0}
                step="0.01"
                value={form.perTonRate}
                onChange={(event) => setForm((f) => ({ ...f, perTonRate: event.target.value }))}
              />
            </FormField>
            <FormField label="Minimumtarief (€)" htmlFor="rc-min">
              <input
                id="rc-min"
                type="number"
                min={0}
                step="0.01"
                value={form.minimumAmount}
                onChange={(event) => setForm((f) => ({ ...f, minimumAmount: event.target.value }))}
              />
            </FormField>

            <FormField label="Toeslagen" htmlFor="rc-surcharges" error={getFieldError(fieldErrors, 'surcharges')}>
              <div id="rc-surcharges">
                {form.surcharges.map((surcharge, index) => (
                  <div key={index} style={{ display: 'flex', gap: '0.4rem', marginBottom: '0.4rem' }}>
                    <input
                      placeholder="Naam (bv. Diesel)"
                      value={surcharge.name}
                      onChange={(event) =>
                        setForm((f) => ({
                          ...f,
                          surcharges: f.surcharges.map((s, i) => (i === index ? { ...s, name: event.target.value } : s)),
                        }))
                      }
                    />
                    <select
                      value={surcharge.kind}
                      aria-label="Toeslagtype"
                      onChange={(event) =>
                        setForm((f) => ({
                          ...f,
                          surcharges: f.surcharges.map((s, i) =>
                            i === index ? { ...s, kind: event.target.value as SurchargeKind } : s,
                          ),
                        }))
                      }
                    >
                      {Object.entries(SURCHARGE_KIND_LABELS).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                    <input
                      type="number"
                      step="0.01"
                      min={0}
                      style={{ width: '6rem' }}
                      aria-label="Waarde"
                      value={surcharge.value}
                      onChange={(event) =>
                        setForm((f) => ({
                          ...f,
                          surcharges: f.surcharges.map((s, i) => (i === index ? { ...s, value: event.target.value } : s)),
                        }))
                      }
                    />
                    <Button
                      variant="secondary"
                      onClick={() =>
                        setForm((f) => ({ ...f, surcharges: f.surcharges.filter((_, i) => i !== index) }))
                      }
                    >
                      ×
                    </Button>
                  </div>
                ))}
                <Button
                  variant="secondary"
                  onClick={() =>
                    setForm((f) => ({ ...f, surcharges: [...f.surcharges, { name: '', kind: 'Percent', value: '' }] }))
                  }
                >
                  Toeslag toevoegen
                </Button>
              </div>
            </FormField>

            <FormField label="Notities" htmlFor="rc-notes">
              <textarea
                id="rc-notes"
                value={form.notes}
                onChange={(event) => setForm((f) => ({ ...f, notes: event.target.value }))}
                rows={2}
                maxLength={2000}
              />
            </FormField>
          </form>
        </Modal>
      )}

      {quoteOpen && (
        <Modal
          title="Prijs berekenen"
          onClose={() => setQuoteOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setQuoteOpen(false)} disabled={busy}>
                Sluiten
              </Button>
              <Button onClick={() => void runQuote()} disabled={busy}>
                Berekenen
              </Button>
            </>
          }
        >
          <ValidationSummary message={quoteError} />
          <FormField label="Klant" htmlFor="q-customer" required>
            <SearchableSelect id="q-customer" value={quoteCustomerId} onChange={setQuoteCustomerId} options={customerOptions} />
          </FormField>
          <FormField label="Datum" htmlFor="q-date" required>
            <input id="q-date" type="date" value={quoteDate} onChange={(event) => setQuoteDate(event.target.value)} />
          </FormField>
          <FormField label="Afstand (km)" htmlFor="q-km">
            <input id="q-km" type="number" min={0} step="0.1" value={quoteKm} onChange={(event) => setQuoteKm(event.target.value)} />
          </FormField>
          <FormField label="Aantal pallets" htmlFor="q-pallets">
            <input
              id="q-pallets"
              type="number"
              min={0}
              step="1"
              value={quotePallets}
              onChange={(event) => setQuotePallets(event.target.value)}
            />
          </FormField>
          <FormField label="Gewicht (kg)" htmlFor="q-kg">
            <input id="q-kg" type="number" min={0} step="1" value={quoteKg} onChange={(event) => setQuoteKg(event.target.value)} />
          </FormField>

          {quote && (
            <div>
              <h3>
                {quote.rateCardName} — {euro(quote.total)}
              </h3>
              <ul>
                {quote.lines.map((line, index) => (
                  <li key={index}>
                    {line.label}: {euro(line.amount)}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Modal>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Tarievenkaart verwijderen"
          message={`Weet je zeker dat je "${confirmDelete.name}" van ${confirmDelete.customerName} wil verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onCancel={() => setConfirmDelete(null)}
          onConfirm={() => {
            setBusy(true)
            deleteRateCard(confirmDelete.id)
              .then(() => {
                toast.showSuccess('Tarievenkaart verwijderd.')
                setConfirmDelete(null)
                reload()
              })
              .catch(() => toast.showError('De tarievenkaart kon niet worden verwijderd.'))
              .finally(() => setBusy(false))
          }}
        />
      )}
    </div>
  )
}
