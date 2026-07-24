import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { euro } from '../../invoices/types'
import { createRateCard, deleteRateCard, listRateCards, updateRateCard } from '../../tarification/api/rateCardsApi'
import { SURCHARGE_KIND_LABELS, type RateCard, type SurchargeKind } from '../../tarification/types'

interface CustomerRateCardsPanelProps {
  customerId: string
}

interface CardDraft {
  card: RateCard | null
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

const num = (value: string): number | null => (value.trim() === '' ? null : Number(value))

/**
 * Customer-scoped view of the sell-side rate cards ("Tarieven & toeslagen"), so pricing can
 * be inspected and edited from the customer itself instead of only via /rate-cards.
 */
export function CustomerRateCardsPanel({ customerId }: CustomerRateCardsPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canView = hasPermission('tariffs.view') || hasPermission('tariffs.manage')
  const canManage = hasPermission('tariffs.manage')

  const [cards, setCards] = useState<RateCard[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<CardDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<RateCard | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listRateCards(customerId)
      .then((data) => {
        setCards(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De tarievenkaarten konden niet worden geladen.'))
  }, [customerId, canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) {
    return <p className="customer-form-muted">Je hebt geen rechten om verkooptarieven te bekijken.</p>
  }

  function openDraft(card: RateCard | null) {
    setDraftError(null)
    setDraft(
      card
        ? {
            card,
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
        : {
            card: null,
            name: '',
            effectiveFrom: new Date().toISOString().slice(0, 10),
            effectiveUntil: '',
            baseAmount: '0',
            perKmRate: '',
            perPalletRate: '',
            perTonRate: '',
            minimumAmount: '',
            notes: '',
            surcharges: [],
          },
    )
  }

  async function submitDraft(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.name.trim() || !draft.effectiveFrom) {
      setDraftError('Naam en ingangsdatum zijn verplicht.')
      return
    }
    setBusy(true)
    try {
      const input = {
        customerId,
        name: draft.name.trim(),
        effectiveFrom: draft.effectiveFrom,
        effectiveUntil: draft.effectiveUntil || null,
        baseAmount: Number(draft.baseAmount) || 0,
        perKmRate: num(draft.perKmRate),
        perPalletRate: num(draft.perPalletRate),
        perTonRate: num(draft.perTonRate),
        minimumAmount: num(draft.minimumAmount),
        notes: draft.notes.trim() || null,
        surcharges: draft.surcharges
          .filter((s) => s.name.trim())
          .map((s) => ({ name: s.name.trim(), kind: s.kind, value: Number(s.value) || 0 })),
      }
      if (draft.card) {
        await updateRateCard(draft.card.id, input)
        showSuccess('Tarievenkaart bijgewerkt.')
      } else {
        await createRateCard(input)
        showSuccess('Tarievenkaart toegevoegd.')
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De tarievenkaart kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!confirmDelete) return
    const target = confirmDelete
    setConfirmDelete(null)
    try {
      await deleteRateCard(target.id)
      showSuccess('Tarievenkaart verwijderd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De tarievenkaart kon niet worden verwijderd.').message)
    }
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Verkooptarieven</h3>
        <div className="customer-panel-actions">
          {canManage && <Button onClick={() => openDraft(null)}>+ Tarievenkaart</Button>}
          <Link className="issued-items-manage-link" to={`/rate-cards?customerId=${customerId}`}>
            Alle tarievenkaarten
          </Link>
        </div>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && cards === null && <p className="placeholder-text">Laden…</p>}
      {cards !== null && cards.length === 0 && (
        <p className="placeholder-text">Nog geen tarievenkaart voor deze klant.</p>
      )}
      {cards !== null && cards.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Geldig</th>
              <th>Basis</th>
              <th>Per km / pallet / ton</th>
              <th>Minimum</th>
              <th>Toeslagen</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {cards.map((card) => (
              <tr key={card.id}>
                <td>{card.name}</td>
                <td>
                  {card.effectiveFrom}
                  {card.effectiveUntil ? ` – ${card.effectiveUntil}` : ' →'}
                </td>
                <td>{euro(card.baseAmount)}</td>
                <td>
                  {[card.perKmRate, card.perPalletRate, card.perTonRate]
                    .map((rate) => (rate !== null ? euro(rate) : '—'))
                    .join(' / ')}
                </td>
                <td>{card.minimumAmount !== null ? euro(card.minimumAmount) : '—'}</td>
                <td>
                  {card.surcharges.length > 0 ? <Badge tone="info">{card.surcharges.length}</Badge> : '—'}
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button type="button" className="issued-items-link" onClick={() => openDraft(card)}>
                      Bewerken
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setConfirmDelete(card)}
                    >
                      Verwijderen
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {draft && (
        <Modal
          title={draft.card ? `Tarievenkaart bewerken — ${draft.card.name}` : 'Tarievenkaart toevoegen'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="rate-card-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="rate-card-form" className="issued-items-form" onSubmit={submitDraft} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Naam" htmlFor="rc-name" required>
                <input id="rc-name" value={draft.name} onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={150} />
              </FormField>
              <FormField label="Basisbedrag (€)" htmlFor="rc-base">
                <input id="rc-base" type="number" step="0.01" value={draft.baseAmount} onChange={(e) => setDraft((d) => (d ? { ...d, baseAmount: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Geldig vanaf" htmlFor="rc-from" required>
                <input id="rc-from" type="date" value={draft.effectiveFrom} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))} />
              </FormField>
              <FormField label="Geldig tot" htmlFor="rc-until" hint="Leeg = onbeperkt.">
                <input id="rc-until" type="date" value={draft.effectiveUntil} onChange={(e) => setDraft((d) => (d ? { ...d, effectiveUntil: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Per km (€)" htmlFor="rc-km">
                <input id="rc-km" type="number" step="0.01" value={draft.perKmRate} onChange={(e) => setDraft((d) => (d ? { ...d, perKmRate: e.target.value } : d))} />
              </FormField>
              <FormField label="Per pallet (€)" htmlFor="rc-pallet">
                <input id="rc-pallet" type="number" step="0.01" value={draft.perPalletRate} onChange={(e) => setDraft((d) => (d ? { ...d, perPalletRate: e.target.value } : d))} />
              </FormField>
            </div>
            <div className="issued-items-form-row">
              <FormField label="Per ton (€)" htmlFor="rc-ton">
                <input id="rc-ton" type="number" step="0.01" value={draft.perTonRate} onChange={(e) => setDraft((d) => (d ? { ...d, perTonRate: e.target.value } : d))} />
              </FormField>
              <FormField label="Minimumbedrag (€)" htmlFor="rc-min">
                <input id="rc-min" type="number" step="0.01" value={draft.minimumAmount} onChange={(e) => setDraft((d) => (d ? { ...d, minimumAmount: e.target.value } : d))} />
              </FormField>
            </div>
            <FormField label="Notities" htmlFor="rc-notes">
              <input id="rc-notes" value={draft.notes} onChange={(e) => setDraft((d) => (d ? { ...d, notes: e.target.value } : d))} maxLength={500} />
            </FormField>

            <fieldset className="issued-items-generate-dimension">
              <legend>Toeslagen</legend>
              {draft.surcharges.map((surcharge, index) => (
                <div key={index} className="issued-items-form-row customer-ratecard-surcharge">
                  <input
                    aria-label={`Toeslag ${index + 1} naam`}
                    placeholder="Naam (bv. Brandstoftoeslag)"
                    value={surcharge.name}
                    maxLength={100}
                    onChange={(e) =>
                      setDraft((d) =>
                        d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, name: e.target.value } : s)) } : d,
                      )
                    }
                  />
                  <select
                    aria-label={`Toeslag ${index + 1} soort`}
                    value={surcharge.kind}
                    onChange={(e) =>
                      setDraft((d) =>
                        d
                          ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, kind: e.target.value as SurchargeKind } : s)) }
                          : d,
                      )
                    }
                  >
                    {Object.entries(SURCHARGE_KIND_LABELS).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                  <input
                    aria-label={`Toeslag ${index + 1} waarde`}
                    type="number"
                    step="0.01"
                    value={surcharge.value}
                    onChange={(e) =>
                      setDraft((d) =>
                        d ? { ...d, surcharges: d.surcharges.map((s, i) => (i === index ? { ...s, value: e.target.value } : s)) } : d,
                      )
                    }
                  />
                  <Button
                    variant="ghost"
                    onClick={() => setDraft((d) => (d ? { ...d, surcharges: d.surcharges.filter((_, i) => i !== index) } : d))}
                  >
                    Verwijderen
                  </Button>
                </div>
              ))}
              <Button
                variant="secondary"
                onClick={() => setDraft((d) => (d ? { ...d, surcharges: [...d.surcharges, { name: '', kind: 'Fixed', value: '0' }] } : d))}
              >
                + Toeslag
              </Button>
            </fieldset>
          </form>
        </Modal>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Tarievenkaart verwijderen"
          message={`Weet je zeker dat je "${confirmDelete.name}" wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </section>
  )
}
