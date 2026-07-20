import { useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { apiClient } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { getRoles } from '../../roles/api/rolesApi'
import type { Role } from '../../roles/types/role'

interface SuggestedRole {
  roleId: string
  roleName: string
  viaFunction: string
}

interface CreateUserAccountDialogProps {
  employeeId: string
  firstName: string
  lastName: string
  email: string | null
  onClose: (created: boolean) => void
}

/**
 * Explicit opt-in account creation from an employee. Function→role mappings pre-select
 * SUGGESTED roles; the administrator confirms the final set. On success the single-use
 * activation token is shown exactly once — the user picks their own password with it.
 */
export function CreateUserAccountDialog({ employeeId, firstName, lastName, email, onClose }: CreateUserAccountDialogProps) {
  const [roles, setRoles] = useState<Role[]>([])
  const [suggested, setSuggested] = useState<SuggestedRole[]>([])
  const [selectedRoleIds, setSelectedRoleIds] = useState<Set<string>>(new Set())
  const [accountEmail, setAccountEmail] = useState(email ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [activation, setActivation] = useState<{ token: string; expiresAtUtc: string } | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([
      getRoles(),
      apiClient.getJson<SuggestedRole[]>(`/api/users/suggested-roles?employeeId=${employeeId}`),
    ])
      .then(([allRoles, suggestions]) => {
        if (!mounted) return
        setRoles(allRoles.filter((role) => role.isActive))
        setSuggested(suggestions)
        setSelectedRoleIds(new Set(suggestions.map((s) => s.roleId)))
      })
      .catch(() => {
        if (mounted) setError('Rollen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [employeeId])

  function toggleRole(roleId: string) {
    setSelectedRoleIds((current) => {
      const next = new Set(current)
      if (next.has(roleId)) next.delete(roleId)
      else next.add(roleId)
      return next
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!accountEmail.trim() || !accountEmail.includes('@')) {
      setError('Een geldig e-mailadres is verplicht voor een account.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const user = await apiClient.postJson<{ id: string }, object>('/api/users', {
        email: accountEmail.trim(),
        firstName,
        lastName,
        employeeId,
        customerId: null,
        roleIds: [...selectedRoleIds],
      })
      const started = await apiClient.postJson<{ token: string; expiresAtUtc: string }, object>(
        `/api/users/${user.id}/activation`,
        {},
      )
      setActivation(started)
    } catch (err) {
      setError(describeApiError(err, 'Het account kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  if (activation) {
    return (
      <Modal
        title="Account aangemaakt"
        onClose={() => onClose(true)}
        footer={<Button onClick={() => onClose(true)}>Sluiten</Button>}
      >
        <p>
          Het account voor <strong>{firstName} {lastName}</strong> is aangemaakt. Bezorg de activatielink hieronder —
          die wordt maar één keer getoond, is 72 uur geldig en kan maar één keer gebruikt worden. De gebruiker kiest
          er zelf een wachtwoord mee.
        </p>
        <FormField label="Activatielink" htmlFor="ua-token">
          <input id="ua-token" readOnly value={`${window.location.origin}/reset-password?token=${activation.token}`} onFocus={(e) => e.target.select()} />
        </FormField>
      </Modal>
    )
  }

  return (
    <Modal
      title={`Account aanmaken voor ${firstName} ${lastName}`}
      onClose={() => onClose(false)}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" form="ua-form" disabled={busy}>
            {busy ? 'Aanmaken…' : 'Account aanmaken'}
          </Button>
        </>
      }
    >
      <form id="ua-form" onSubmit={handleSubmit} noValidate>
        <ValidationSummary message={error} />
        <FormField label="E-mailadres" htmlFor="ua-email" required hint="Hiermee meldt de gebruiker zich aan.">
          <input id="ua-email" type="email" value={accountEmail} onChange={(e) => setAccountEmail(e.target.value)} maxLength={250} disabled={busy} />
        </FormField>
        <FormField label="Rollen" htmlFor="ua-roles" hint="Voorgestelde rollen komen uit de functie→rol-koppelingen; pas gerust aan.">
          <div>
            {roles.map((role) => {
              const suggestion = suggested.find((s) => s.roleId === role.id)
              return (
                <label key={role.id} className="customer-form-checkbox" style={{ display: 'flex', gap: 8 }}>
                  <input
                    type="checkbox"
                    checked={selectedRoleIds.has(role.id)}
                    onChange={() => toggleRole(role.id)}
                    disabled={busy}
                  />
                  {role.name}
                  {suggestion && <Badge tone="info">voorgesteld via {suggestion.viaFunction}</Badge>}
                </label>
              )
            })}
            {roles.length === 0 && <span>Geen rollen beschikbaar.</span>}
          </div>
        </FormField>
      </form>
    </Modal>
  )
}
