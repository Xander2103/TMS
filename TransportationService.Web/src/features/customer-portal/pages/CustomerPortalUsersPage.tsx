import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  deactivatePortalUser,
  invitePortalUser,
  listPortalUsers,
  reactivatePortalUser,
  resendPortalUserInvite,
  setPortalUserGrants,
  type PortalUserGrants,
  type PortalUserListItem,
} from '../api/customerPortalApi'

const EMPTY_GRANTS: PortalUserGrants = { documents: false, invoices: false, manageUsers: false }

export function CustomerPortalUsersPage() {
  const toast = useToast()
  const [users, setUsers] = useState<PortalUserListItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [inviteOpen, setInviteOpen] = useState(false)
  const [lastToken, setLastToken] = useState<{ email: string; link: string } | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function reload() {
    try {
      const rows = await listPortalUsers()
      setUsers(rows)
      setError(null)
    } catch (err) {
      setError(describeApiError(err, 'De gebruikers konden niet worden geladen.').message)
    } finally {
      setLoaded(true)
    }
  }

  useEffect(() => {
    let mounted = true
    listPortalUsers()
      .then((rows) => {
        if (!mounted) return
        setUsers(rows)
        setError(null)
      })
      .catch((err: unknown) => {
        if (!mounted) return
        setError(describeApiError(err, 'De gebruikers konden niet worden geladen.').message)
      })
      .finally(() => {
        if (mounted) setLoaded(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handleDeactivate(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      await deactivatePortalUser(user.id)
      toast.showSuccess(`${user.firstName} ${user.lastName} is gedeactiveerd.`)
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'Deactiveren is mislukt.').message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleReactivate(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      await reactivatePortalUser(user.id)
      toast.showSuccess(`${user.firstName} ${user.lastName} is opnieuw actief.`)
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'Reactiveren is mislukt.').message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleResend(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      const result = await resendPortalUserInvite(user.id)
      toast.showSuccess(`Uitnodiging opnieuw verstuurd naar ${user.email}.`)
      // The backend only ever includes activationToken while its mail provider is the
      // development sink (no real SMTP/SendGrid configured) — see customerPortalApi.ts. Once a
      // live provider is registered, this is null and the normal "sent" toast above is the only
      // feedback the admin gets, exactly as it should be.
      if (result.activationToken) {
        setLastToken({ email: user.email, link: `/activeren?token=${result.activationToken}&email=${encodeURIComponent(user.email)}` })
      } else {
        setLastToken(null)
      }
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'Opnieuw uitnodigen is mislukt.').message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleToggleGrant(user: PortalUserListItem, key: keyof PortalUserGrants) {
    setBusyId(user.id)
    try {
      await setPortalUserGrants(user.id, { ...user.grants, [key]: !user.grants[key] })
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, 'De rechten konden niet worden aangepast.').message)
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<PortalUserListItem>[] = [
    { key: 'name', header: 'Naam', render: (row) => `${row.firstName} ${row.lastName}` },
    { key: 'email', header: 'E-mailadres', render: (row) => row.email },
    {
      key: 'status',
      header: 'Status',
      render: (row) =>
        !row.isActive ? (
          <Badge tone="neutral">Gedeactiveerd</Badge>
        ) : row.hasPendingActivation ? (
          <Badge tone="warning">Uitnodiging openstaand</Badge>
        ) : (
          <Badge tone="success">Actief</Badge>
        ),
    },
    {
      key: 'grants',
      header: 'Extra rechten',
      render: (row) => (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem', fontSize: '0.85rem' }}>
          <label>
            <input
              type="checkbox"
              checked={row.grants.documents}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'documents')}
            />{' '}
            Documenten
          </label>
          <label>
            <input
              type="checkbox"
              checked={row.grants.invoices}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'invoices')}
            />{' '}
            Facturen
          </label>
          <label>
            <input
              type="checkbox"
              checked={row.grants.manageUsers}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'manageUsers')}
            />{' '}
            Gebruikersbeheer
          </label>
        </div>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div style={{ display: 'flex', gap: '0.4rem' }}>
          {row.isActive ? (
            <Button variant="secondary" disabled={busyId === row.id} onClick={() => void handleDeactivate(row)}>
              Deactiveren
            </Button>
          ) : (
            <Button variant="secondary" disabled={busyId === row.id} onClick={() => void handleReactivate(row)}>
              Reactiveren
            </Button>
          )}
          {row.hasPendingActivation && (
            <Button variant="ghost" disabled={busyId === row.id} onClick={() => void handleResend(row)}>
              Uitnodiging opnieuw sturen
            </Button>
          )}
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Gebruikers"
        subtitle="Beheer wie namens uw bedrijf toegang heeft tot het klantportaal."
        action={<Button onClick={() => setInviteOpen(true)}>Gebruiker uitnodigen</Button>}
      />
      <DataTable
        columns={columns}
        rows={users}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen gebruikers uitgenodigd."
        loadingMessage="Gebruikers laden..."
      />
      {lastToken && (
        <p style={{ fontSize: '0.85rem', opacity: 0.75 }}>
          Ontwikkelomgeving: activeringslink voor {lastToken.email} — <code>{lastToken.link}</code>
        </p>
      )}
      {inviteOpen && (
        <InviteUserModal
          onClose={() => setInviteOpen(false)}
          onInvited={(result) => {
            setInviteOpen(false)
            // See handleResend: the dev-only activation link is shown ONLY when the backend
            // actually returned a raw token (development sink), never under a live mail provider.
            setLastToken(
              result.activationToken
                ? {
                    email: result.user.email,
                    link: `/activeren?token=${result.activationToken}&email=${encodeURIComponent(result.user.email)}`,
                  }
                : null,
            )
            toast.showSuccess(`Uitnodiging verstuurd naar ${result.user.email}.`)
            void reload()
          }}
        />
      )}
    </div>
  )
}

function InviteUserModal({
  onClose,
  onInvited,
}: {
  onClose: () => void
  onInvited: (result: Awaited<ReturnType<typeof invitePortalUser>>) => void
}) {
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [grants, setGrants] = useState<PortalUserGrants>(EMPTY_GRANTS)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!firstName.trim() || !lastName.trim() || !email.trim()) {
      setError('Vul voornaam, achternaam en e-mailadres in.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await invitePortalUser({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        grants,
      })
      onInvited(result)
    } catch (err) {
      setError(describeApiError(err, 'De uitnodiging kon niet worden verstuurd.').message)
      setBusy(false)
    }
  }

  return (
    <Modal title="Gebruiker uitnodigen" onClose={onClose} busy={busy}>
      <form onSubmit={handleSubmit} noValidate>
        {error && (
          <p role="alert" style={{ color: 'var(--danger, #b3261e)' }}>
            {error}
          </p>
        )}
        <FormField label="Voornaam" htmlFor="piu-first" required>
          <input id="piu-first" value={firstName} onChange={(e) => setFirstName(e.target.value)} disabled={busy} autoFocus />
        </FormField>
        <FormField label="Achternaam" htmlFor="piu-last" required>
          <input id="piu-last" value={lastName} onChange={(e) => setLastName(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label="E-mailadres" htmlFor="piu-email" required>
          <input id="piu-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label="Extra rechten" hint="Opdrachten indienen en berichten zijn altijd beschikbaar.">
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.documents}
              onChange={(e) => setGrants((g) => ({ ...g, documents: e.target.checked }))}
              disabled={busy}
            />{' '}
            Documenten bekijken
          </label>
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.invoices}
              onChange={(e) => setGrants((g) => ({ ...g, invoices: e.target.checked }))}
              disabled={busy}
            />{' '}
            Facturen bekijken
          </label>
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.manageUsers}
              onChange={(e) => setGrants((g) => ({ ...g, manageUsers: e.target.checked }))}
              disabled={busy}
            />{' '}
            Gebruikers beheren
          </label>
        </FormField>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem', marginTop: '1rem' }}>
          <Button type="button" variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" disabled={busy}>
            {busy ? 'Bezig...' : 'Uitnodigen'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
