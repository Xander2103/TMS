import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import {
  CHARGE_POLICY_MODE_KEYS,
  listChargePolicies,
  saveChargePolicies,
  type ChargePolicy,
  type ChargePolicyInput,
  type ChargePolicyMode,
} from '../api/chargePoliciesApi'
import './settings.css'

/** Incidenttypes waarvoor een beleid kan gelden ('' = alle types); labels via keymap. */
const POLICY_INCIDENT_TYPES = [
  'Damage',
  'Delay',
  'Theft',
  'Accident',
  'WrongDelivery',
  'MissingGoods',
  'CustomerComplaint',
  'VehicleBreakdown',
  'Administrative',
  'Other',
] as const

/** Bewerkbare rij; '' bij klant/incidenttype = "alle" (null in de payload). */
interface PolicyRow {
  key: string
  customerId: string
  /** Naam uit de load, als fallback-optie wanneer de klant niet in de eerste pagina zit. */
  customerName: string | null
  incidentType: string
  mode: ChargePolicyMode
  amount: string
  description: string
}

let rowCounter = 0
function newRowKey(): string {
  rowCounter += 1
  return `policy-${rowCounter}`
}

function toRow(policy: ChargePolicy): PolicyRow {
  return {
    key: newRowKey(),
    customerId: policy.customerId ?? '',
    customerName: policy.customerName,
    incidentType: policy.incidentType ?? '',
    mode: policy.mode,
    amount: policy.defaultAmount === null ? '' : String(policy.defaultAmount),
    description: policy.defaultDescription ?? '',
  }
}

/**
 * Parameters → Beheer → Doorrekenbeleid: per klant en/of incidenttype bepalen of een
 * klantdoorrekening nooit, als voorstel of automatisch wordt aangemaakt. PUT vervangt de
 * volledige set; het meest specifieke beleid wint, zonder beleid geldt "voorstellen".
 */
