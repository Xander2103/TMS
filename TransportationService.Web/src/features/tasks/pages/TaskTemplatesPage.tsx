import { useCallback, useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
        setError(t('tasks.templates.loadFailed'))
        setIsLoading(false)
      })
  }, [canManageRecurring, t])

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
      showSuccess(t('tasks.templates.saved'))
      setTemplateDialog(null)
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.templates.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function submitApply(template: TaskTemplate, employeeId: string, startAt: string | null) {
    setBusy(true)
    try {
      const created = await applyTaskTemplate(template.id, { employeeId, startAt: startAt ?? undefined })
      showSuccess(t('tasks.templates.applied', { count: created.length, name: template.name }))
      setApplyDialog(null)
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.templates.applyFailed')))
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
      showSuccess(t('tasks.templates.recurrenceSaved'))
      setRecurrenceDialog(null)
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.templates.saveFailed')))
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
      showSuccess(t('tasks.templates.deleted', { name: target.name }))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tasks.templates.deleteFailed')))
    }
  }

  const templateColumns: Column<TaskTemplate>[] = [
    { key: 'name', header: t('tasks.templates.columns.name'), render: (row) => row.name },
    { key: 'description', header: t('tasks.templates.columns.description'), render: (row) => row.description ?? '—' },
    { key: 'items', header: t('tasks.templates.columns.items'), render: (row) => String(row.items.length) },
    {
      key: 'active',
      header: t('tasks.templates.columns.active'),
      render: (row) => (
        <Badge tone={row.isActive ? 'success' : 'neutral'}>
          {row.isActive ? t('tasks.templates.statusActive') : t('tasks.templates.statusInactive')}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <>
          {canApply && (
            <Button variant="ghost" onClick={() => setApplyDialog(row)} disabled={busy || !row.isActive}>
              {t('tasks.templates.applyToEmployee')}
            </Button>
          )}
          {canManageTemplates && (
            <>
              <Button variant="ghost" onClick={() => setTemplateDialog({ initial: row })} disabled={busy}>
                {t('ui.actions.edit')}
              </Button>
              <Button
                variant="ghost"
                onClick={() => setDeleteTarget({ kind: 'template', id: row.id, name: row.name })}
                disabled={busy}
              >
                {t('ui.actions.delete')}
              </Button>
            </>
          )}
        </>
      ),
    },
  ]

  const recurrenceColumns: Column<TaskRecurrence>[] = [
    { key: 'template', header: t('tasks.templates.columns.template'), render: (row) => row.templateName },
    { key: 'employee', header: t('tasks.templates.columns.employee'), render: (row) => row.assignedEmployeeName },
    {
      key: 'interval',
      header: t('tasks.templates.columns.interval'),
      render: (row) =>
        row.interval === 'CustomDays' && row.customIntervalDays != null
          ? t('tasks.templates.everyXDays', { count: row.customIntervalDays })
          : t(TASK_RECURRENCE_INTERVAL_LABELS[row.interval]),
    },
    { key: 'start', header: t('tasks.templates.columns.start'), render: (row) => row.startDate },
    { key: 'end', header: t('tasks.templates.columns.end'), render: (row) => row.endDate ?? '—' },
    {
      key: 'active',
      header: t('tasks.templates.columns.active'),
      render: (row) => (
        <Badge tone={row.isActive ? 'success' : 'neutral'}>
          {row.isActive ? t('tasks.templates.statusActive') : t('tasks.templates.statusInactive')}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) =>
        canManageRecurring ? (
          <>
            <Button variant="ghost" onClick={() => setRecurrenceDialog({ initial: row })} disabled={busy}>
              {t('ui.actions.edit')}
            </Button>
            <Button
              variant="ghost"
              onClick={() => setDeleteTarget({ kind: 'recurrence', id: row.id, name: row.templateName })}
              disabled={busy}
            >
              {t('ui.actions.delete')}
            </Button>
          </>
        ) : null,
    },
  ]

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('tasks.templates.title') }]}
      />
      <PageHeader
        title={t('tasks.templates.title')}
        subtitle={t('tasks.templates.subtitle')}
        action={
          canManageTemplates ? (
            <Button onClick={() => setTemplateDialog({ initial: null })} disabled={busy}>
              {t('tasks.templates.newTemplate')}
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
        emptyMessage={t('tasks.templates.empty')}
      />

      {canManageRecurring && (
        <section className="task-settings-section">
          <div className="task-settings-section-head">
            <h3>{t('tasks.templates.recurringTitle')}</h3>
            <Button variant="secondary" onClick={() => setRecurrenceDialog({ initial: null })} disabled={busy}>
              {t('tasks.templates.newRecurrence')}
            </Button>
          </div>
          <DataTable
            columns={recurrenceColumns}
            rows={recurrences}
            rowKey={(row) => row.id}
            isLoading={isLoading}
            emptyMessage={t('tasks.templates.emptyRecurrences')}
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
          title={
            deleteTarget.kind === 'template'
              ? t('tasks.templates.deleteTemplateTitle')
              : t('tasks.templates.deleteRecurrenceTitle')
          }
          message={t('tasks.templates.deleteConfirm', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
