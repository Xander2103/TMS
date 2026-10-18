import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, localizeApiError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { formatInteger } from '../../../utils/numbers'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { useDirtyDialogClose } from '../../settings/components/useDirtyDialogClose'
import { getTrailerOptions } from '../../trailers/api/trailersApi'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
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

/** The level a rule is bound to; 'Asset' = the specific vehicle or trailer of the chosen asset type. */
type PolicyLevel = 'CompanyDefault' | 'Category' | 'Asset'

interface PolicyDraft {
  kind: MaintenancePolicyKind
  assetKind: FleetAssetKind
  level: PolicyLevel
  categoryId: string | null
  assetId: string | null
  intervalMonths: string
  intervalKm: string
  warningDays: string
  description: string
  isActive: boolean
}

function draftOf(policy: MaintenancePolicy | null): PolicyDraft {
  const scope = policy?.scope ?? 'CompanyDefault'
  return {
    kind: policy?.kind ?? 'Maintenance',
    assetKind: policy?.assetKind ?? 'Vehicle',
    level: scope === 'Vehicle' || scope === 'Trailer' ? 'Asset' : scope,
    categoryId: policy?.categoryId ?? null,
    assetId: policy?.vehicleId ?? policy?.trailerId ?? null,
    intervalMonths: policy?.intervalMonths?.toString() ?? '',
    intervalKm: policy?.intervalKm?.toString() ?? '',
    warningDays: policy?.warningDays?.toString() ?? '30',
    description: policy?.description ?? '',
    isActive: policy?.isActive ?? true,
  }
}

function sameDraft(a: PolicyDraft, b: PolicyDraft): boolean {
  return (Object.keys(a) as (keyof PolicyDraft)[]).every((key) => a[key] === b[key])
}

