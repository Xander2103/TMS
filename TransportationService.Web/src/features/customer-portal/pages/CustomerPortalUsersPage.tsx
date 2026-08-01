import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
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
      setError(describeApiError(err, t('common.users.loadError')).message)
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
        setError(describeApiError(err, t('common.users.loadError')).message)
      })
      .finally(() => {
        if (mounted) setLoaded(true)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handleDeactivate(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      await deactivatePortalUser(user.id)
      toast.showSuccess(t('common.users.toasts.deactivated', { name: `${user.firstName} ${user.lastName}` }))
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('common.users.errors.deactivateFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleReactivate(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      await reactivatePortalUser(user.id)
      toast.showSuccess(t('common.users.toasts.reactivated', { name: `${user.firstName} ${user.lastName}` }))
      await reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('common.users.errors.reactivateFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  async function handleResend(user: PortalUserListItem) {
    setBusyId(user.id)
    try {
      const result = await resendPortalUserInvite(user.id)
      toast.showSuccess(t('common.users.toasts.inviteResent', { email: user.email }))
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
      toast.showError(describeApiError(err, t('common.users.errors.resendFailed')).message)
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
      toast.showError(describeApiError(err, t('common.users.errors.grantsFailed')).message)
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<PortalUserListItem>[] = [
    { key: 'name', header: t('common.users.columns.name'), render: (row) => `${row.firstName} ${row.lastName}` },
    { key: 'email', header: t('common.users.columns.email'), render: (row) => row.email },
    {
      key: 'status',
      header: t('common.users.columns.status'),
      render: (row) =>
        !row.isActive ? (
          <Badge tone="neutral">{t('common.users.status.deactivated')}</Badge>
        ) : row.hasPendingActivation ? (
          <Badge tone="warning">{t('common.users.status.pendingInvite')}</Badge>
        ) : (
          <Badge tone="success">{t('common.users.status.active')}</Badge>
        ),
    },
    {
      key: 'grants',
      header: t('common.users.columns.grants'),
      render: (row) => (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem', fontSize: '0.85rem' }}>
          <label>
            <input
              type="checkbox"
              checked={row.grants.documents}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'documents')}
            />{' '}
            {t('common.users.grants.documents')}
          </label>
          <label>
            <input
              type="checkbox"
              checked={row.grants.invoices}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'invoices')}
            />{' '}
            {t('common.users.grants.invoices')}
          </label>
          <label>
            <input
              type="checkbox"
              checked={row.grants.manageUsers}
              disabled={busyId === row.id}
              onChange={() => void handleToggleGrant(row, 'manageUsers')}
            />{' '}
            {t('common.users.grants.manageUsers')}
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
              {t('common.users.actions.deactivate')}
            </Button>
          ) : (
            <Button variant="secondary" disabled={busyId === row.id} onClick={() => void handleReactivate(row)}>
              {t('common.users.actions.reactivate')}
            </Button>
          )}
          {row.hasPendingActivation && (
            <Button variant="ghost" disabled={busyId === row.id} onClick={() => void handleResend(row)}>
              {t('common.users.actions.resendInvite')}
            </Button>
          )}
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('common.users.title')}
        subtitle={t('common.users.subtitle')}
        action={<Button onClick={() => setInviteOpen(true)}>{t('common.users.invite')}</Button>}
      />
      <DataTable
        columns={columns}
        rows={users}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('common.users.empty')}
        loadingMessage={t('common.users.loading')}
      />
      {lastToken && (
        <p style={{ fontSize: '0.85rem', opacity: 0.75 }}>
          {t('common.users.devActivationLink', { email: lastToken.email })} <code>{lastToken.link}</code>
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
            toast.showSuccess(t('common.users.toasts.inviteSent', { email: result.user.email }))
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
  const { t } = useLocale()
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [grants, setGrants] = useState<PortalUserGrants>(EMPTY_GRANTS)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!firstName.trim() || !lastName.trim() || !email.trim()) {
      setError(t('common.users.errors.validation'))
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
      setError(describeApiError(err, t('common.users.errors.inviteFailed')).message)
      setBusy(false)
    }
  }

  return (
    <Modal title={t('common.users.inviteModal.title')} onClose={onClose} busy={busy}>
      <form onSubmit={handleSubmit} noValidate>
        {error && (
          <p role="alert" style={{ color: 'var(--danger, #b3261e)' }}>
            {error}
          </p>
        )}
        <FormField label={t('common.users.inviteModal.firstName')} htmlFor="piu-first" required>
          <input id="piu-first" value={firstName} onChange={(e) => setFirstName(e.target.value)} disabled={busy} autoFocus />
        </FormField>
        <FormField label={t('common.users.inviteModal.lastName')} htmlFor="piu-last" required>
          <input id="piu-last" value={lastName} onChange={(e) => setLastName(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t('common.users.inviteModal.email')} htmlFor="piu-email" required>
          <input id="piu-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} />
        </FormField>
        <FormField label={t('common.users.inviteModal.extraRights')} hint={t('common.users.inviteModal.extraRightsHint')}>
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.documents}
              onChange={(e) => setGrants((g) => ({ ...g, documents: e.target.checked }))}
              disabled={busy}
            />{' '}
            {t('common.users.inviteModal.viewDocuments')}
          </label>
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.invoices}
              onChange={(e) => setGrants((g) => ({ ...g, invoices: e.target.checked }))}
              disabled={busy}
            />{' '}
            {t('common.users.inviteModal.viewInvoices')}
          </label>
          <label style={{ display: 'block' }}>
            <input
              type="checkbox"
              checked={grants.manageUsers}
              onChange={(e) => setGrants((g) => ({ ...g, manageUsers: e.target.checked }))}
              disabled={busy}
            />{' '}
            {t('common.users.inviteModal.manageUsers')}
          </label>
        </FormField>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem', marginTop: '1rem' }}>
          <Button type="button" variant="secondary" onClick={onClose} disabled={busy}>
            {t('common.actions.cancel')}
          </Button>
          <Button type="submit" disabled={busy}>
            {busy ? t('common.actions.busy') : t('common.users.inviteModal.submit')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
