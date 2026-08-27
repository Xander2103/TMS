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
import { describeApiError, getFieldError, localizeApiError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
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
  const { t } = useLocale()
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
        setError(t('maintenance.policy.loadFailed'))
        setLoaded(true)
      })
  }, [t])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<MaintenancePolicy>[] = [
    { key: 'kind', header: t('maintenance.policies.colKind'), render: (row) => t(POLICY_KIND_LABELS[row.kind]) },
    { key: 'asset', header: t('maintenance.policies.colAssetKind'), render: (row) => t(ASSET_KIND_LABELS[row.assetKind]) },
    { key: 'level', header: t('maintenance.policies.colAppliesTo'), render: (row) => policyLevelLabel(t, row) },
    {
      key: 'interval',
      header: t('maintenance.policies.colInterval'),
      render: (row) =>
        [
          row.intervalMonths !== null ? t('maintenance.policies.months', { months: row.intervalMonths }) : null,
          row.intervalKm !== null ? t('maintenance.policies.km', { km: formatInteger(row.intervalKm) }) : null,
        ]
          .filter(Boolean)
          .join(` ${t('maintenance.policy.or')} `),
    },
    { key: 'warning', header: t('maintenance.policies.colWarning'), render: (row) => t('maintenance.policies.warningDaysAfter', { days: row.warningDays }) },
    {
      key: 'status',
      header: t('maintenance.policies.colStatus'),
      render: (row) => (row.isActive ? <Badge tone="success">{t('ui.statusBadges.active')}</Badge> : <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: t('fleet.common.actions'),
            render: (row: MaintenancePolicy) => (
              <span className="customer-locations-actions">
                <Button variant="ghost" onClick={() => setDialog({ policy: row })}>
                  {t('ui.actions.edit')}
                </Button>
                <Button variant="ghost" onClick={() => setConfirmDelete(row)}>
                  {t('ui.actions.delete')}
                </Button>
              </span>
            ),
          },
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.vloot'), to: '/fleet' }, { label: t('maintenance.policies.breadcrumb') }]} />
      <PageHeader
        title={t('maintenance.policies.title')}
        subtitle={t('maintenance.policies.subtitle')}
        action={canManage && <Button onClick={() => setDialog({ policy: null })}>{t('maintenance.policies.newRule')}</Button>}
      />

      <DataTable
        columns={columns}
        rows={policies}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('maintenance.policies.empty')}
        loadingMessage={t('maintenance.policies.loading')}
      />

      {dialog && (
        <PolicyDialog
          policy={dialog.policy}
          onClose={(saved) => {
            setDialog(null)
            if (saved) {
              toast.showSuccess(dialog.policy ? t('maintenance.policies.updatedToast') : t('maintenance.policies.createdToast'))
              reload()
            }
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('maintenance.policies.deleteTitle')}
          message={t(`maintenance.policies.deleteMessage.${confirmDelete.kind}`, { level: policyLevelLabel(t, confirmDelete) })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await deleteMaintenancePolicy(confirmDelete.id)
              toast.showSuccess(t('maintenance.policies.deleted'))
              setConfirmDelete(null)
              reload()
            } catch (err) {
              toast.showError(localizeApiError(t, err, t('maintenance.policies.deleteFailed')))
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
  const { t } = useLocale()
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
      const described = describeApiError(err, t('maintenance.policies.saveFailed'))
      setError(localizeApiError(t, err, t('maintenance.policies.saveFailed')))
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={policy ? t('maintenance.policies.editTitle') : t('maintenance.policies.newTitle')}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="policy-form" disabled={saving}>
            {saving ? t('fleet.common.saving') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="policy-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary
          message={error}
          fieldErrors={fieldErrors}
          fieldLabels={{ intervalMonths: t('maintenance.policy.intervalMonths'), intervalKm: t('maintenance.policy.intervalKm'), warningDays: t('maintenance.policies.warningTermLabel') }}
        />
        <FormField label={t('maintenance.policies.colKind')} htmlFor="mp-kind">
          <select id="mp-kind" value={kind} onChange={(e) => setKind(e.target.value as MaintenancePolicyKind)} disabled={saving}>
            {Object.entries(POLICY_KIND_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {t(label)}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('maintenance.policies.colAssetKind')} htmlFor="mp-asset">
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
                {t(label)}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('maintenance.policies.fieldCategory')} htmlFor="mp-category" hint={t('maintenance.policies.categoryHint')}>
          <select id="mp-category" value={categoryId ?? ''} onChange={(e) => setCategoryId(e.target.value || null)} disabled={saving}>
            <option value="">{t('maintenance.policies.companyDefaultOption')}</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('maintenance.policy.intervalMonths')} htmlFor="mp-months" error={getFieldError(fieldErrors, 'intervalMonths')}>
          <input id="mp-months" type="number" min={1} value={intervalMonths} onChange={(e) => setIntervalMonths(e.target.value)} disabled={saving} />
        </FormField>
        {assetKind === 'Vehicle' && (
          <FormField label={t('maintenance.policy.intervalKm')} htmlFor="mp-km" error={getFieldError(fieldErrors, 'intervalKm')} hint={t('maintenance.policies.intervalKmOnlyVehicles')}>
            <input id="mp-km" type="number" min={1} value={intervalKm} onChange={(e) => setIntervalKm(e.target.value)} disabled={saving} />
          </FormField>
        )}
        <FormField label={t('maintenance.policy.warningDaysBefore')} htmlFor="mp-warning" error={getFieldError(fieldErrors, 'warningDays')}>
          <input id="mp-warning" type="number" min={0} max={365} value={warningDays} onChange={(e) => setWarningDays(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label={t('maintenance.policy.description')} htmlFor="mp-description">
          <input id="mp-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} disabled={saving} />
        </FormField>
        <FormField label={t('maintenance.policies.colStatus')} htmlFor="mp-active">
          <label className="customer-form-checkbox">
            <input id="mp-active" type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={saving} />
            {t('maintenance.policies.isActive')}
          </label>
        </FormField>
      </form>
    </Modal>
  )
}
