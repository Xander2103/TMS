import { useEffect, useState, type FormEvent } from 'react'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { formatDate } from '../../../utils/dates'
import { createTankCard, listEmployeeTankCards, searchTankCards, updateTankCard } from '../../tank-cards/api/tankCardsApi'
import { maskCardNumber, tankCardToInput, type TankCard, type TankCardStatus } from '../../tank-cards/types'
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
function limitsSummary(card: TankCard, t: TranslateFn): string | null {
  const parts: string[] = []
  if (card.dailyLimit != null) parts.push(t('employees.tankCards.limitDay', { amount: card.dailyLimit }))
  if (card.weeklyLimit != null) parts.push(t('employees.tankCards.limitWeek', { amount: card.weeklyLimit }))
  if (card.monthlyLimit != null) parts.push(t('employees.tankCards.limitMonth', { amount: card.monthlyLimit }))
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
  const { t } = useLocale()
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
        if (mounted) setLoadError('employees.tankCards.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, canView, reloadToken])

  if (!canView) return null

  function reload() {
    setReloadToken((token) => token + 1)
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
      setLinkError(t('employees.tankCards.chooseCardRequired'))
      return
    }
    const card = availableCards.find((c) => c.id === selectedCardId)
    if (!card) {
      setLinkError(t('employees.tankCards.cardUnavailable'))
      return
    }
    setSaving(true)
    try {
      await updateTankCard(card.id, tankCardToInput(card, { employeeId }))
      toast.showSuccess(t('employees.tankCards.linked'))
      setLinkOpen(false)
      reload()
    } catch (err) {
      setLinkError(describeApiError(err, t('employees.tankCards.linkFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleUnlink() {
    if (!unlinkTarget) return
    setSaving(true)
    try {
      await updateTankCard(unlinkTarget.id, tankCardToInput(unlinkTarget, { employeeId: null }))
      toast.showSuccess(t('employees.tankCards.unlinked'))
      setUnlinkTarget(null)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('employees.tankCards.unlinkFailed')).message)
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
      setCreateError(t('employees.tankCards.cardNumberRequired'))
      return
    }
    if (!newCard.provider.trim()) {
      setCreateError(t('employees.tankCards.providerRequired'))
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
      toast.showSuccess(t('employees.tankCards.created'))
      setCreateOpen(false)
      reload()
    } catch (err) {
      setCreateError(describeApiError(err, t('employees.tankCards.createFailed')).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="employee-tank-cards">
      <div className="employee-tank-cards-header">
        <h2>{t('employees.tankCards.heading')}</h2>
        <div className="employee-tank-cards-actions-top">
          {canEdit && (
            <Button variant="secondary" onClick={openLink}>
              {t('employees.tankCards.linkExisting')}
            </Button>
          )}
          {canCreate && <Button onClick={openCreate}>{t('employees.tankCards.newCard')}</Button>}
        </div>
      </div>

      {loadError && <p className="placeholder-text">{t(loadError)}</p>}
      {!loadError && cards === null && <p className="placeholder-text">{t('employees.tankCards.loading')}</p>}
      {!loadError && cards !== null && cards.length === 0 && (
        <p className="placeholder-text">{t('employees.tankCards.empty')}</p>
      )}

      {!loadError && cards !== null && cards.length > 0 && (
        <ul className="employee-tank-cards-list">
          {cards.map((card) => {
            const summary = limitsSummary(card, t)
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
                    {t('employees.tankCards.validUntil', { date: formatDate(card.validUntil) || '—' })}
                    {summary && <span> · {summary}</span>}
                  </div>
                </div>
                <div className="employee-tank-card-side">
                  <Badge tone={STATUS_TONE[card.status]}>{t(`tankCards.status.${card.status}`)}</Badge>
                  {canEdit && (
                    <button
                      type="button"
                      className="employee-tank-card-link employee-tank-card-link-danger"
                      onClick={() => setUnlinkTarget(card)}
                    >
                      {t('employees.tankCards.unlink')}
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
          title={t('employees.tankCards.linkTitle')}
          onClose={() => setLinkOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setLinkOpen(false)} disabled={saving}>
                {t('employees.tankCards.cancel')}
              </Button>
              <Button type="submit" form="etc-link-form" disabled={saving || availableCards.length === 0}>
                {saving ? t('employees.tankCards.linking') : t('employees.tankCards.link')}
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
            <FormField label={t('employees.tankCards.cardField')} htmlFor="etc-link-select" required>
              <select
                id="etc-link-select"
                value={selectedCardId}
                onChange={(e) => setSelectedCardId(e.target.value)}
                disabled={saving || availableLoading}
              >
                <option value="">
                  {availableLoading
                    ? t('employees.tankCards.optionLoading')
                    : availableCards.length === 0
                      ? t('employees.tankCards.optionNoneAvailable')
                      : t('employees.tankCards.optionChoose')}
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
          title={t('employees.tankCards.createTitle')}
          onClose={() => setCreateOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCreateOpen(false)} disabled={saving}>
                {t('employees.tankCards.cancel')}
              </Button>
              <Button type="submit" form="etc-create-form" disabled={saving}>
                {saving ? t('employees.tankCards.saving') : t('employees.tankCards.save')}
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
            <FormField label={t('employees.tankCards.cardNumber')} htmlFor="etc-number" required>
              <input
                id="etc-number"
                value={newCard.cardNumber}
                onChange={(e) => setNewCard((f) => ({ ...f, cardNumber: e.target.value }))}
                disabled={saving}
                maxLength={50}
              />
            </FormField>
            <FormField label={t('employees.tankCards.provider')} htmlFor="etc-provider" required>
              <input
                id="etc-provider"
                value={newCard.provider}
                onChange={(e) => setNewCard((f) => ({ ...f, provider: e.target.value }))}
                disabled={saving}
                maxLength={100}
                placeholder={t('employees.tankCards.providerPlaceholder')}
              />
            </FormField>
            <FormField label={t('employees.tankCards.internalName')} htmlFor="etc-internal-name">
              <input
                id="etc-internal-name"
                value={newCard.internalName}
                onChange={(e) => setNewCard((f) => ({ ...f, internalName: e.target.value }))}
                disabled={saving}
                maxLength={200}
              />
            </FormField>
            <div className="employee-tank-cards-form-row">
              <FormField label={t('employees.tankCards.validFrom')} htmlFor="etc-from">
                <input
                  id="etc-from"
                  type="date"
                  value={newCard.validFrom}
                  onChange={(e) => setNewCard((f) => ({ ...f, validFrom: e.target.value }))}
                  disabled={saving}
                />
              </FormField>
              <FormField label={t('employees.tankCards.validUntilField')} htmlFor="etc-until">
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
          title={t('employees.tankCards.unlinkTitle')}
          message={t('employees.tankCards.unlinkMessage', {
            card: maskCardNumber(unlinkTarget.cardNumber),
            provider: unlinkTarget.provider,
          })}
          confirmLabel={t('employees.tankCards.unlinkConfirm')}
          destructive
          onConfirm={handleUnlink}
          onCancel={() => setUnlinkTarget(null)}
        />
      )}
    </section>
  )
}
