import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { formatDate } from '../../../utils/dates'
import { createTankCard, listEmployeeTankCards, searchTankCards, updateTankCard } from '../../tank-cards/api/tankCardsApi'
import {
  maskCardNumber,
  tankCardToInput,
  TANK_CARD_STATUS_LABELS,
  type TankCard,
  type TankCardStatus,
} from '../../tank-cards/types'
import './EmployeeTankCardsSection.css'

const STATUS_TONE: Record<TankCardStatus, BadgeTone> = {
  Active: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Blocked: 'danger',
}

interface NewCardForm {
  cardNumber: string
  provider: string
  internalName: string
  validFrom: string
  validUntil: string
}

const EMPTY_NEW_CARD: NewCardForm = {
  cardNumber: '',
  provider: '',
  internalName: '',
  validFrom: '',
  validUntil: '',
}

/** "dag €x · week €y · maand €z" with any unset limit omitted; null when none are set. */
function limitsSummary(card: TankCard): string | null {
  const parts: string[] = []
  if (card.dailyLimit != null) parts.push(`dag €${card.dailyLimit}`)
  if (card.weeklyLimit != null) parts.push(`week €${card.weeklyLimit}`)
  if (card.monthlyLimit != null) parts.push(`maand €${card.monthlyLimit}`)
  return parts.length > 0 ? parts.join(' · ') : null
}

/**
 * Employee-dossier counterpart to the fleet Tankkaarten page (HR maturity wave, task 12): shows
 * the cards linked to this employee and lets HR link an existing free card, unlink one, or issue
 * a brand new one — without ever re-entering a card that already exists in the fleet module.
 */
