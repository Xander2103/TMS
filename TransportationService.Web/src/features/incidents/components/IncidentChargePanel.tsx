import { useState } from 'react'
import { Link } from 'react-router-dom'
import './incidents.css'
import { formatCurrency } from '../../../utils/numbers'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { localizeApiError } from '../../../api/problemDetails'
import {
  createIncidentRedelivery,
  decideIncidentCharge,
  proposeIncidentCharge,
} from '../api/incidentsApi'
import { CHARGE_DECISION_LABELS, RESPONSIBLE_PARTY_LABELS, type IncidentDetail } from '../types'

interface IncidentChargePanelProps {
  incident: IncidentDetail
  onUpdated: (incident: IncidentDetail) => void
  /**
   * B24: the detail page shows responsibility/charge and redelivery as two cards (their titles
   * come from the card). 'all' keeps the original single section with its own headings.
   */
  part?: 'all' | 'charge' | 'redelivery'
}

/**
 * Wave 6 §2+§3: verantwoordelijkheid, doorrekening en herlevering. Voorstellen kan met
 * incidents.manage; goed-/afkeuren vereist problems.approve_charge (server bewaakt
 * fail-closed). Goedkeuring maakt de verkooplijn op de gekoppelde order aan.
 */
export function IncidentChargePanel({ incident, onUpdated, part = 'all' }: IncidentChargePanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()
  const canManage = hasPermission('incidents.manage')
  const canDecide = hasPermission('problems.approve_charge')
  // The dispatcher redelivers through the controlled command (problems.create_redelivery).
  const canRedeliver = hasPermission('orders.create') || hasPermission('orders.manage') || hasPermission('problems.create_redelivery')

  const [amount, setAmount] = useState(incident.chargeAmount?.toString() ?? '')
  const [description, setDescription] = useState(incident.chargeDescription ?? '')
  const [busy, setBusy] = useState(false)
  const [confirmRedelivery, setConfirmRedelivery] = useState(false)
  const [redeliveryQuantity, setRedeliveryQuantity] = useState('')
  const [redeliveryFree, setRedeliveryFree] = useState(false)
  const [redeliveryFreeReason, setRedeliveryFreeReason] = useState('')
  // B24: while a proposal awaits its decision the form to change it stays folded away.
  const [reproposing, setReproposing] = useState(false)

  async function run(action: () => Promise<IncidentDetail>, message: string) {
    setBusy(true)
    try {
      onUpdated(await action())
      showSuccess(message)
    } catch (err) {
      // Never a raw permission/technical message: the stable code is translated first.
      showError(localizeApiError(t, err, t('incidents.charge.actionFailed')))
    } finally {
      setBusy(false)
    }
  }

  const chargeTone = incident.chargeDecision === 'Approved'
    ? 'success' as const
    : incident.chargeDecision === 'Rejected'
      ? 'danger' as const
      : incident.chargeDecision === 'Proposed'
        ? 'warning' as const
        : 'neutral' as const

  const charge = (
    <>
      <p className="inc-charge-line">
        {t('incidents.charge.responsibleParty')}{' '}
        <strong>{t(RESPONSIBLE_PARTY_LABELS[incident.responsibleParty] ?? incident.responsibleParty)}</strong>
        {incident.responsibilityNotes && <span className="customer-form-muted"> — {incident.responsibilityNotes}</span>}
      </p>
      <p className="inc-charge-line">
        {t('incidents.charge.charge')}{' '}
        <Badge tone={chargeTone}>{t(CHARGE_DECISION_LABELS[incident.chargeDecision] ?? incident.chargeDecision)}</Badge>
        {incident.chargeAmount !== null && <strong> {formatCurrency(incident.chargeAmount)}</strong>}
        {incident.chargeDescription && <span className="customer-form-muted"> — {incident.chargeDescription}</span>}
      </p>

      {canManage && incident.responsibleParty === 'Customer' && incident.chargeDecision === 'Proposed' && !reproposing && (
        <button type="button" className="inc-charge-repropose" onClick={() => setReproposing(true)}>
          {t('incidents.charge.changeProposal')}
        </button>
      )}
      {canManage && incident.responsibleParty === 'Customer' && incident.chargeDecision !== 'Approved'
        && (incident.chargeDecision !== 'Proposed' || reproposing) && (
        <div className="inc-charge-row">
          <FormField label={t('incidents.charge.amountLabel')} htmlFor="inc-charge-amount">
            <input
              id="inc-charge-amount"
              type="number"
              min={0.01}
              step="0.01"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              disabled={busy}
            />
          </FormField>
          <FormField label={t('incidents.charge.descriptionLabel')} htmlFor="inc-charge-desc">
            <input
              id="inc-charge-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={500}
              disabled={busy}
            />
          </FormField>
          <Button
            variant="secondary"
            disabled={busy || !amount || Number(amount) <= 0 || !description.trim()}
            onClick={() => {
              setReproposing(false)
              void run(
                () => proposeIncidentCharge(incident.id, Number(amount), description.trim()),
                t('incidents.charge.proposed'),
              )
            }}
          >
            {t('incidents.charge.propose')}
          </Button>
        </div>
      )}
      {incident.responsibleParty !== 'Customer' && incident.chargeDecision === 'None' && (
        <p className="customer-form-muted">
          {t('incidents.charge.onlyCustomer')}
        </p>
      )}

      {canDecide && incident.chargeDecision === 'Proposed' && (
        <div className="inc-charge-row">
          <Button
            disabled={busy}
            onClick={() => void run(() => decideIncidentCharge(incident.id, true), t('incidents.charge.approved'))}
          >
            {t('incidents.charge.approve')}
          </Button>
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => void run(() => decideIncidentCharge(incident.id, false), t('incidents.charge.rejected'))}
          >
            {t('incidents.charge.reject')}
          </Button>
        </div>
      )}

    </>
  )

  const redelivery = (
    <>
      {incident.redeliverySuggested && !incident.linkedRedeliveryOrderNumber && (
        <p role="status" className="inc-redelivery-hint">
          ⚠ {t('incidents.charge.redeliverySuggested')}
        </p>
      )}
      {incident.linkedRedeliveryOrderId ? (
        <p className="inc-charge-line">
          {t('incidents.charge.redeliveryOrder')}{' '}
          <Link className="ui-id-link" to={`/transport-orders/${incident.linkedRedeliveryOrderId}`}>
            {incident.linkedRedeliveryOrderNumber}
          </Link>
        </p>
      ) : incident.transportOrderId ? (
        canRedeliver ? (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => {
              setRedeliveryQuantity('')
              setRedeliveryFree(false)
              setRedeliveryFreeReason('')
              setConfirmRedelivery(true)
            }}
          >
            {t('incidents.charge.createRedelivery')}
          </Button>
        ) : (
          <p className="customer-form-muted">{t('incidents.charge.noOrderRights')}</p>
        )
      ) : (
        <p className="customer-form-muted">{t('incidents.charge.linkOrderFirst')}</p>
      )}
      {confirmRedelivery && (
        // M13: a redelivery creates new operational work in the dossier — confirm it first. It re-delivers
        // what was NOT delivered and starts unpriced (review) unless it is explicitly confirmed free.
        <Modal
          title={t('incidents.charge.redeliveryConfirmTitle')}
          onClose={() => setConfirmRedelivery(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setConfirmRedelivery(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button
                disabled={busy || (redeliveryFree && !redeliveryFreeReason.trim()) || (redeliveryQuantity !== '' && Number(redeliveryQuantity) <= 0)}
                onClick={() => {
                  setConfirmRedelivery(false)
                  void run(
                    () => createIncidentRedelivery(incident.id, {
                      quantity: redeliveryQuantity === '' ? null : Number(redeliveryQuantity),
                      free: redeliveryFree,
                      freeReason: redeliveryFree ? redeliveryFreeReason.trim() : null,
                    }),
                    t('incidents.charge.redeliveryCreated'),
                  )
                }}
              >
                {t('incidents.charge.createRedelivery')}
              </Button>
            </>
          }
        >
          <p>
            {t('incidents.charge.redeliveryConfirmMessage', {
              order: incident.transportOrderNumber ?? '—',
              dossier: incident.dossierNumber ?? '—',
            })}
          </p>
          <FormField label={t('incidents.charge.redeliveryQuantity')} htmlFor="inc-redelivery-qty" hint={t('incidents.charge.redeliveryQuantityHint')}>
            <input
              id="inc-redelivery-qty"
              type="number"
              min={0.01}
              step="0.01"
              value={redeliveryQuantity}
              onChange={(e) => setRedeliveryQuantity(e.target.value)}
            />
          </FormField>
          <label className="tof-checkbox">
            <input type="checkbox" checked={redeliveryFree} onChange={(e) => setRedeliveryFree(e.target.checked)} />
            {' '}{t('incidents.charge.redeliveryFree')}
          </label>
          {redeliveryFree && (
            <FormField label={t('incidents.charge.redeliveryFreeReason')} htmlFor="inc-redelivery-free-reason" required>
              <input
                id="inc-redelivery-free-reason"
                value={redeliveryFreeReason}
                onChange={(e) => setRedeliveryFreeReason(e.target.value)}
                maxLength={300}
              />
            </FormField>
          )}
          {!redeliveryFree && <p className="customer-form-muted">{t('incidents.charge.redeliveryUnpriced')}</p>}
        </Modal>
      )}
    </>
  )

  if (part === 'charge') return charge
  if (part === 'redelivery') return redelivery
  return (
    <section className="ui-form-section">
      <h3>{t('incidents.charge.title')}</h3>
      {charge}
      <h3>{t('incidents.charge.redeliveryTitle')}</h3>
      {redelivery}
    </section>
  )
}