function PolicyDialog({ policy, onClose }: { policy: MaintenancePolicy | null; onClose: (saved: boolean) => void }) {
  const { t } = useLocale()
  const vehicleCategories = useLookupOptions('/api/vehicle-categories')
  const trailerCategories = useLookupOptions('/api/trailer-categories')

  const [initial] = useState(() => draftOf(policy))
  const [draft, setDraft] = useState<PolicyDraft>(initial)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)
  // Vehicle/trailer pickers, loaded on demand per asset type (only the "specific asset" level needs them).
  const [assetOptions, setAssetOptions] = useState<{ kind: FleetAssetKind; options: SearchableSelectOption[] } | null>(null)
  const [assetOptionsError, setAssetOptionsError] = useState<string | null>(null)

  const dirty = !sameDraft(draft, initial)
  const { requestClose, guard } = useDirtyDialogClose(dirty, saving, () => onClose(false))

  const { assetKind, level } = draft
  const categories = assetKind === 'Vehicle' ? vehicleCategories.options : trailerCategories.options
  const needsAssetOptions = level === 'Asset' && assetOptions?.kind !== assetKind

  useEffect(() => {
    if (!needsAssetOptions) return
    let mounted = true
    const load = assetKind === 'Vehicle'
      ? getVehicleOptions().then((rows) => rows.map((row) => ({ value: row.id, label: `${row.internalNumber} · ${row.licensePlate}`, keywords: row.licensePlate })))
      : getTrailerOptions().then((rows) => rows.map((row) => ({ value: row.id, label: `${row.internalNumber} · ${row.licensePlate}`, keywords: row.licensePlate })))
    load
      .then((options) => {
        if (!mounted) return
        setAssetOptions({ kind: assetKind, options })
        setAssetOptionsError(null)
      })
      .catch(() => {
        if (mounted) setAssetOptionsError(t('maintenance.policies.assetOptionsFailed'))
      })
    return () => {
      mounted = false
    }
  }, [needsAssetOptions, assetKind, t])

  // The rule's own (possibly deleted) asset is not in the options list; keep it selectable/visible.
  const ownAssetId = policy?.vehicleId ?? policy?.trailerId ?? null
  const loadedAssetOptions = assetOptions?.kind === assetKind ? assetOptions.options : []
  const pickerOptions: SearchableSelectOption[] =
    policy && ownAssetId && policy.assetKind === assetKind && !loadedAssetOptions.some((option) => option.value === ownAssetId)
      ? [...loadedAssetOptions, { value: ownAssetId, label: policyLevelLabel(t, policy) }]
      : loadedAssetOptions

  function patch(next: Partial<PolicyDraft>) {
    setDraft((current) => ({ ...current, ...next }))
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    // Client-side: a chosen level needs its target. Everything else is validated server-side.
    const missing: FieldErrors = {}
    if (level === 'Category' && !draft.categoryId) missing.categoryId = [t('maintenance.policies.categoryRequired')]
    if (level === 'Asset' && !draft.assetId) {
      const field = assetKind === 'Vehicle' ? 'vehicleId' : 'trailerId'
      missing[field] = [t(assetKind === 'Vehicle' ? 'maintenance.policies.vehicleRequired' : 'maintenance.policies.trailerRequired')]
    }
    if (Object.keys(missing).length > 0) {
      setError(null)
      setFieldErrors(missing)
      return
    }

    setSaving(true)
    setError(null)
    setFieldErrors({})
    const input: MaintenancePolicyInput = {
      kind: draft.kind,
      assetKind,
      categoryId: level === 'Category' ? draft.categoryId : null,
      vehicleId: level === 'Asset' && assetKind === 'Vehicle' ? draft.assetId : null,
      trailerId: level === 'Asset' && assetKind === 'Trailer' ? draft.assetId : null,
      intervalMonths: draft.intervalMonths === '' ? null : Number(draft.intervalMonths),
      intervalKm: draft.intervalKm === '' || assetKind !== 'Vehicle' ? null : Number(draft.intervalKm),
      warningDays: Number(draft.warningDays) || 0,
      description: draft.description.trim() || null,
      isActive: draft.isActive,
    }
    try {
      if (policy) {
        await updateMaintenancePolicy(policy.id, input)
      } else {
        await createMaintenancePolicy(input)
      }
      onClose(true)
    } catch (err) {
      // A second ACTIVE rule at the same level is refused with a Dutch 400 ("Er bestaat al een
      // actieve onderhoudsregel voor …") — shown as-is in the summary.
      const described = describeApiError(err, t('maintenance.policies.saveFailed'))
      setError(localizeApiError(t, err, t('maintenance.policies.saveFailed')))
      setFieldErrors(described.fieldErrors)
      setSaving(false)
    }
  }

  const assetLevelLabel = t(assetKind === 'Vehicle' ? 'maintenance.policies.levelOptVehicle' : 'maintenance.policies.levelOptTrailer')
  const assetFieldKey = assetKind === 'Vehicle' ? 'vehicleId' : 'trailerId'

  return (
    <>
      <Modal
        title={policy ? t('maintenance.policies.editTitle') : t('maintenance.policies.newTitle')}
        onClose={requestClose}
        busy={saving}
        footer={
          <>
            <Button variant="secondary" onClick={requestClose} disabled={saving}>
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
            fieldLabels={{
              intervalMonths: t('maintenance.policy.intervalMonths'),
              intervalKm: t('maintenance.policy.intervalKm'),
              warningDays: t('maintenance.policies.warningTermLabel'),
              categoryId: t('maintenance.policies.fieldCategory'),
              vehicleId: t('maintenance.policies.fieldVehicle'),
              trailerId: t('maintenance.policies.fieldTrailer'),
            }}
          />
          <FormField label={t('maintenance.policies.colKind')} htmlFor="mp-kind">
            <select id="mp-kind" value={draft.kind} onChange={(e) => patch({ kind: e.target.value as MaintenancePolicyKind })} disabled={saving}>
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
              // A category or asset of the other type never applies: clear the target, keep the level.
              onChange={(e) => patch({ assetKind: e.target.value as FleetAssetKind, categoryId: null, assetId: null })}
              disabled={saving}
            >
              {Object.entries(ASSET_KIND_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {t(label)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t('maintenance.policies.fieldLevel')} htmlFor="mp-level" hint={t('maintenance.policies.levelHint')}>
            <select
              id="mp-level"
              value={level}
              onChange={(e) => patch({ level: e.target.value as PolicyLevel, categoryId: null, assetId: null })}
              disabled={saving}
            >
              <option value="CompanyDefault">{t('maintenance.policies.levelOptCompany')}</option>
              <option value="Category">{t('maintenance.policies.levelOptCategory')}</option>
              <option value="Asset">{assetLevelLabel}</option>
            </select>
          </FormField>
          {level === 'Category' && (
            <FormField label={t('maintenance.policies.fieldCategory')} htmlFor="mp-category" required error={getFieldError(fieldErrors, 'categoryId')}>
              <select id="mp-category" value={draft.categoryId ?? ''} onChange={(e) => patch({ categoryId: e.target.value || null })} disabled={saving}>
                <option value="">{t('maintenance.policies.categoryPlaceholder')}</option>
                {categories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))}
              </select>
            </FormField>
          )}
          {level === 'Asset' && (
            <FormField
              label={t(assetKind === 'Vehicle' ? 'maintenance.policies.fieldVehicle' : 'maintenance.policies.fieldTrailer')}
              htmlFor="mp-target-asset"
              required
              error={getFieldError(fieldErrors, assetFieldKey)}
            >
              <SearchableSelect
                id="mp-target-asset"
                value={draft.assetId}
                onChange={(value) => patch({ assetId: value })}
                options={pickerOptions}
                isLoading={needsAssetOptions && !assetOptionsError}
                errorMessage={assetOptionsError}
                placeholder={t(assetKind === 'Vehicle' ? 'maintenance.policies.vehiclePlaceholder' : 'maintenance.policies.trailerPlaceholder')}
                disabled={saving}
              />
            </FormField>
          )}
          <FormField label={t('maintenance.policy.intervalMonths')} htmlFor="mp-months" error={getFieldError(fieldErrors, 'intervalMonths')}>
            <input id="mp-months" type="number" min={1} value={draft.intervalMonths} onChange={(e) => patch({ intervalMonths: e.target.value })} disabled={saving} />
          </FormField>
          {assetKind === 'Vehicle' && (
            <FormField label={t('maintenance.policy.intervalKm')} htmlFor="mp-km" error={getFieldError(fieldErrors, 'intervalKm')} hint={t('maintenance.policies.intervalKmOnlyVehicles')}>
              <input id="mp-km" type="number" min={1} value={draft.intervalKm} onChange={(e) => patch({ intervalKm: e.target.value })} disabled={saving} />
            </FormField>
          )}
          <FormField label={t('maintenance.policy.warningDaysBefore')} htmlFor="mp-warning" error={getFieldError(fieldErrors, 'warningDays')}>
            <input id="mp-warning" type="number" min={0} max={365} value={draft.warningDays} onChange={(e) => patch({ warningDays: e.target.value })} disabled={saving} />
          </FormField>
          <FormField label={t('maintenance.policy.description')} htmlFor="mp-description">
            <input id="mp-description" value={draft.description} onChange={(e) => patch({ description: e.target.value })} maxLength={500} disabled={saving} />
          </FormField>
          <FormField label={t('maintenance.policies.colStatus')} htmlFor="mp-active">
            <label className="customer-form-checkbox">
              <input id="mp-active" type="checkbox" checked={draft.isActive} onChange={(e) => patch({ isActive: e.target.checked })} disabled={saving} />
              {t('maintenance.policies.isActive')}
            </label>
          </FormField>
        </form>
      </Modal>
      {guard}
    </>
  )
}
