import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../../customers/api/customersApi'
import { listCustomerNotificationOverrides, setCustomerNotificationOverride } from '../api/notificationAdminApi'
import type { CustomerNotificationOverride } from '../types'

type OverrideChoice = 'inherit' | 'on' | 'off'

function choiceOf(enabled: boolean | null): OverrideChoice {
  if (enabled === null) return 'inherit'
  return enabled ? 'on' : 'off'
}

interface CustomerOverridesTabProps {
  canManage: boolean
}

/** "Klantafwijkingen" tab: per customer, the events the tenant rule opened up for override, each
 * shown with its effective state and an inherit/aan/uit control. */
export function CustomerOverridesTab({ canManage }: CustomerOverridesTabProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [rows, setRows] = useState<CustomerNotificationOverride[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    searchCustomers({ page: 1, pageSize: 500 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: `${c.customerNumber} — ${c.name}` }))))
      .catch(() => setCustomerOptions([]))
  }, [])

  useEffect(() => {
    if (!customerId) return
    let isMounted = true
    listCustomerNotificationOverrides(customerId)
      .then((data) => {
        if (!isMounted) return
        setRows(data)
        setLoadError(null)
      })
      .catch(() => {
        if (isMounted) setLoadError(t('notificationAdmin.overrides.loadFailed'))
      })
    return () => {
      isMounted = false
    }
  }, [customerId, reloadToken])

  function reload() {
    setReloadToken((t) => t + 1)
  }

  async function setChoice(row: CustomerNotificationOverride, choice: OverrideChoice) {
    if (!customerId) return
    const enabled = choice === 'inherit' ? null : choice === 'on'
    try {
      await setCustomerNotificationOverride(customerId, row.eventKey, enabled)
      showSuccess(t('notificationAdmin.overrides.updated', { label: row.label }))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('notificationAdmin.overrides.saveFailed')))
    }
  }

  const columns: Column<CustomerNotificationOverride>[] = [
    { key: 'label', header: t('notificationAdmin.overrides.columns.event'), render: (r) => r.label },
    {
      key: 'state',
      header: t('notificationAdmin.overrides.columns.status'),
      render: (r) =>
        r.enabled === null ? (
          <Badge tone="neutral">{t('notificationAdmin.overrides.inherited')}</Badge>
        ) : (
          <Badge tone={r.enabled ? 'success' : 'warning'}>
            {r.enabled ? t('notificationAdmin.overrides.overriddenOn') : t('notificationAdmin.overrides.overriddenOff')}
          </Badge>
        ),
    },
    {
      key: 'control',
      header: t('notificationAdmin.overrides.columns.control'),
      render: (r) =>
        canManage ? (
          <div
            className="notification-admin-segmented"
            role="group"
            aria-label={t('notificationAdmin.overrides.groupAria', { label: r.label })}
          >
            {(['inherit', 'on', 'off'] as OverrideChoice[]).map((choice) => (
              <button
                key={choice}
                type="button"
                className={choiceOf(r.enabled) === choice ? 'notification-admin-segment is-active' : 'notification-admin-segment'}
                onClick={() => void setChoice(r, choice)}
              >
                {choice === 'inherit'
                  ? t('notificationAdmin.overrides.inherit')
                  : choice === 'on'
                    ? t('notificationAdmin.overrides.on')
                    : t('notificationAdmin.overrides.off')}
              </button>
            ))}
          </div>
        ) : (
          '—'
        ),
    },
  ]

  return (
    <div>
      <div className="notification-admin-customer-picker">
        <SearchableSelect
          ariaLabel={t('notificationAdmin.overrides.customerAria')}
          value={customerId}
          onChange={setCustomerId}
          options={customerOptions}
          placeholder={t('notificationAdmin.overrides.customerPlaceholder')}
        />
      </div>

      {!customerId && <p className="placeholder-text">{t('notificationAdmin.overrides.chooseCustomer')}</p>}
      {customerId && loadError && <p className="placeholder-text">{loadError}</p>}
      {customerId && !loadError && rows === null && <p className="placeholder-text">{t('notificationAdmin.overrides.loading')}</p>}
      {customerId && !loadError && rows !== null && rows.length === 0 && (
        <p className="placeholder-text">{t('notificationAdmin.overrides.empty')}</p>
      )}
      {customerId && !loadError && rows !== null && rows.length > 0 && (
        <DataTable columns={columns} rows={rows} rowKey={(r) => r.eventKey} />
      )}
    </div>
  )
}
