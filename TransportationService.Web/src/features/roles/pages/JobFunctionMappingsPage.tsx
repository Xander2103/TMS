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
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
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
        setError('Koppelingen konden niet worden geladen.')
        setLoaded(true)
      })
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<Mapping>[] = [
    { key: 'function', header: 'Functie', render: (row) => row.jobFunctionName },
    { key: 'role', header: 'Voorgestelde rol', render: (row) => row.roleName },
    ...(canManage
      ? [
          {
            key: 'actions',
            header: 'Acties',
            render: (row: Mapping) => (
              <Button variant="ghost" onClick={() => setConfirmDelete(row)}>
                Verwijderen
              </Button>
            ),
          },
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Rollen', to: '/roles' }, { label: 'Functie→rol-koppelingen' }]} />
      <PageHeader
        title="Functie→rol-koppelingen"
        subtitle="Bij het aanmaken van een account uit een medewerker worden deze rollen voorgesteld; de beheerder bevestigt altijd."
        action={canManage && <Button onClick={() => setAddOpen(true)}>Nieuwe koppeling</Button>}
      />
      <DataTable
        columns={columns}
        rows={mappings}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen koppelingen — accounts krijgen dan geen voorgestelde rollen."
        loadingMessage="Koppelingen laden..."
      />

      {addOpen && (
        <AddMappingDialog
          onClose={(saved) => {
            setAddOpen(false)
            if (saved) {
              toast.showSuccess('Koppeling toegevoegd.')
              reload()
            }
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Koppeling verwijderen"
          message={`Koppeling '${confirmDelete.jobFunctionName} → ${confirmDelete.roleName}' verwijderen? Bestaande accounts behouden hun rollen.`}
          confirmLabel="Verwijderen"
          destructive
          busy={busy}
          onConfirm={async () => {
            setBusy(true)
            try {
              await apiClient.deleteRequest(`/api/job-function-role-mappings/${confirmDelete.id}`)
              toast.showSuccess('Koppeling verwijderd.')
              setConfirmDelete(null)
              reload()
            } catch (err) {
              toast.showError(describeApiError(err, 'De koppeling kon niet worden verwijderd.').message)
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
  const functions = useLookupOptions('/api/job-functions')
  const [roles, setRoles] = useState<Role[]>([])
  const [functionId, setFunctionId] = useState('')
  const [roleId, setRoleId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getRoles()
      .then((data) => setRoles(data.filter((role) => role.isActive)))
      .catch(() => setError('Rollen konden niet worden geladen.'))
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!functionId || !roleId) {
      setError('Kies zowel een functie als een rol.')
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
      setError(describeApiError(err, 'De koppeling kon niet worden toegevoegd.').message)
      setSaving(false)
    }
  }

  return (
    <Modal
      title="Nieuwe functie→rol-koppeling"
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            Annuleren
          </Button>
          <Button type="submit" form="mapping-form" disabled={saving}>
            {saving ? 'Toevoegen…' : 'Toevoegen'}
          </Button>
        </>
      }
    >
      <form id="mapping-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} />
        <FormField label="Functie" htmlFor="mp-function" required>
          <select id="mp-function" value={functionId} onChange={(e) => setFunctionId(e.target.value)} disabled={saving}>
            <option value="">— Kies functie —</option>
            {functions.options.map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Rol" htmlFor="mp-role" required>
          <select id="mp-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={saving}>
            <option value="">— Kies rol —</option>
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