export function ChargePoliciesPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('problems.approve_charge')
  const canView = canManage || hasPermission('incidents.manage')

  const [rows, setRows] = useState<PolicyRow[] | null>(null)
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!canView) return
    let mounted = true
    listChargePolicies()
      .then((data) => {
        if (!mounted) return
        setRows(data.map(toRow))
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('settingsPages.chargePolicies.loadFailed'))
      })
    searchCustomers({ page: 1, pageSize: 200 })
      .then((result) => {
        if (mounted) setCustomers(result.items)
      })
      .catch(() => {
        /* zonder klantenlijst blijft alleen de optie "Alle klanten" beschikbaar */
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canView])

  if (!canView) return <p className="placeholder-text">{t('settingsPages.chargePolicies.noPermission')}</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rows === null) return <p className="placeholder-text">{t('settingsPages.chargePolicies.loading')}</p>

  function patchRow(key: string, patch: Partial<Omit<PolicyRow, 'key'>>) {
    setRows((current) => (current ? current.map((row) => (row.key === key ? { ...row, ...patch } : row)) : current))
  }

  function addRow() {
    setRows((current) => [
      ...(current ?? []),
      { key: newRowKey(), customerId: '', customerName: null, incidentType: '', mode: 'Propose', amount: '', description: '' },
    ])
  }

  async function handleSave() {
    if (!rows) return
    const inputs: ChargePolicyInput[] = []
    for (const row of rows) {
      const amount = row.amount === '' ? null : Number(row.amount)
      if (amount !== null && (!Number.isFinite(amount) || amount < 0)) {
        showError(t('settingsPages.chargePolicies.amountPositive'))
        return
      }
      if (row.mode === 'Auto' && (amount === null || amount <= 0)) {
        showError(t('settingsPages.chargePolicies.autoNeedsAmount'))
        return
      }
      inputs.push({
        customerId: row.customerId || null,
        incidentType: row.incidentType || null,
        mode: row.mode,
        defaultAmount: amount,
        defaultDescription: row.description.trim() || null,
      })
    }
    setBusy(true)
    try {
      await saveChargePolicies(inputs)
      showSuccess(t('settingsPages.chargePolicies.saved'))
      const reloaded = await listChargePolicies()
      setRows(reloaded.map(toRow))
    } catch (err) {
      showError(describeApiError(err, t('settingsPages.chargePolicies.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('navigation.menu.chargePolicies') }]}
      />
      <PageHeader
        title={t('settingsPages.chargePolicies.title')}
        subtitle={t('settingsPages.chargePolicies.subtitle')}
        action={
          canManage && (
            <span className="settings-actions">
              <Button variant="secondary" onClick={addRow} disabled={busy}>
                {t('settingsPages.chargePolicies.add')}
              </Button>
              <Button onClick={() => void handleSave()} disabled={busy}>
                {busy ? t('settingsPages.common.saving') : t('ui.actions.save')}
              </Button>
            </span>
          )
        }
      />

      <p className="customer-form-muted">{t('settingsPages.chargePolicies.explanation')}</p>

      {rows.length === 0 ? (
        <p className="placeholder-text">{t('settingsPages.chargePolicies.empty')}</p>
      ) : (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('settingsPages.chargePolicies.columnCustomer')}</th>
              <th>{t('settingsPages.chargePolicies.columnIncidentType')}</th>
              <th>{t('settingsPages.chargePolicies.columnMode')}</th>
              <th>{t('settingsPages.chargePolicies.columnAmount')}</th>
              <th>{t('settingsPages.chargePolicies.columnDescription')}</th>
              {canManage && <th aria-label={t('settingsPages.common.actions')} />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={row.key}>
                <td>
                  <select
                    aria-label={t('settingsPages.chargePolicies.customerAria', { index: index + 1 })}
                    value={row.customerId}
                    onChange={(e) => patchRow(row.key, { customerId: e.target.value })}
                    disabled={!canManage}
                  >
                    <option value="">{t('settingsPages.chargePolicies.allCustomers')}</option>
                    {customers.map((customer) => (
                      <option key={customer.id} value={customer.id}>
                        {customer.name}
                      </option>
                    ))}
                    {row.customerId && !customers.some((customer) => customer.id === row.customerId) && (
                      <option value={row.customerId}>
                        {row.customerName ?? t('settingsPages.chargePolicies.unknownCustomer')}
                      </option>
                    )}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.chargePolicies.incidentTypeAria', { index: index + 1 })}
                    value={row.incidentType}
                    onChange={(e) => patchRow(row.key, { incidentType: e.target.value })}
                    disabled={!canManage}
                  >
                    <option value="">{t('settingsPages.chargePolicies.allTypes')}</option>
                    {POLICY_INCIDENT_TYPES.map((type) => (
                      <option key={type} value={type}>
                        {t(`settingsPages.chargePolicies.incidentType.${type}`)}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.chargePolicies.modeAria', { index: index + 1 })}
                    value={row.mode}
                    onChange={(e) => patchRow(row.key, { mode: e.target.value as ChargePolicyMode })}
                    disabled={!canManage}
                  >
                    {(Object.entries(CHARGE_POLICY_MODE_KEYS) as [ChargePolicyMode, string][]).map(
                      ([value, labelKey]) => (
                        <option key={value} value={value}>
                          {t(labelKey)}
                        </option>
                      ),
                    )}
                  </select>
                </td>
                <td>
                  <input
                    type="number"
                    min={0.01}
                    step="0.01"
                    className="settings-doc-rules-priority"
                    aria-label={t('settingsPages.chargePolicies.amountAria', { index: index + 1 })}
                    value={row.amount}
                    onChange={(e) => patchRow(row.key, { amount: e.target.value })}
                    disabled={!canManage}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    maxLength={500}
                    aria-label={t('settingsPages.chargePolicies.descriptionAria', { index: index + 1 })}
                    value={row.description}
                    onChange={(e) => patchRow(row.key, { description: e.target.value })}
                    disabled={!canManage}
                  />
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setRows((current) => (current ? current.filter((r) => r.key !== row.key) : current))}
                    >
                      {t('ui.actions.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
