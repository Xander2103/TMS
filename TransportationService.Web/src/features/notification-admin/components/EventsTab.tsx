import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import type { TranslateFn } from '../../../i18n/localeContext'
import { COMMUNICATION_TYPE_LABEL_KEYS, type CustomerCommunicationType } from '../../customers/types'
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

function recipientChipLabel(t: TranslateFn, spec: RecipientSpec): string {
  switch (spec.type) {
    case 'CustomerPrimaryContact':
      return t(RECIPIENT_TYPE_LABELS.CustomerPrimaryContact)
    case 'Driver':
      return t(RECIPIENT_TYPE_LABELS.Driver)
    case 'ExplicitEmail':
      return spec.value ?? t(RECIPIENT_TYPE_LABELS.ExplicitEmail)
    case 'InternalPermission':
      return spec.value
        ? t('notificationAdmin.events.chipPermission', { value: spec.value })
        : t(RECIPIENT_TYPE_LABELS.InternalPermission)
    case 'InternalRole':
      return spec.value
        ? t('notificationAdmin.events.chipRole', { value: spec.value })
        : t(RECIPIENT_TYPE_LABELS.InternalRole)
    case 'CustomerCommunicationRule': {
      const labelKey = spec.value ? COMMUNICATION_TYPE_LABEL_KEYS[spec.value as CustomerCommunicationType] : null
      return labelKey
        ? t('notificationAdmin.events.chipCommunication', { label: t(labelKey) })
        : t(RECIPIENT_TYPE_LABELS.CustomerCommunicationRule)
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
  const { t } = useLocale()
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
      .catch(() => setLoadError(t('notificationAdmin.events.loadFailed')))
  }, [t])

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
      showSuccess(t('notificationAdmin.events.updated'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('notificationAdmin.events.updateFailed')))
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rules === null) return <p className="placeholder-text">{t('notificationAdmin.events.loading')}</p>

  const groups = groupOrder(rules)

  const columns: Column<NotificationRule>[] = [
    {
      key: 'event',
      header: t('notificationAdmin.events.columns.event'),
      render: (rule) => (
        <span className="notification-admin-event-label">
          {rule.label}
          {rule.peppolPending && <Badge tone="warning">{t('notificationAdmin.events.peppolPending')}</Badge>}
        </span>
      ),
    },
    {
      key: 'channels',
      header: t('notificationAdmin.events.columns.channels'),
      render: (rule) => (
        <div className="notification-admin-channel-cell">
          <label className="notification-admin-checkbox">
            <input
              type="checkbox"
              aria-label={t('notificationAdmin.events.inAppAria', { label: rule.label })}
              checked={rule.inAppEnabled}
              disabled={!canManage || rule.peppolPending}
              onChange={() => void toggleField(rule, 'inAppEnabled')}
            />
            {t('notificationAdmin.events.inApp')}
          </label>
          <label className="notification-admin-checkbox">
            <input
              type="checkbox"
              aria-label={t('notificationAdmin.events.emailAria', { label: rule.label })}
              checked={rule.emailEnabled}
              disabled={!canManage || rule.peppolPending}
              onChange={() => void toggleField(rule, 'emailEnabled')}
            />
            {t('notificationAdmin.events.email')}
          </label>
          <label
            className="notification-admin-checkbox notification-admin-checkbox-disabled"
            title={t('notificationAdmin.events.smsUnavailable')}
          >
            <input
              type="checkbox"
              checked={false}
              disabled
              aria-label={t('notificationAdmin.events.smsAria', { label: rule.label })}
            />
            {t('notificationAdmin.events.sms')}
          </label>
        </div>
      ),
    },
    {
      key: 'recipients',
      header: t('notificationAdmin.events.columns.recipients'),
      render: (rule) => (
        <div className="notification-admin-chip-row">
          {rule.recipients.length === 0 && (
            <span className="notification-admin-muted">{t('notificationAdmin.events.noRecipients')}</span>
          )}
          {rule.recipients.map((r, i) => (
            <Badge key={i} tone="neutral">
              {recipientChipLabel(t, r)}
            </Badge>
          ))}
        </div>
      ),
    },
    {
      key: 'requiresReview',
      header: t('notificationAdmin.events.columns.requiresReview'),
      render: (rule) => (
        <label className="notification-admin-checkbox" title={t('notificationAdmin.events.reviewTitle')}>
          <input
            type="checkbox"
            aria-label={t('notificationAdmin.events.reviewAria', { label: rule.label })}
            checked={rule.requiresReview}
            disabled={!canManage || rule.peppolPending}
            onChange={() => void toggleField(rule, 'requiresReview')}
          />
        </label>
      ),
    },
    {
      key: 'active',
      header: t('notificationAdmin.events.columns.active'),
      render: (rule) => (
        <label className="notification-admin-checkbox">
          <input
            type="checkbox"
            aria-label={t('notificationAdmin.events.activeAria', { label: rule.label })}
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
          {t('ui.actions.edit')}
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
