import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import {
  createMaintenancePolicy,
  deleteMaintenancePolicy,
  listMaintenancePolicies,
  updateMaintenancePolicy,
} from '../api/maintenancePoliciesApi'
import {
  ASSET_KIND_LABELS,
  POLICY_KIND_LABELS,
  policyLevelLabel,
  type FleetAssetKind,
  type MaintenancePolicy,
  type MaintenancePolicyInput,
  type MaintenancePolicyKind,
} from '../types'

interface DialogState {
  policy: MaintenancePolicy | null
}

/**
 * Configurable default maintenance/inspection intervals. Resolution order (documented and
 * enforced server-side): asset override → category rule → company default.
 */
export function MaintenancePoliciesPage() {
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('maintenance_policies.manage')

  const [policies, setPolicies] = useState<MaintenancePolicy[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [dialog, setDialog] = useState<DialogState | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<MaintenancePolicy | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listMaintenancePolicies()
      .then((data) => {
        setPolicies(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError('Onderhoudsbeleid kon niet worden geladen.')
        setLoaded(true)
      })
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<MaintenancePolicy>[] = [
    { key: 'kind', header: 'Soort', render: (row) => POLICY_KIND_LABELS[row.kind] },
    { key: 'asset', header: 'Activatype', render: (row) => ASSET_KIND_LABELS[row.assetKind] },
    { key: 'level', header: 'Geldt voor', render: (row) => policyLevelLabel(row) },
    {
      key: 'interval',
      header: 'Interval',
      render: (row) =>
        [
          row.intervalMonths !== null ? `${row.intervalMonths} maanden` : null,
          row.intervalKm !== null ? `${row.intervalKm.toLocaleString('nl-BE')} km` : null,
        ]
          .filter(Boolean)
          .join(' of '),
    },
    { key: 'warning', header: 'Waarschuwing', render: (row) => `${row.warningDays} dagen vooraf` },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: 'Acties',
            render: (row: MaintenancePolicy) => (
              <span className="customer-locations-actions">
                <Button variant="ghost" onClick={() => setDialog({ policy: row })}>
                  Bewerken
                </Button>
                <Button variant="ghost" onClick={() => setConfirmDelete(row)}>
                  Verwijderen
                </Button>
              </span>
            ),
          },
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Vloot', to: '/fleet' }, { label: 'Onderhoudsbeleid' }]} />
      <PageHeader
        title="Onderhoudsbeleid"
        subtitle="Standaardintervallen voor onderhoud en keuringen. Volgorde: specifiek voertuig/oplegger → categorie → bedrijfsstandaard."
        action={canManage && <Button onClick={() => setDialog({ policy: null })}>Nieuwe regel</Button>}
      />

      <DataTable
        columns={columns}
        rows={policies}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen regels — voertuigen en opleggers krijgen dan geen automatisch geplande onderhoudsbeurten."
        loadingMessage="Beleid laden..."
      />

      {dialog && (
        <PolicyDialog
          policy={dialog.policy}
          onClose={(saved) => {
            setDialog(null)
            if (saved) {
              toast.showSuccess(dialog.policy ? 'Regel bijgewerkt.' : 'Regel toegevoegd.')
              reload()
            }
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Regel verwijderen"
          message={`Deze ${POLICY_KIND_LABELS[confirmDelete.kind].toLowerCase()}sregel (${policyLevelLabel(confirmDelete)}) verwijderen? Al ingeplande beurten blijven bestaan.`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteMaintenancePolicy(confirmDelete.id)
              toast.showSuccess('Regel verwijderd.')
              setConfirmDelete(null)
              reload()
            } catch (err) {
              toast.showError(describeApiError(err, 'Regel kon niet worden verwijderd.').message)
            } finally {
              setBusy(false)
            }
          }}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </div>
  )
}

function PolicyDialog({ policy, onClose }: { policy: MaintenancePolicy | null; onClose: (saved: boolean) => void }) {
  const vehicleCategories = useLookupOptions('/api/vehicle-categories')
  const trailerCategories = useLookupOptions('/api/trailer-categories')

  const [kind, setKind] = useState<MaintenancePolicyKind>(policy?.kind ?? 'Maintenance')
  const [assetKind, setAssetKind] = useState<FleetAssetKind>(policy?.assetKind ?? 'Vehicle')
  const [categoryId, setCategoryId] = useState<string | null>(policy?.categoryId ?? null)
  const [intervalMonths, setIntervalMonths] = useState(policy?.intervalMonths?.toString() ?? '')
  const [intervalKm, setIntervalKm] = useState(policy?.intervalKm?.toString() ?? '')
  const [warningDays, setWarningDays] = useState(policy?.warningDays?.toString() ?? '30')
  const [description, setDescription] = useState(policy?.description ?? '')
  const [isActive, setIsActive] = useState(policy?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)

  const categories = assetKind === 'Vehicle' ? vehicleCategories.options : trailerCategories.options

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setFieldErrors({})
    const input: MaintenancePolicyInput = {
      kind,
      assetKind,
      categoryId: categoryId || null,
      // Asset-specific overrides are managed from the asset detail pages later; this screen
      // covers category rules and the company default.
      vehicleId: policy?.vehicleId ?? null,
      trailerId: policy?.trailerId ?? null,
      intervalMonths: intervalMonths === '' ? null : Number(intervalMonths),
      intervalKm: intervalKm === '' || assetKind !== 'Vehicle' ? null : Number(intervalKm),
      warningDays: Number(warningDays) || 0,
      description: description.trim() || null,
      isActive,
    }
    try {
      if (policy) {
        await updateMaintenancePolicy(policy.id, input)
      } else {
        await createMaintenancePolicy(input)
      }
      onClose(true)
    } catch (err) {
      const described = describeApiError(err, 'Regel kon niet worden opgeslagen.')
      setError(described.message)
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={policy ? 'Regel bewerken' : 'Nieuwe regel'}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="policy-form" disabled={saving}>
            {saving ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="policy-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary
          message={error}
          fieldErrors={fieldErrors}
          fieldLabels={{ intervalMonths: 'Interval (maanden)', intervalKm: 'Interval (km)', warningDays: 'Waarschuwingstermijn' }}
        />
        <FormField label="Soort" htmlFor="mp-kind">
          <select id="mp-kind" value={kind} onChange={(e) => setKind(e.target.value as MaintenancePolicyKind)} disabled={saving}>
            {Object.entries(POLICY_KIND_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Activatype" htmlFor="mp-asset">
          <select
            id="mp-asset"
            value={assetKind}
            onChange={(e) => {
              setAssetKind(e.target.value as FleetAssetKind)
              setCategoryId(null)
            }}
            disabled={saving}
          >
            {Object.entries(ASSET_KIND_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Categorie" htmlFor="mp-category" hint="Leeg = bedrijfsstandaard voor dit activatype.">
          <select id="mp-category" value={categoryId ?? ''} onChange={(e) => setCategoryId(e.target.value || null)} disabled={saving}>
            <option value="">— Bedrijfsstandaard —</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Interval (maanden)" htmlFor="mp-months" error={getFieldError(fieldErrors, 'intervalMonths')}>
          <input id="mp-months" type="number" min={1} value={intervalMonths} onChange={(e) => setIntervalMonths(e.target.value)} disabled={saving} />
        </FormField>
        {assetKind === 'Vehicle' && (
          <FormField label="Interval (km)" htmlFor="mp-km" error={getFieldError(fieldErrors, 'intervalKm')} hint="Alleen voor voertuigen.">
            <input id="mp-km" type="number" min={1} value={intervalKm} onChange={(e) => setIntervalKm(e.target.value)} disabled={saving} />
          </FormField>
        )}
        <FormField label="Waarschuwing (dagen vooraf)" htmlFor="mp-warning" error={getFieldError(fieldErrors, 'warningDays')}>
          <input id="mp-warning" type="number" min={0} max={365} value={warningDays} onChange={(e) => setWarningDays(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label="Omschrijving" htmlFor="mp-description">
          <input id="mp-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} disabled={saving} />
        </FormField>
        <FormField label="Status" htmlFor="mp-active">
          <label className="customer-form-checkbox">
            <input id="mp-active" type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={saving} />
            Regel is actief
          </label>
        </FormField>
      </form>
    </Modal>
  )
}
