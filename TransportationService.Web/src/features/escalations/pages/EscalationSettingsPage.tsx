import { useEffect, useMemo, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { usePermissions } from '../../roles/hooks/usePermissions'
import { listEscalationPolicies, updateEscalationPolicy } from '../api/escalationsApi'
import { ESCALATION_KINDS, ESCALATION_KIND_LABELS, type EscalationKind, type EscalationPolicy } from '../types'
import './escalations.css'

/** Per-rij bewerkbare kopie van een beleid; delayHours als tekst zodat vrij typen kan. */
interface PolicyDraft {
  delayHours: string
  targetPermissionCode: string
  isActive: boolean
}

function toDraft(policy: EscalationPolicy): PolicyDraft {
  return {
    delayHours: String(policy.delayHours),
    targetPermissionCode: policy.targetPermissionCode,
    isActive: policy.isActive,
  }
}

/**
 * "Escalatieregels" (/settings/escalations): per escalatiesoort de termijn, doel-permissie en
 * actief-status beheren. Rij-per-rij opslaan via PUT /api/escalation-policies/{kind}; alleen
 * zichtbaar met `escalations.manage` (zelfde patroon als "Meldingen en e-mails").
 */
export function EscalationSettingsPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const { permissions } = usePermissions()

  const [policies, setPolicies] = useState<EscalationPolicy[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [drafts, setDrafts] = useState<Record<string, PolicyDraft>>({})
  const [savingKind, setSavingKind] = useState<EscalationKind | null>(null)

  const canManage = hasPermission('escalations.manage')

  useEffect(() => {
    if (!canManage) return
    let mounted = true
    listEscalationPolicies()
      .then((data) => {
        if (!mounted) return
        setPolicies(data)
        setDrafts(Object.fromEntries(data.map((policy) => [policy.kind, toDraft(policy)])))
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('escalations.page.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canManage])

  const sortedPolicies = useMemo(() => {
    if (!policies) return []
    return [...policies].sort(
      (a, b) => ESCALATION_KINDS.indexOf(a.kind) - ESCALATION_KINDS.indexOf(b.kind),
    )
  }, [policies])

  const sortedPermissions = useMemo(
    () =>
      [...permissions].sort((a, b) =>
        a.module === b.module ? a.description.localeCompare(b.description, 'nl') : a.module.localeCompare(b.module, 'nl'),
      ),
    [permissions],
  )

  function patchDraft(kind: EscalationKind, patch: Partial<PolicyDraft>) {
    setDrafts((current) => ({ ...current, [kind]: { ...current[kind], ...patch } }))
  }

  async function save(policy: EscalationPolicy) {
    const draft = drafts[policy.kind]
    if (!draft) return

    const delayHours = Number(draft.delayHours)
    if (!Number.isInteger(delayHours) || delayHours < 0 || draft.delayHours.trim() === '') {
      showError(t('escalations.form.invalidDelay'))
      return
    }
    if (!draft.targetPermissionCode) {
      showError(t('escalations.form.targetRequired'))
      return
    }

    setSavingKind(policy.kind)
    try {
      const updated = await updateEscalationPolicy(policy.kind, {
        delayHours,
        targetPermissionCode: draft.targetPermissionCode,
        isActive: draft.isActive,
      })
      setPolicies((current) => (current ?? []).map((p) => (p.kind === updated.kind ? updated : p)))
      setDrafts((current) => ({ ...current, [updated.kind]: toDraft(updated) }))
      showSuccess(t('escalations.form.saved', { kind: t(ESCALATION_KIND_LABELS[policy.kind]) }))
    } catch (err) {
      showError(localizeApiError(t, err, t('escalations.form.saveFailed')))
    } finally {
      setSavingKind(null)
    }
  }

  if (!canManage) {
    return <p className="placeholder-text">{t('escalations.page.noPermission')}</p>
  }

  const columns: Column<EscalationPolicy>[] = [
    {
      key: 'soort',
      header: t('escalations.columns.kind'),
      render: (policy) => <strong>{t(ESCALATION_KIND_LABELS[policy.kind])}</strong>,
    },
    {
      key: 'actief',
      header: t('escalations.columns.active'),
      render: (policy) => {
        const draft = drafts[policy.kind]
        return (
          <label className="escalations-checkbox">
            <input
              type="checkbox"
              aria-label={t('escalations.aria.active', { kind: t(ESCALATION_KIND_LABELS[policy.kind]) })}
              checked={draft?.isActive ?? policy.isActive}
              onChange={(e) => patchDraft(policy.kind, { isActive: e.target.checked })}
            />
          </label>
        )
      },
    },
    {
      key: 'termijn',
      header: t('escalations.columns.delay'),
      render: (policy) => (
        <input
          type="number"
          className="escalations-hours-input"
          min={0}
          step={1}
          aria-label={t('escalations.aria.delay', { kind: t(ESCALATION_KIND_LABELS[policy.kind]) })}
          value={drafts[policy.kind]?.delayHours ?? String(policy.delayHours)}
          onChange={(e) => patchDraft(policy.kind, { delayHours: e.target.value })}
        />
      ),
    },
    {
      key: 'doel',
      header: t('escalations.columns.target'),
      render: (policy) => (
        <select
          className="escalations-permission-select"
          aria-label={t('escalations.aria.target', { kind: t(ESCALATION_KIND_LABELS[policy.kind]) })}
          value={drafts[policy.kind]?.targetPermissionCode ?? policy.targetPermissionCode}
          onChange={(e) => patchDraft(policy.kind, { targetPermissionCode: e.target.value })}
        >
          {sortedPermissions.map((permission) => (
            <option key={permission.code} value={permission.code}>
              {permission.module} — {permission.description}
            </option>
          ))}
        </select>
      ),
    },
    {
      key: 'opslaan',
      header: <span aria-label={t('escalations.columns.actionsAria')} />,
      render: (policy) => (
        <Button onClick={() => void save(policy)} disabled={savingKind === policy.kind}>
          {savingKind === policy.kind ? t('escalations.form.saving') : t('ui.actions.save')}
        </Button>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('escalations.page.title') }]} />
      <PageHeader title={t('escalations.page.title')} subtitle={t('escalations.page.subtitle')} />

      <DataTable
        columns={columns}
        rows={sortedPolicies}
        rowKey={(policy) => policy.kind}
        isLoading={policies === null && !loadError}
        error={loadError}
        emptyMessage={t('escalations.page.empty')}
      />
    </div>
  )
}
