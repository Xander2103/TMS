import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { PageHeader } from '../../../components/layout/PageHeader'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  createActivityType,
  listActivityTypes,
  removeActivityType,
  updateActivityType,
  type ActivityType,
  type ActivityTypeInput,
} from '../api/activityTypesApi'
import { ACTIVITY_TYPE_ICONS, activityTypeIcon } from '../activityTypeIcons'
import './activityTypes.css'

interface Draft {
  type: ActivityType | null
  code: string
  name: string
  icon: string
  kpiCategory: string
  sortOrder: string
  isActive: boolean
  hasStops: boolean
  supportsGoods: boolean
  planningRelevant: boolean
  warehouseRelevant: boolean
  allowsDuration: boolean
  isQuickStart: boolean
  quickStartOrder: string
  isSystemDefaultTransport: boolean
}

function emptyDraft(): Draft {
  return {
    type: null,
    code: '',
    name: '',
    icon: '',
    kpiCategory: '',
    sortOrder: '0',
    isActive: true,
    hasStops: false,
    supportsGoods: false,
    planningRelevant: false,
    warehouseRelevant: false,
    allowsDuration: false,
    isQuickStart: false,
    quickStartOrder: '0',
    isSystemDefaultTransport: false,
  }
}

function draftOf(type: ActivityType): Draft {
  return {
    type,
    code: type.code,
    name: type.name,
    icon: type.icon ?? '',
    kpiCategory: type.kpiCategory ?? '',
    sortOrder: String(type.sortOrder),
    isActive: type.isActive,
    hasStops: type.hasStops,
    supportsGoods: type.supportsGoods,
    planningRelevant: type.planningRelevant,
    warehouseRelevant: type.warehouseRelevant,
    allowsDuration: type.allowsDuration,
    isQuickStart: type.isQuickStart,
    quickStartOrder: String(type.quickStartOrder),
    isSystemDefaultTransport: type.isSystemDefaultTransport,
  }
}

/**
 * Parameters → Stamgegevens → Activiteitstypes: the tenant-managed catalogue of operational
 * activity kinds (Wave 1 §5). Capability flags drive all behaviour — domain logic never
 * matches on the code — so a tenant reshapes its activity model here without source changes.
 */
