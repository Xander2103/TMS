import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  createIncidentRedelivery,
  decideIncidentCharge,
  proposeIncidentCharge,
} from '../api/incidentsApi'
import { CHARGE_DECISION_LABELS, RESPONSIBLE_PARTY_LABELS, type IncidentDetail } from '../types'

interface IncidentChargePanelProps {
  incident: IncidentDetail
  onUpdated: (incident: IncidentDetail) => void
}

/**
 * Wave 6 §2+§3: verantwoordelijkheid, doorrekening en herlevering. Voorstellen kan met
 * incidents.manage; goed-/afkeuren vereist problems.approve_charge (server bewaakt
 * fail-closed). Goedkeuring maakt de verkooplijn op de gekoppelde order aan.
 */
export function IncidentChargePanel({ incident, onUpdated }: IncidentChargePanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('incidents.manage')
  const canDecide = hasPermission('problems.approve_charge')
  const canRedeliver = hasPermission('orders.create') || hasPermission('orders.manage')

  const [amount, setAmount] = useState(incident.chargeAmount?.toString() ?? '')
  const [description, setDescription] = useState(incident.chargeDescription ?? '')
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<IncidentDetail>, message: string) {
    setBusy(true)
    try {
      onUpdated(await action())
      showSuccess(message)
    } catch (err) {
      showError(describeApiError(err, 'De actie kon niet worden uitgevoerd.').message)
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

  return (
    <section className="ui-form-section">
      <h3>Verantwoordelijkheid &amp; doorrekening</h3>
      <p>
        Verantwoordelijke partij:{' '}
        <strong>{RESPONSIBLE_PARTY_LABELS[incident.responsibleParty] ?? incident.responsibleParty}</strong>
        {incident.responsibilityNotes && <span className="customer-form-muted"> — {incident.responsibilityNotes}</span>}
      </p>
      <p>
        Doorrekening:{' '}
        <Badge tone={chargeTone}>{CHARGE_DECISION_LABELS[incident.chargeDecision] ?? incident.chargeDecision}</Badge>
        {incident.chargeAmount !== null && <strong> € {incident.chargeAmount.toFixed(2)}</strong>}
        {incident.chargeDescription && <span className="customer-form-muted"> — {incident.chargeDescription}</span>}
      </p>

      {canManage && incident.responsibleParty === 'Customer' && incident.chargeDecision !== 'Approved' && (
        <div className="wh-trace-bar">
          <FormField label="Bedrag (€)" htmlFor="inc-charge-amount">
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
          <FormField label="Omschrijving" htmlFor="inc-charge-desc">
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
            onClick={() => void run(
              () => proposeIncidentCharge(incident.id, Number(amount), description.trim()),
              'Doorrekening voorgesteld.',
            )}
          >
            Doorrekening voorstellen
          </Button>
        </div>
      )}
      {incident.responsibleParty !== 'Customer' && incident.chargeDecision === 'None' && (
        <p className="customer-form-muted">
          Alleen problemen met verantwoordelijkheid &lsquo;Klant&rsquo; kunnen worden doorgerekend; interne kosten blijven intern.
        </p>
      )}

      {canDecide && incident.chargeDecision === 'Proposed' && (
        <div className="wh-trace-bar">
          <Button
            disabled={busy}
            onClick={() => void run(() => decideIncidentCharge(incident.id, true), 'Doorrekening goedgekeurd — verkooplijn aangemaakt.')}
          >
            Goedkeuren
          </Button>
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => void run(() => decideIncidentCharge(incident.id, false), 'Doorrekening afgekeurd.')}
          >
            Afkeuren
          </Button>
        </div>
      )}

      <h3>Herlevering</h3>
      {incident.redeliverySuggested && !incident.linkedRedeliveryOrderNumber && (
        <p
          role="status"
          style={{
            background: 'var(--warning-bg)',
            border: '1px solid var(--warning-border)',
            color: 'var(--warning)',
            borderRadius: 8,
            padding: '8px 12px',
            fontWeight: 600,
          }}
        >
          ⚠ Herlevering aanbevolen na mislukte levering — controleer en maak aan.
        </p>
      )}
      {incident.linkedRedeliveryOrderId ? (
        <p>
          Herleveringsorder: <strong>{incident.linkedRedeliveryOrderNumber}</strong>
        </p>
      ) : incident.transportOrderId ? (
        canRedeliver ? (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => void run(() => createIncidentRedelivery(incident.id), 'Herleveringsorder aangemaakt in hetzelfde dossier.')}
          >
            Herlevering aanmaken
          </Button>
        ) : (
          <p className="customer-form-muted">Je hebt geen rechten om orders aan te maken.</p>
        )
      ) : (
        <p className="customer-form-muted">Koppel eerst de originele order om een herlevering aan te maken.</p>
      )}
    </section>
  )
}
