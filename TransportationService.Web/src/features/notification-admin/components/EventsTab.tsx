import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { COMMUNICATION_TYPE_LABELS, type CustomerCommunicationType } from '../../customers/types'
import { listNotificationRules, updateNotificationRule } from '../api/notificationAdminApi'
import { RECIPIENT_TYPE_LABELS, type NotificationRule, type RecipientSpec } from '../types'
import { EventRuleModal } from './EventRuleModal'

/** Ordered as the catalog's own groups appear (Orders, Facturatie, Personeel, Vloot). */
function groupOrder(rules: NotificationRule[]): string[] {
  const seen = new Set<string>()
  const order: string[] = []
  for (const rule of rules) {
    if (!seen.has(rule.group)) {
      seen.add(rule.group)
      order.push(rule.group)
    }
  }
  return order
}

function recipientChipLabel(spec: RecipientSpec): string {
  switch (spec.type) {
    case 'CustomerPrimaryContact':
      return RECIPIENT_TYPE_LABELS.CustomerPrimaryContact
    case 'Driver':
      return RECIPIENT_TYPE_LABELS.Driver
    case 'ExplicitEmail':
      return spec.value ?? RECIPIENT_TYPE_LABELS.ExplicitEmail
    case 'InternalPermission':
      return spec.value ? `Recht: ${spec.value}` : RECIPIENT_TYPE_LABELS.InternalPermission
    case 'InternalRole':
      return spec.value ? `Rol: ${spec.value}` : RECIPIENT_TYPE_LABELS.InternalRole
    case 'CustomerCommunicationRule': {
      const label = spec.value ? COMMUNICATION_TYPE_LABELS[spec.value as CustomerCommunicationType] : null
      return label ? `Communicatie: ${label}` : RECIPIENT_TYPE_LABELS.CustomerCommunicationRule
    }
    default:
      return spec.type
  }
}

interface EventsTabProps {
  canManage: boolean
}

/** "Gebeurtenissen" tab: every catalog event grouped, with inline channel/active toggles and a
 * recipients editor modal. Peppol-pending events show a badge and cannot be toggled yet. */
export function EventsTab({ canManage }: EventsTabProps) {
  const { showSuccess, showError } = useToast()
  const [rules, setRules] = useState<NotificationRule[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState<NotificationRule | null>(null)

  const reload = useCallback(() => {
    listNotificationRules()
      .then((data) => {
        setRules(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De meldingsregels konden niet worden geladen.'))
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  async function toggleField(
    rule: NotificationRule,
    field: 'enabled' | 'inAppEnabled' | 'emailEnabled' | 'requiresReview',
  ) {
    try {
      await updateNotificationRule(rule.eventKey, {
        enabled: field === 'enabled' ? !rule.enabled : rule.enabled,
        inAppEnabled: field === 'inAppEnabled' ? !rule.inAppEnabled : rule.inAppEnabled,
        emailEnabled: field === 'emailEnabled' ? !rule.emailEnabled : rule.emailEnabled,
        allowCustomerOverride: rule.allowCustomerOverride,
        recipients: rule.recipients,
        // The DTO value is the effective (rule ?? catalog) setting; echoing it keeps the
        // behaviour stable when another field is toggled, flipping it changes the review hold.
        requiresReview: field === 'requiresReview' ? !rule.requiresReview : rule.requiresReview,
      })
      showSuccess('Meldingsregel bijgewerkt.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De meldingsregel kon niet worden bijgewerkt.').message)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rules === null) return <p className="placeholder-text">Meldingsregels laden…</p>

  const groups = groupOrder(rules)

  const columns: Column<NotificationRule>[] = [
    {
      key: 'event',
      header: 'Gebeurtenis',
      render: (rule) => (
        <span className="notification-admin-event-label">
          {rule.label}
          {rule.peppolPending && <Badge tone="warning">Nog niet actief</Badge>}
        </span>
      ),
    },
    {
      key: 'channels',
      header: 'Kanalen',
      render: (rule) => (
        <div className="notification-admin-channel-cell">
          <label className="notification-admin-checkbox">
            <input
              type="checkbox"
              aria-label={`In-app voor ${rule.label}`}
              checked={rule.inAppEnabled}
              disabled={!canManage || rule.peppolPending}
              onChange={() => void toggleField(rule, 'inAppEnabled')}
            />
            In-app
          </label>
          <label className="notification-admin-checkbox">
            <input
              type="checkbox"
              aria-label={`E-mail voor ${rule.label}`}
              checked={rule.emailEnabled}
              disabled={!canManage || rule.peppolPending}
              onChange={() => void toggleField(rule, 'emailEnabled')}
            />
            E-mail
          </label>
          <label className="notification-admin-checkbox notification-admin-checkbox-disabled" title="SMS is nog niet beschikbaar">
            <input type="checkbox" checked={false} disabled aria-label={`SMS voor ${rule.label} (binnenkort)`} />
            SMS
          </label>
        </div>
      ),
    },
    {
      key: 'recipients',
      header: 'Ontvangers',
      render: (rule) => (
        <div className="notification-admin-chip-row">
          {rule.recipients.length === 0 && <span className="notification-admin-muted">Geen</span>}
          {rule.recipients.map((r, i) => (
            <Badge key={i} tone="neutral">
              {recipientChipLabel(r)}
            </Badge>
          ))}
        </div>
      ),
    },
    {
      key: 'requiresReview',
      header: 'Controle vóór verzenden',
      render: (rule) => (
        <label className="notification-admin-checkbox" title="Klantmail wordt vastgehouden tot een dispatcher die vrijgeeft (tab 'Wacht op controle').">
          <input
            type="checkbox"
            aria-label={`Controle vóór verzenden: ${rule.label}`}
            checked={rule.requiresReview}
            disabled={!canManage || rule.peppolPending}
            onChange={() => void toggleField(rule, 'requiresReview')}
          />
        </label>
      ),
    },
    {
      key: 'active',
      header: 'Actief',
      render: (rule) => (
        <label className="notification-admin-checkbox">
          <input
            type="checkbox"
            aria-label={`Actief: ${rule.label}`}
            checked={rule.enabled}
            disabled={!canManage || rule.peppolPending}
            onChange={() => void toggleField(rule, 'enabled')}
          />
        </label>
      ),
    },
  ]

  if (canManage) {
    columns.push({
      key: 'edit',
      header: '',
      render: (rule) => (
        <Button variant="ghost" onClick={() => setEditing(rule)}>
          Bewerken
        </Button>
      ),
    })
  }

  return (
    <div>
      {groups.map((group) => (
        <section key={group} className="notification-admin-group">
          <h3>{group}</h3>
          <DataTable columns={columns} rows={rules.filter((r) => r.group === group)} rowKey={(r) => r.eventKey} />
        </section>
      ))}

      {editing && (
        <EventRuleModal
          rule={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            reload()
          }}
        />
      )}
    </div>
  )
}
