import { useCallback, useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import {
  applyTaskTemplate,
  createTaskRecurrence,
  createTaskTemplate,
  deleteTaskRecurrence,
  deleteTaskTemplate,
  listTaskRecurrences,
  listTaskTemplates,
  updateTaskRecurrence,
  updateTaskTemplate,
} from '../api/tasksApi'
import {
  TASK_RECURRENCE_INTERVAL_LABELS,
  type SaveTaskRecurrenceInput,
  type SaveTaskTemplateInput,
  type TaskRecurrence,
  type TaskTemplate,
} from '../api/types'
import { ApplyTemplateDialog } from '../components/ApplyTemplateDialog'
import { RecurrenceDialog } from '../components/RecurrenceDialog'
import { TemplateEditorDialog } from '../components/TemplateEditorDialog'
import '../components/tasks.css'

/** Beheer van taaksjablonen en terugkerende taken (onboarding-checklists, periodieke controles). */
export function TaskTemplatesPage() {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManageTemplates = hasPermission('tasks.manage_templates')
  const canManageRecurring = hasPermission('tasks.manage_recurring')
  const canApply = hasPermission('tasks.assign')
  const { options: categories } = useLookupOptions('/api/task-categories')

  const [templates, setTemplates] = useState<TaskTemplate[]>([])
  const [recurrences, setRecurrences] = useState<TaskRecurrence[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [templateDialog, setTemplateDialog] = useState<{ initial: TaskTemplate | null } | null>(null)
  const [applyDialog, setApplyDialog] = useState<TaskTemplate | null>(null)
  const [recurrenceDialog, setRecurrenceDialog] = useState<{ initial: TaskRecurrence | null } | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<{ kind: 'template' | 'recurrence'; id: string; name: string } | null>(null)

  const reload = useCallback(() => {
    Promise.all([listTaskTemplates(true), canManageRecurring ? listTaskRecurrences() : Promise.resolve([])])
      .then(([templateData, recurrenceData]) => {
        setTemplates(templateData)
        setRecurrences(recurrenceData)
        setError(null)
        setIsLoading(false)
      })
      .catch(() => {
        setError('De sjablonen konden niet worden geladen.')
        setIsLoading(false)
      })
  }, [canManageRecurring])

  useEffect(() => {
    reload()
  }, [reload])

  async function submitTemplate(input: SaveTaskTemplateInput) {
    setBusy(true)
    try {
      if (templateDialog?.initial) {
        await updateTaskTemplate(templateDialog.initial.id, input)
      } else {
        await createTaskTemplate(input)
      }
      showSuccess('Sjabloon opgeslagen.')
      setTemplateDialog(null)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Opslaan is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitApply(template: TaskTemplate, employeeId: string, startAt: string | null) {
    setBusy(true)
    try {
      const created = await applyTaskTemplate(template.id, { employeeId, startAt: startAt ?? undefined })
      showSuccess(`${created.length} taken aangemaakt uit "${template.name}".`)
      setApplyDialog(null)
    } catch (err) {
      showError(describeApiError(err, 'Toepassen is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function submitRecurrence(input: SaveTaskRecurrenceInput) {
    setBusy(true)
    try {
      if (recurrenceDialog?.initial) {
        await updateTaskRecurrence(recurrenceDialog.initial.id, input)
      } else {
        await createTaskRecurrence(input)
      }
      showSuccess('Terugkerende taak opgeslagen.')
      setRecurrenceDialog(null)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Opslaan is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await (target.kind === 'template' ? deleteTaskTemplate(target.id) : deleteTaskRecurrence(target.id))
      showSuccess(`'${target.name}' verwijderd.`)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Verwijderen is mislukt.').message)
    }
  }

  const templateColumns: Column<TaskTemplate>[] = [
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'description', header: 'Beschrijving', render: (row) => row.description ?? '—' },
    { key: 'items', header: 'Taken', render: (row) => String(row.items.length) },
    {
      key: 'active',
      header: 'Actief',
      render: (row) => <Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'Actief' : 'Inactief'}</Badge>,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <>
          {canApply && (
            <Button variant="ghost" onClick={() => setApplyDialog(row)} disabled={busy || !row.isActive}>
              Toepassen op medewerker
            </Button>
          )}
          {canManageTemplates && (
            <>
              <Button variant="ghost" onClick={() => setTemplateDialog({ initial: row })} disabled={busy}>
                Bewerken
              </Button>
              <Button
                variant="ghost"
                onClick={() => setDeleteTarget({ kind: 'template', id: row.id, name: row.name })}
                disabled={busy}
              >
                Verwijderen
              </Button>
            </>
          )}
        </>
      ),
    },
  ]

  const recurrenceColumns: Column<TaskRecurrence>[] = [
    { key: 'template', header: 'Sjabloon', render: (row) => row.templateName },
    { key: 'employee', header: 'Medewerker', render: (row) => row.assignedEmployeeName },
    {
      key: 'interval',
      header: 'Interval',
      render: (row) =>
        row.interval === 'CustomDays' && row.customIntervalDays != null
          ? `Elke ${row.customIntervalDays} dagen`
          : TASK_RECURRENCE_INTERVAL_LABELS[row.interval],
    },
    { key: 'start', header: 'Start', render: (row) => row.startDate },
    { key: 'end', header: 'Einde', render: (row) => row.endDate ?? '—' },
    {
      key: 'active',
      header: 'Actief',
      render: (row) => <Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'Actief' : 'Inactief'}</Badge>,
    },
    {
      key: 'actions',
      header: '',
      render: (row) =>
        canManageRecurring ? (
          <>
            <Button variant="ghost" onClick={() => setRecurrenceDialog({ initial: row })} disabled={busy}>
              Bewerken
            </Button>
            <Button
              variant="ghost"
              onClick={() => setDeleteTarget({ kind: 'recurrence', id: row.id, name: row.templateName })}
              disabled={busy}
            >
              Verwijderen
            </Button>
          </>
        ) : null,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Taaksjablonen' }]} />
      <PageHeader
        title="Taaksjablonen"
        subtitle="Sjablonen voor terugkerende checklists en periodiek gegenereerde taken."
        action={
          canManageTemplates ? (
            <Button onClick={() => setTemplateDialog({ initial: null })} disabled={busy}>
              Nieuw sjabloon
            </Button>
          ) : undefined
        }
      />

      <DataTable
        columns={templateColumns}
        rows={templates}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen sjablonen."
      />

      {canManageRecurring && (
        <section className="task-settings-section">
          <div className="task-settings-section-head">
            <h3>Terugkerende taken</h3>
            <Button variant="secondary" onClick={() => setRecurrenceDialog({ initial: null })} disabled={busy}>
              Nieuwe terugkerende taak
            </Button>
          </div>
          <DataTable
            columns={recurrenceColumns}
            rows={recurrences}
            rowKey={(row) => row.id}
            isLoading={isLoading}
            emptyMessage="Nog geen terugkerende taken."
          />
        </section>
      )}

      {templateDialog && (
        <TemplateEditorDialog
          initial={templateDialog.initial}
          categories={categories}
          busy={busy}
          onSubmit={(input) => void submitTemplate(input)}
          onClose={() => setTemplateDialog(null)}
        />
      )}

      {applyDialog && (
        <ApplyTemplateDialog
          templateName={applyDialog.name}
          busy={busy}
          onSubmit={(employeeId, startAt) => void submitApply(applyDialog, employeeId, startAt)}
          onClose={() => setApplyDialog(null)}
        />
      )}

      {recurrenceDialog && (
        <RecurrenceDialog
          initial={recurrenceDialog.initial}
          templates={templates.filter((template) => template.isActive || template.id === recurrenceDialog.initial?.templateId)}
          busy={busy}
          onSubmit={(input) => void submitRecurrence(input)}
          onClose={() => setRecurrenceDialog(null)}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={deleteTarget.kind === 'template' ? 'Sjabloon verwijderen' : 'Terugkerende taak verwijderen'}
          message={`Weet je zeker dat je '${deleteTarget.name}' wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
