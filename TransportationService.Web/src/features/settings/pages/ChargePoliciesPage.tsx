import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import {
  CHARGE_POLICY_MODE_LABELS,
  listChargePolicies,
  saveChargePolicies,
  type ChargePolicy,
  type ChargePolicyInput,
  type ChargePolicyMode,
} from '../api/chargePoliciesApi'
import './settings.css'

/** Incidenttypes waarvoor een beleid kan gelden; '' = alle types. */
const POLICY_INCIDENT_TYPES: { value: string; label: string }[] = [
  { value: 'Damage', label: 'Schade' },
  { value: 'Delay', label: 'Vertraging' },
  { value: 'Theft', label: 'Diefstal' },
  { value: 'Accident', label: 'Ongeval' },
  { value: 'WrongDelivery', label: 'Foutieve levering' },
  { value: 'MissingGoods', label: 'Ontbrekende goederen' },
  { value: 'CustomerComplaint', label: 'Klantklacht' },
  { value: 'VehicleBreakdown', label: 'Pech' },
  { value: 'Administrative', label: 'Administratief' },
  { value: 'Other', label: 'Overig' },
]

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
        if (mounted) setLoadError('Het doorrekenbeleid kon niet worden geladen.')
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
  }, [canView])

  if (!canView) return <p className="placeholder-text">Je hebt geen rechten om het doorrekenbeleid te bekijken.</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rows === null) return <p className="placeholder-text">Doorrekenbeleid laden…</p>

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
        showError('Het standaardbedrag moet een positief getal zijn.')
        return
      }
      if (row.mode === 'Auto' && (amount === null || amount <= 0)) {
        showError('Automatisch doorrekenen vereist een positief standaardbedrag.')
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
      showSuccess('Doorrekenbeleid opgeslagen.')
      const reloaded = await listChargePolicies()
      setRows(reloaded.map(toRow))
    } catch (err) {
      showError(describeApiError(err, 'Het doorrekenbeleid kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Doorrekenbeleid' }]} />
      <PageHeader
        title="Doorrekenbeleid"
        subtitle="Of incidentkosten aan de klant worden doorgerekend: nooit, als voorstel of automatisch."
        action={
          canManage && (
            <span className="settings-actions">
              <Button variant="secondary" onClick={addRow} disabled={busy}>
                Beleid toevoegen
              </Button>
              <Button onClick={() => void handleSave()} disabled={busy}>
                {busy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </span>
          )
        }
      />

      <p className="customer-form-muted">
        Doorrekenen kan enkel bij verantwoordelijkheid &lsquo;Klant&rsquo;. Het meest specifieke beleid wint
        (klant + type &gt; klant &gt; type &gt; algemeen). Zonder beleid geldt: voorstellen ter goedkeuring.
      </p>

      {rows.length === 0 ? (
        <p className="placeholder-text">Nog geen beleid — doorrekeningen worden ter goedkeuring voorgesteld.</p>
      ) : (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Klant</th>
              <th>Incidenttype</th>
              <th>Modus</th>
              <th>Standaardbedrag</th>
              <th>Omschrijving</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={row.key}>
                <td>
                  <select
                    aria-label={`Klant beleid ${index + 1}`}
                    value={row.customerId}
                    onChange={(e) => patchRow(row.key, { customerId: e.target.value })}
                    disabled={!canManage}
                  >
                    <option value="">Alle klanten</option>
                    {customers.map((customer) => (
                      <option key={customer.id} value={customer.id}>
                        {customer.name}
                      </option>
                    ))}
                    {row.customerId && !customers.some((customer) => customer.id === row.customerId) && (
                      <option value={row.customerId}>{row.customerName ?? 'Onbekende klant'}</option>
                    )}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={`Incidenttype beleid ${index + 1}`}
                    value={row.incidentType}
                    onChange={(e) => patchRow(row.key, { incidentType: e.target.value })}
                    disabled={!canManage}
                  >
                    <option value="">Alle</option>
                    {POLICY_INCIDENT_TYPES.map((type) => (
                      <option key={type.value} value={type.value}>
                        {type.label}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={`Modus beleid ${index + 1}`}
                    value={row.mode}
                    onChange={(e) => patchRow(row.key, { mode: e.target.value as ChargePolicyMode })}
                    disabled={!canManage}
                  >
                    {(Object.entries(CHARGE_POLICY_MODE_LABELS) as [ChargePolicyMode, string][]).map(
                      ([value, label]) => (
                        <option key={value} value={value}>
                          {label}
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
                    aria-label={`Standaardbedrag beleid ${index + 1}`}
                    value={row.amount}
                    onChange={(e) => patchRow(row.key, { amount: e.target.value })}
                    disabled={!canManage}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    maxLength={500}
                    aria-label={`Omschrijving beleid ${index + 1}`}
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
                      Verwijderen
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