export function ActivityTypesPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('activity_types.manage')
  const canView = hasPermission('activity_types.view') || hasPermission('dossiers.view') || canManage

  const [types, setTypes] = useState<ActivityType[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ActivityType | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    listActivityTypes(true)
      .then((data) => {
        setTypes(data)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('dossiers.activityTypes.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">{t('dossiers.activityTypes.noPermission')}</p>

  const patch = (changes: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...changes } : d))

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    setBusy(true)
    setDraftError(null)
    try {
      const input: ActivityTypeInput = {
        code: draft.code.trim(),
        name: draft.name.trim(),
        icon: draft.icon || null,
        kpiCategory: draft.kpiCategory.trim() || null,
        sortOrder: Number(draft.sortOrder) || 0,
        isActive: draft.isActive,
        hasStops: draft.hasStops,
        supportsGoods: draft.supportsGoods,
        planningRelevant: draft.planningRelevant,
        warehouseRelevant: draft.warehouseRelevant,
        allowsDuration: draft.allowsDuration,
        isQuickStart: draft.isQuickStart,
        quickStartOrder: Number(draft.quickStartOrder) || 0,
        isSystemDefaultTransport: draft.isSystemDefaultTransport,
      }
      if (draft.type) {
        await updateActivityType(draft.type.id, input)
        showSuccess(t('dossiers.activityTypes.updated'))
      } else {
        await createActivityType(input)
        showSuccess(t('dossiers.activityTypes.added'))
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, t('dossiers.activityTypes.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<ActivityType>[] = [
    {
      key: 'name',
      header: t('dossiers.activityTypes.columns.name'),
      render: (type) => {
        const Icon = activityTypeIcon(type.icon)
        return (
          <span className="activity-type-name">
            <Icon size={18} aria-hidden />
            <span>
              {type.name}
              {type.isSystemDefaultTransport && (
                <span className="activity-type-default"> <Badge tone="info">{t('dossiers.activityTypes.defaultTransport')}</Badge></span>
              )}
            </span>
          </span>
        )
      },
    },
    { key: 'code', header: t('dossiers.activityTypes.columns.code'), render: (type) => <code className="activity-type-code">{type.code}</code> },
    {
      key: 'capabilities',
      header: t('dossiers.activityTypes.columns.capabilities'),
      render: (type) => (
        <span className="activity-type-caps">
          {type.hasStops && <Badge tone="neutral">{t('dossiers.activityTypes.capTransportOrder')}</Badge>}
          {type.supportsGoods && <Badge tone="neutral">{t('dossiers.activityTypes.capGoods')}</Badge>}
          {type.planningRelevant && <Badge tone="neutral">{t('dossiers.activityTypes.capPlanning')}</Badge>}
          {type.warehouseRelevant && <Badge tone="neutral">{t('dossiers.activityTypes.capWarehouse')}</Badge>}
          {type.allowsDuration && <Badge tone="neutral">{t('dossiers.activityTypes.capDuration')}</Badge>}
        </span>
      ),
    },
    {
      key: 'quickstart',
      header: t('dossiers.activityTypes.columns.quickStart'),
      render: (type) =>
        type.isQuickStart ? <Badge tone="success">{t('dossiers.activityTypes.tile', { order: type.quickStartOrder })}</Badge> : '—',
    },
    {
      key: 'active',
      header: t('dossiers.activityTypes.columns.active'),
      render: (type) => (
        <Badge tone={type.isActive ? 'success' : 'neutral'}>
          {type.isActive ? t('ui.statusBadges.active') : t('ui.statusBadges.inactive')}
        </Badge>
      ),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (type) => (
              <span className="issued-items-row-actions">
                <button
                  type="button"
                  className="issued-items-link"
                  onClick={() => {
                    setDraftError(null)
                    setDraft(draftOf(type))
                  }}
                >
                  {t('ui.actions.edit')}
                </button>
                <button
                  type="button"
                  className="issued-items-link issued-items-link-danger"
                  onClick={() => setDeleteTarget(type)}
                >
                  {t('ui.actions.delete')}
                </button>
              </span>
            ),
          } satisfies Column<ActivityType>,
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('navigation.menu.activityTypes') }]} />
      <PageHeader
        title={t('navigation.menu.activityTypes')}
        subtitle={t('dossiers.activityTypes.subtitle')}
        action={
          canManage ? (
            <Button
              onClick={() => {
                setDraftError(null)
                setDraft(emptyDraft())
              }}
            >
              {t('dossiers.activityTypes.new')}
            </Button>
          ) : undefined
        }
      />

      <DataTable
        columns={columns}
        rows={types ?? []}
        rowKey={(type) => type.id}
        isLoading={types === null && loadError === null}
        error={loadError}
        emptyMessage={t('dossiers.activityTypes.empty')}
        rowClassName={(type) => (type.isActive ? undefined : 'activity-type-row-inactive')}
      />

      {draft && (
        <Modal
          title={
            draft.type
              ? t('dossiers.activityTypes.editTitle', { name: draft.type.name })
              : t('dossiers.activityTypes.new')
          }
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="activity-type-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="activity-type-form" onSubmit={submit} noValidate>
            {draftError && (
              <div role="alert" className="issued-items-form-error">
                {draftError}
              </div>
            )}
            <FormField
              label={t('dossiers.activityTypes.codeField')}
              htmlFor="at-code"
              required
              hint={draft.type ? t('dossiers.activityTypes.codeLockedHint') : t('dossiers.activityTypes.codeNewHint')}
            >
              <input
                id="at-code"
                value={draft.code}
                maxLength={50}
                disabled={draft.type !== null}
                onChange={(e) => patch({ code: e.target.value.toUpperCase() })}
              />
            </FormField>
            <FormField label={t('dossiers.activityTypes.nameField')} htmlFor="at-name" required hint={t('dossiers.activityTypes.nameHint')}>
              <input id="at-name" value={draft.name} maxLength={100} onChange={(e) => patch({ name: e.target.value })} />
            </FormField>
            <FormField label={t('dossiers.activityTypes.iconField')} htmlFor="at-icon">
              <select id="at-icon" value={draft.icon} onChange={(e) => patch({ icon: e.target.value })}>
                <option value="">{t('dossiers.activityTypes.iconDefault')}</option>
                {Object.entries(ACTIVITY_TYPE_ICONS).map(([key, { labelKey }]) => (
                  <option key={key} value={key}>
                    {t(labelKey)}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField
              label={t('dossiers.activityTypes.kpiField')}
              htmlFor="at-kpi"
              hint={t('dossiers.activityTypes.kpiHint')}
            >
              <input
                id="at-kpi"
                value={draft.kpiCategory}
                maxLength={50}
                onChange={(e) => patch({ kpiCategory: e.target.value })}
              />
            </FormField>
            <FormField label={t('dossiers.activityTypes.sortField')} htmlFor="at-sort">
              <input
                id="at-sort"
                type="number"
                value={draft.sortOrder}
                onChange={(e) => patch({ sortOrder: e.target.value })}
              />
            </FormField>

            <fieldset className="activity-type-flags">
              <legend>{t('dossiers.activityTypes.flagsLegend')}</legend>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.hasStops} onChange={(e) => patch({ hasStops: e.target.checked })} />
                <span>
                  {t('dossiers.activityTypes.capTransportOrder')}
                  <span className="activity-type-flag-hint">
                    {t('dossiers.activityTypes.flagTransportHint')}
                  </span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.supportsGoods}
                  onChange={(e) => patch({ supportsGoods: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.capGoods')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagGoodsHint')}</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.planningRelevant}
                  onChange={(e) => patch({ planningRelevant: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagPlanning')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagPlanningHint')}</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.warehouseRelevant}
                  onChange={(e) => patch({ warehouseRelevant: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagWarehouse')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagWarehouseHint')}</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.allowsDuration}
                  onChange={(e) => patch({ allowsDuration: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagDuration')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagDurationHint')}</span>
                </span>
              </label>
            </fieldset>

            <fieldset className="activity-type-flags">
              <legend>{t('dossiers.activityTypes.quickLegend')}</legend>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.isQuickStart}
                  onChange={(e) => patch({ isQuickStart: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagQuickStart')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagQuickStartHint')}</span>
                </span>
              </label>
              {draft.isQuickStart && (
                <FormField label={t('dossiers.activityTypes.quickStartOrderField')} htmlFor="at-quickstart-order">
                  <input
                    id="at-quickstart-order"
                    type="number"
                    value={draft.quickStartOrder}
                    onChange={(e) => patch({ quickStartOrder: e.target.value })}
                  />
                </FormField>
              )}
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.isSystemDefaultTransport}
                  onChange={(e) => patch({ isSystemDefaultTransport: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagDefaultTransport')}
                  <span className="activity-type-flag-hint">
                    {t('dossiers.activityTypes.flagDefaultTransportHint')}
                  </span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.isActive}
                  onChange={(e) => patch({ isActive: e.target.checked })}
                />
                <span>
                  {t('dossiers.activityTypes.flagActive')}
                  <span className="activity-type-flag-hint">{t('dossiers.activityTypes.flagActiveHint')}</span>
                </span>
              </label>
            </fieldset>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('dossiers.activityTypes.deleteTitle')}
          message={t('dossiers.activityTypes.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await removeActivityType(target.id)
              showSuccess(t('dossiers.activityTypes.deleted'))
              reload()
            } catch (err) {
              showError(describeApiError(err, t('dossiers.activityTypes.deleteFailed')).message)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