export function EmployeeTankCardsSection({ employeeId }: { employeeId: string }) {
  const { hasPermission } = useAuth()
  const toast = useToast()
  const canView = hasPermission('tank_cards.view')
  const canEdit = hasPermission('tank_cards.edit')
  const canCreate = hasPermission('tank_cards.create')

  const [cards, setCards] = useState<TankCard[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [linkOpen, setLinkOpen] = useState(false)
  const [availableCards, setAvailableCards] = useState<TankCard[]>([])
  const [availableLoading, setAvailableLoading] = useState(false)
  const [selectedCardId, setSelectedCardId] = useState('')
  const [linkError, setLinkError] = useState<string | null>(null)

  const [unlinkTarget, setUnlinkTarget] = useState<TankCard | null>(null)

  const [createOpen, setCreateOpen] = useState(false)
  const [newCard, setNewCard] = useState<NewCardForm>(EMPTY_NEW_CARD)
  const [createError, setCreateError] = useState<string | null>(null)

  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!canView) return
    let mounted = true
    listEmployeeTankCards(employeeId)
      .then((data) => {
        if (!mounted) return
        setCards(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Tankkaarten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, canView, reloadToken])

  if (!canView) return null

  function reload() {
    setReloadToken((t) => t + 1)
  }

  function openLink() {
    setSelectedCardId('')
    setLinkError(null)
    setLinkOpen(true)
    setAvailableLoading(true)
    searchTankCards({ available: true, page: 1, pageSize: 200 })
      .then((result) => setAvailableCards(result.items))
      .catch(() => setAvailableCards([]))
      .finally(() => setAvailableLoading(false))
  }

  async function handleLinkSubmit(event: FormEvent) {
    event.preventDefault()
    setLinkError(null)
    if (!selectedCardId) {
      setLinkError('Kies een tankkaart.')
      return
    }
    const card = availableCards.find((c) => c.id === selectedCardId)
    if (!card) {
      setLinkError('Deze tankkaart is niet meer beschikbaar.')
      return
    }
    setSaving(true)
    try {
      await updateTankCard(card.id, tankCardToInput(card, { employeeId }))
      toast.showSuccess('Tankkaart gekoppeld.')
      setLinkOpen(false)
      reload()
    } catch (err) {
      setLinkError(describeApiError(err, 'De tankkaart kon niet worden gekoppeld.').message)
    } finally {
      setSaving(false)
    }
  }

  async function handleUnlink() {
    if (!unlinkTarget) return
    setSaving(true)
    try {
      await updateTankCard(unlinkTarget.id, tankCardToInput(unlinkTarget, { employeeId: null }))
      toast.showSuccess('Tankkaart ontkoppeld.')
      setUnlinkTarget(null)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'De tankkaart kon niet worden ontkoppeld.').message)
      setUnlinkTarget(null)
    } finally {
      setSaving(false)
    }
  }

  function openCreate() {
    setNewCard(EMPTY_NEW_CARD)
    setCreateError(null)
    setCreateOpen(true)
  }

  async function handleCreateSubmit(event: FormEvent) {
    event.preventDefault()
    setCreateError(null)
    if (!newCard.cardNumber.trim()) {
      setCreateError('Kaartnummer is verplicht.')
      return
    }
    if (!newCard.provider.trim()) {
      setCreateError('Leverancier is verplicht.')
      return
    }
    setSaving(true)
    try {
      await createTankCard({
        cardNumber: newCard.cardNumber.trim(),
        provider: newCard.provider.trim(),
        vehicleId: null,
        employeeId,
        validFrom: newCard.validFrom || null,
        validUntil: newCard.validUntil || null,
        internalName: newCard.internalName.trim() || null,
        fuelType: null,
        dailyLimit: null,
        weeklyLimit: null,
        monthlyLimit: null,
        costCenter: null,
        notes: null,
      })
      toast.showSuccess('Tankkaart aangemaakt.')
      setCreateOpen(false)
      reload()
    } catch (err) {
      setCreateError(describeApiError(err, 'De tankkaart kon niet worden aangemaakt.').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="employee-tank-cards">
      <div className="employee-tank-cards-header">
        <h2>Tankkaarten</h2>
        <div className="employee-tank-cards-actions-top">
          {canEdit && (
            <Button variant="secondary" onClick={openLink}>
              Bestaande kaart koppelen
            </Button>
          )}
          {canCreate && <Button onClick={openCreate}>Nieuwe kaart</Button>}
        </div>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && cards === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && cards !== null && cards.length === 0 && (
        <p className="placeholder-text">Geen tankkaarten gekoppeld.</p>
      )}

      {!loadError && cards !== null && cards.length > 0 && (
        <ul className="employee-tank-cards-list">
          {cards.map((card) => {
            const summary = limitsSummary(card)
            return (
              <li key={card.id} className="employee-tank-card">
                <div className="employee-tank-card-main">
                  <div className="employee-tank-card-title">
                    <code>{maskCardNumber(card.cardNumber)}</code>
                    {card.internalName && <span className="employee-tank-card-internal-name"> — {card.internalName}</span>}
                  </div>
                  <div className="employee-tank-card-meta">
                    {card.provider}
                    {' · '}
                    Geldig tot {formatDate(card.validUntil) || '—'}
                    {summary && <span> · {summary}</span>}
                  </div>
                </div>
                <div className="employee-tank-card-side">
                  <Badge tone={STATUS_TONE[card.status]}>{TANK_CARD_STATUS_LABELS[card.status]}</Badge>
                  {canEdit && (
                    <button
                      type="button"
                      className="employee-tank-card-link employee-tank-card-link-danger"
                      onClick={() => setUnlinkTarget(card)}
                    >
                      Ontkoppelen
                    </button>
                  )}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {linkOpen && (
        <Modal
          title="Bestaande kaart koppelen"
          onClose={() => setLinkOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setLinkOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="etc-link-form" disabled={saving || availableCards.length === 0}>
                {saving ? 'Koppelen…' : 'Koppelen'}
              </Button>
            </>
          }
        >
          <form id="etc-link-form" className="employee-tank-cards-form" onSubmit={handleLinkSubmit} noValidate>
            {linkError && (
              <div className="employee-tank-cards-form-error" role="alert">
                {linkError}
              </div>
            )}
            <FormField label="Tankkaart" htmlFor="etc-link-select" required>
              <select
                id="etc-link-select"
                value={selectedCardId}
                onChange={(e) => setSelectedCardId(e.target.value)}
                disabled={saving || availableLoading}
              >
                <option value="">
                  {availableLoading ? 'Laden…' : availableCards.length === 0 ? 'Geen beschikbare kaarten' : '— Kies een tankkaart —'}
                </option>
                {availableCards.map((card) => (
                  <option key={card.id} value={card.id}>
                    {maskCardNumber(card.cardNumber)} — {card.provider}
                    {card.internalName ? ` (${card.internalName})` : ''}
                  </option>
                ))}
              </select>
            </FormField>
          </form>
        </Modal>
      )}

      {createOpen && (
        <Modal
          title="Nieuwe tankkaart"
          onClose={() => setCreateOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCreateOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="etc-create-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="etc-create-form" className="employee-tank-cards-form" onSubmit={handleCreateSubmit} noValidate>
            {createError && (
              <div className="employee-tank-cards-form-error" role="alert">
                {createError}
              </div>
            )}
            <FormField label="Kaartnummer" htmlFor="etc-number" required>
              <input
                id="etc-number"
                value={newCard.cardNumber}
                onChange={(e) => setNewCard((f) => ({ ...f, cardNumber: e.target.value }))}
                disabled={saving}
                maxLength={50}
              />
            </FormField>
            <FormField label="Leverancier" htmlFor="etc-provider" required>
              <input
                id="etc-provider"
                value={newCard.provider}
                onChange={(e) => setNewCard((f) => ({ ...f, provider: e.target.value }))}
                disabled={saving}
                maxLength={100}
                placeholder="bv. DKV, Shell, Total"
              />
            </FormField>
            <FormField label="Interne naam" htmlFor="etc-internal-name">
              <input
                id="etc-internal-name"
                value={newCard.internalName}
                onChange={(e) => setNewCard((f) => ({ ...f, internalName: e.target.value }))}
                disabled={saving}
                maxLength={100}
              />
            </FormField>
            <div className="employee-tank-cards-form-row">
              <FormField label="Geldig van" htmlFor="etc-from">
                <input
                  id="etc-from"
                  type="date"
                  value={newCard.validFrom}
                  onChange={(e) => setNewCard((f) => ({ ...f, validFrom: e.target.value }))}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Geldig tot" htmlFor="etc-until">
                <input
                  id="etc-until"
                  type="date"
                  value={newCard.validUntil}
                  onChange={(e) => setNewCard((f) => ({ ...f, validUntil: e.target.value }))}
                  disabled={saving}
                />
              </FormField>
            </div>
          </form>
        </Modal>
      )}

      {unlinkTarget && (
        <ConfirmDialog
          title="Tankkaart ontkoppelen"
          message={`Weet je zeker dat je kaart ${maskCardNumber(unlinkTarget.cardNumber)} (${unlinkTarget.provider}) wilt ontkoppelen van deze medewerker?`}
          confirmLabel="Ontkoppelen"
          destructive
          onConfirm={handleUnlink}
          onCancel={() => setUnlinkTarget(null)}
        />
      )}
    </section>
  )
}
