import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { apiClient } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { getRoles } from '../api/rolesApi'
import type { Role } from '../types/role'

interface Mapping {
  id: string
  jobFunctionId: string
  jobFunctionName: string
  roleId: string
  roleName: string
}

/**
 * Configurable HR-function → role mappings. Mappings only SUGGEST roles when an account is
 * created from an employee; the administrator always confirms the final set.
 */
export function JobFunctionMappingsPage() {
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('roles.manage_permissions')

  const [mappings, setMappings] = useState<Mapping[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [addOpen, setAddOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<Mapping | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    apiClient
      .getJson<Mapping[]>('/api/job-function-role-mappings')
      .then((data) => {
        setMappings(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError(t('usersRoles.roles.mappings.loadFailed'))
        setLoaded(true)
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<Mapping>[] = [
    { key: 'function', header: t('usersRoles.roles.mappings.columnFunction'), render: (row) => row.jobFunctionName },
    { key: 'role', header: t('usersRoles.roles.mappings.columnRole'), render: (row) => row.roleName },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: t('usersRoles.roles.mappings.columnActions'),
            render: (row: Mapping) => (
              <Button variant="ghost" onClick={() => setConfirmDelete(row)}>
                {t('ui.actions.delete')}
              </Button>
            ),
          },
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('usersRoles.roles.mappings.breadcrumbRoles'), to: '/roles' }, { label: t('usersRoles.roles.mappings.title') }]}
      />
      <PageHeader
        title={t('usersRoles.roles.mappings.title')}
        subtitle={t('usersRoles.roles.mappings.subtitle')}
        action={canManage && <Button onClick={() => setAddOpen(true)}>{t('usersRoles.roles.mappings.newMapping')}</Button>}
      />
      <DataTable
        columns={columns}
        rows={mappings}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('usersRoles.roles.mappings.empty')}
        loadingMessage={t('usersRoles.roles.mappings.loading')}
      />

      {addOpen && (
        <AddMappingDialog
          onClose={(saved) => {
            setAddOpen(false)
            if (saved) {
              toast.showSuccess(t('usersRoles.roles.mappings.added'))
              reload()
            }
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('usersRoles.roles.mappings.deleteTitle')}
          message={t('usersRoles.roles.mappings.deleteMessage', {
            functionName: confirmDelete.jobFunctionName,
            roleName: confirmDelete.roleName,
          })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await apiClient.deleteRequest(`/api/job-function-role-mappings/${confirmDelete.id}`)
              toast.showSuccess(t('usersRoles.roles.mappings.deleted'))
              setConfirmDelete(null)
              reload()
            } catch (err) {
              toast.showError(localizeApiError(t, err, t('usersRoles.roles.mappings.deleteFailed')))
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

function AddMappingDialog({ onClose }: { onClose: (saved: boolean) => void }) {
  const { t } = useLocale()
  const functions = useLookupOptions('/api/job-functions')
  const [roles, setRoles] = useState<Role[]>([])
  const [functionId, setFunctionId] = useState('')
  const [roleId, setRoleId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getRoles()
      .then((data) => setRoles(data.filter((role) => role.isActive)))
      .catch(() => setError(t('usersRoles.roles.mappings.rolesLoadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!functionId || !roleId) {
      setError(t('usersRoles.roles.mappings.chooseBoth'))
      return
    }
    setSaving(true)
    setError(null)
    try {
      await apiClient.postJson<Mapping, { jobFunctionId: string; roleId: string }>(
        '/api/job-function-role-mappings',
        { jobFunctionId: functionId, roleId },
      )
      onClose(true)
    } catch (err) {
      setError(localizeApiError(t, err, t('usersRoles.roles.mappings.addFailed')))
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('usersRoles.roles.mappings.dialogTitle')}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="mapping-form" disabled={saving}>
            {saving ? t('usersRoles.roles.mappings.adding') : t('usersRoles.roles.mappings.add')}
          </Button>
        </>
      }
    >
      <form id="mapping-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} />
        <FormField label={t('usersRoles.roles.mappings.function')} htmlFor="mp-function" required>
          <select id="mp-function" value={functionId} onChange={(e) => setFunctionId(e.target.value)} disabled={saving}>
            <option value="">{t('usersRoles.roles.mappings.chooseFunction')}</option>
            {functions.options.map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('usersRoles.roles.mappings.role')} htmlFor="mp-role" required>
          <select id="mp-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={saving}>
            <option value="">{t('usersRoles.roles.mappings.chooseRole')}</option>
            {roles.map((role) => (
              <option key={role.id} value={role.id}>
                {role.name}
              </option>
            ))}
          </select>
        </FormField>
      </form>
    </Modal>
  )
}
