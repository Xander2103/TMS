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
      .catch(() => setLoadError('De activiteitstypes konden niet worden geladen.'))
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">Je hebt geen rechten om activiteitstypes te bekijken.</p>

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
        showSuccess('Activiteitstype bijgewerkt.')
      } else {
        await createActivityType(input)
        showSuccess('Activiteitstype toegevoegd.')
      }
      setDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'Het activiteitstype kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  const columns: Column<ActivityType>[] = [
    {
      key: 'name',
      header: 'Naam',
      render: (type) => {
        const Icon = activityTypeIcon(type.icon)
        return (
          <span className="activity-type-name">
            <Icon size={18} aria-hidden />
            <span>
              {type.name}
              {type.isSystemDefaultTransport && (
                <span className="activity-type-default"> <Badge tone="info">Standaard transport</Badge></span>
              )}
            </span>
          </span>
        )
      },
    },
    { key: 'code', header: 'Code', render: (type) => <code className="activity-type-code">{type.code}</code> },
    {
      key: 'capabilities',
      header: 'Mogelijkheden',
      render: (type) => (
        <span className="activity-type-caps">
          {type.hasStops && <Badge tone="neutral">Transportopdracht</Badge>}
          {type.supportsGoods && <Badge tone="neutral">Goederen</Badge>}
          {type.planningRelevant && <Badge tone="neutral">Planning</Badge>}
          {type.warehouseRelevant && <Badge tone="neutral">Magazijn</Badge>}
          {type.allowsDuration && <Badge tone="neutral">Duur</Badge>}
        </span>
      ),
    },
    {
      key: 'quickstart',
      header: 'Snelstart',
      render: (type) => (type.isQuickStart ? <Badge tone="success">{`Tegel ${type.quickStartOrder}`}</Badge> : '—'),
    },
    {
      key: 'active',
      header: 'Actief',
      render: (type) => (
        <Badge tone={type.isActive ? 'success' : 'neutral'}>{type.isActive ? 'Actief' : 'Inactief'}</Badge>
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
                  Bewerken
                </button>
                <button
                  type="button"
                  className="issued-items-link issued-items-link-danger"
                  onClick={() => setDeleteTarget(type)}
                >
                  Verwijderen
                </button>
              </span>
            ),
          } satisfies Column<ActivityType>,
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Activiteitstypes' }]} />
      <PageHeader
        title="Activiteitstypes"
        subtitle="De soorten werk die in een dossier kunnen voorkomen (transport, kraanwerk, opslag, …). De vinkjes bepalen het gedrag — pas ze aan aan jouw activiteiten."
        action={
          canManage ? (
            <Button
              onClick={() => {
                setDraftError(null)
                setDraft(emptyDraft())
              }}
            >
              Nieuw activiteitstype
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
        emptyMessage="Nog geen activiteitstypes."
        rowClassName={(type) => (type.isActive ? undefined : 'activity-type-row-inactive')}
      />

      {draft && (
        <Modal
          title={draft.type ? `Activiteitstype bewerken — ${draft.type.name}` : 'Nieuw activiteitstype'}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="activity-type-form" disabled={busy}>
                Opslaan
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
              label="Code"
              htmlFor="at-code"
              required
              hint={draft.type ? 'De code is vast na aanmaak.' : 'bv. KRAANWERK — vast na aanmaak.'}
            >
              <input
                id="at-code"
                value={draft.code}
                maxLength={50}
                disabled={draft.type !== null}
                onChange={(e) => patch({ code: e.target.value.toUpperCase() })}
              />
            </FormField>
            <FormField label="Naam" htmlFor="at-name" required hint="bv. Kraanwerk ter plaatse">
              <input id="at-name" value={draft.name} maxLength={100} onChange={(e) => patch({ name: e.target.value })} />
            </FormField>
            <FormField label="Icoon" htmlFor="at-icon">
              <select id="at-icon" value={draft.icon} onChange={(e) => patch({ icon: e.target.value })}>
                <option value="">Standaard</option>
                {Object.entries(ACTIVITY_TYPE_ICONS).map(([key, { label }]) => (
                  <option key={key} value={key}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField
              label="KPI-categorie"
              htmlFor="at-kpi"
              hint="Vrij groeperingslabel voor rapportage, bv. Kraan"
            >
              <input
                id="at-kpi"
                value={draft.kpiCategory}
                maxLength={50}
                onChange={(e) => patch({ kpiCategory: e.target.value })}
              />
            </FormField>
            <FormField label="Sorteervolgorde" htmlFor="at-sort">
              <input
                id="at-sort"
                type="number"
                value={draft.sortOrder}
                onChange={(e) => patch({ sortOrder: e.target.value })}
              />
            </FormField>

            <fieldset className="activity-type-flags">
              <legend>Mogelijkheden</legend>
              <label className="tof-checkbox">
                <input type="checkbox" checked={draft.hasStops} onChange={(e) => patch({ hasStops: e.target.checked })} />
                <span>
                  Transportopdracht
                  <span className="activity-type-flag-hint">
                    Wordt uitgevoerd via een transportopdracht (stops, goederen, planning)
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
                  Goederen
                  <span className="activity-type-flag-hint">Het dossier toont de goederensectie bij deze activiteit</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.planningRelevant}
                  onChange={(e) => patch({ planningRelevant: e.target.checked })}
                />
                <span>
                  Planningsrelevant
                  <span className="activity-type-flag-hint">Telt mee voor planning en capaciteit</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.warehouseRelevant}
                  onChange={(e) => patch({ warehouseRelevant: e.target.checked })}
                />
                <span>
                  Magazijnrelevant
                  <span className="activity-type-flag-hint">Zichtbaar voor de magazijnwerking</span>
                </span>
              </label>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.allowsDuration}
                  onChange={(e) => patch({ allowsDuration: e.target.checked })}
                />
                <span>
                  Duur registreerbaar
                  <span className="activity-type-flag-hint">Uren invulbaar op de activiteit (bv. kraanuren)</span>
                </span>
              </label>
            </fieldset>

            <fieldset className="activity-type-flags">
              <legend>Snelstart & standaard</legend>
              <label className="tof-checkbox">
                <input
                  type="checkbox"
                  checked={draft.isQuickStart}
                  onChange={(e) => patch({ isQuickStart: e.target.checked })}
                />
                <span>
                  Snelstart-tegel
                  <span className="activity-type-flag-hint">Verschijnt als sjabloontegel op het scherm “Nieuw dossier”</span>
                </span>
              </label>
              {draft.isQuickStart && (
                <FormField label="Volgorde snelstart" htmlFor="at-quickstart-order">
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
                  Standaard transporttype
                  <span className="activity-type-flag-hint">
                    Gebruikt voor opdrachten die zonder dossier binnenkomen (EDI, klantportaal). Er is er altijd precies
                    één actief; aanvinken verplaatst de vlag.
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
                  Actief
                  <span className="activity-type-flag-hint">Inactieve types zijn niet meer kiesbaar bij nieuwe activiteiten</span>
                </span>
              </label>
            </fieldset>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Activiteitstype verwijderen"
          message={`Weet je zeker dat je '${deleteTarget.name}' wilt verwijderen? Een type dat in gebruik is, kan alleen gedeactiveerd worden.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await removeActivityType(target.id)
              showSuccess('Activiteitstype verwijderd.')
              reload()
            } catch (err) {
              showError(describeApiError(err, 'Het activiteitstype kon niet worden verwijderd.').message)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
