import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import {
  CUSTOMER_LOCATION_ROLE_KEYS,
  linkCustomerAddress,
  listCustomerAddresses,
  pickAddresses,
  unlinkCustomerAddress,
  updateCustomerAddressLink,
  type AddressPickerOption,
  type CustomerAddress,
  type CustomerLocationRole,
} from '../../locations/api/customerAddressesApi'
import { ADDRESS_PICKER_GROUP_KEYS } from '../../locations/api/customerAddressesApi'

interface CustomerAddressesPanelProps {
  customerId: string
}

const ROLES: CustomerLocationRole[] = ['Both', 'Loading', 'Unloading']

/** "Noorderlaan 10, 2030 Antwerpen" */
function formatAddress(a: { street: string | null; houseNumber: string | null; postalCode: string | null; city: string | null }): string {
  const line = [a.street, a.houseNumber].filter(Boolean).join(' ')
  const place = [a.postalCode, a.city].filter(Boolean).join(' ')
  return [line, place].filter(Boolean).join(', ')
}

/**
 * Adressen tab on the customer detail page (sprint 2, central address master).
 *
 * A physical address exists once and may be used by several customers, so this panel manages
 * the RELATIONSHIP: link an existing address, create a new one, set the customer-specific
 * alias/reference/role/defaults, and unlink. Unlinking removes the relationship only — the
 * address itself, and every historical order that used it, stay untouched.
 */
export function CustomerAddressesPanel({ customerId }: CustomerAddressesPanelProps) {
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canEdit = hasPermission('locations.edit')
  const canCreate = hasPermission('locations.create')

  const [rows, setRows] = useState<CustomerAddress[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showInactive, setShowInactive] = useState(false)
  const [busy, setBusy] = useState(false)
  const [showLinkDialog, setShowLinkDialog] = useState(false)
  const [showQuickCreate, setShowQuickCreate] = useState(false)
  const [editing, setEditing] = useState<CustomerAddress | null>(null)
  const [unlinkTarget, setUnlinkTarget] = useState<CustomerAddress | null>(null)

  const reload = useCallback(() => {
    listCustomerAddresses(customerId, showInactive)
      .then((data) => {
        setRows(data)
        setError(null)
      })
      .catch(() => setError(t('customers.addresses.loadFailed')))
  }, [customerId, showInactive, t])

  useEffect(reload, [reload])

  async function handleLink(locationId: string) {
    setBusy(true)
    try {
      await linkCustomerAddress(customerId, {
        locationId,
        alias: null,
        customerReference: null,
        role: 'Both',
        isDefaultLoading: false,
        isDefaultUnloading: false,
        isDefaultBilling: false,
        instructions: null,
      })
      toast.showSuccess(t('customers.addresses.linked'))
      setShowLinkDialog(false)
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.addresses.linkFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleUnlink() {
    if (!unlinkTarget) return
    const target = unlinkTarget
    setUnlinkTarget(null)
    setBusy(true)
    try {
      await unlinkCustomerAddress(customerId, target.linkId)
      toast.showSuccess(t('customers.addresses.unlinked'))
      reload()
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.addresses.unlinkFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  if (error) return <p className="placeholder-text">{error}</p>
  if (rows === null) return <p className="placeholder-text">{t('customers.addresses.loading')}</p>

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('customers.addresses.title')}</h3>
        {canEdit && (
          <span className="customer-locations-new">
            <Button variant="secondary" onClick={() => setShowLinkDialog(true)} disabled={busy}>
              {t('customers.addresses.linkExisting')}
            </Button>
            {canCreate && (
              <Button onClick={() => setShowQuickCreate(true)} disabled={busy}>
                {t('customers.addresses.addNew')}
              </Button>
            )}
          </span>
        )}
      </div>

      <p className="customer-form-muted">{t('customers.addresses.explanation')}</p>

      <label className="customer-form-checkbox">
        <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
        {t('customers.addresses.showInactive')}
      </label>

      {rows.length === 0 && <p className="placeholder-text">{t('customers.addresses.empty')}</p>}

      {rows.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('customers.addresses.columnAddress')}</th>
              <th>{t('customers.addresses.columnReference')}</th>
              <th>{t('customers.addresses.columnRole')}</th>
              <th>{t('customers.addresses.columnDefaults')}</th>
              {canEdit && <th aria-label={t('customers.addresses.actionsAria')} />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.linkId}>
                <td>
                  <Link to={`/locations/${row.locationId}`}>{row.alias ?? row.name}</Link>{' '}
                  <span className="customer-locations-code">({row.code})</span>
                  <div className="customer-form-muted">{formatAddress(row)}</div>
                  {row.linkedCustomerCount > 1 && (
                    <Badge tone="info">
                      {t('customers.addresses.sharedBadge', { count: row.linkedCustomerCount })}
                    </Badge>
                  )}
                  {!row.addressIsActive && <Badge tone="neutral">{t('customers.addresses.addressInactive')}</Badge>}
                  {!row.isActive && <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>}
                </td>
                <td>{row.customerReference ?? '—'}</td>
                <td>{t(CUSTOMER_LOCATION_ROLE_KEYS[row.role])}</td>
                <td>
                  <span className="customer-locations-badges">
                    {row.isDefaultLoading && <Badge tone="info">{t('customers.locations.defaultLoadingBadge')}</Badge>}
                    {row.isDefaultUnloading && <Badge tone="info">{t('customers.locations.defaultUnloadingBadge')}</Badge>}
                    {row.isDefaultBilling && <Badge tone="info">{t('customers.locations.defaultBillingBadge')}</Badge>}
                  </span>
                </td>
                {canEdit && (
                  <td className="issued-items-row-actions">
                    <button type="button" className="issued-items-link" onClick={() => setEditing(row)}>
                      {t('ui.actions.edit')}
                    </button>
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setUnlinkTarget(row)}
                    >
                      {t('customers.addresses.unlink')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {showLinkDialog && (
        <LinkAddressDialog
          customerId={customerId}
          alreadyLinked={new Set(rows.map((r) => r.locationId))}
          busy={busy}
          onPick={handleLink}
          onClose={() => setShowLinkDialog(false)}
        />
      )}

      {showQuickCreate && (
        <LocationQuickCreateDialog
          customerId={customerId}
          onClose={(created) => {
            setShowQuickCreate(false)
            // The quick-create dialog also resolves with an EXISTING address when the user
            // picks one from the duplicate warning; linking is correct either way.
            if (created) void handleLink(created.id)
          }}
        />
      )}

      {editing && (
        <EditLinkDialog
          customerId={customerId}
          link={editing}
          onClose={(saved) => {
            setEditing(null)
            if (saved) reload()
          }}
        />
      )}

      {unlinkTarget && (
        <ConfirmDialog
          title={t('customers.addresses.unlinkTitle')}
          message={t('customers.addresses.unlinkMessage', { name: unlinkTarget.alias ?? unlinkTarget.name })}
          confirmLabel={t('customers.addresses.unlink')}
          destructive
          onConfirm={handleUnlink}
          onCancel={() => setUnlinkTarget(null)}
        />
      )}
    </section>
  )
}

interface LinkAddressDialogProps {
  customerId: string
  alreadyLinked: Set<string>
  busy: boolean
  onPick: (locationId: string) => void
  onClose: () => void
}

/** Search the central address master; already-linked addresses are not offered again. */
function LinkAddressDialog({ customerId, alreadyLinked, busy, onPick, onClose }: LinkAddressDialogProps) {
  const { t } = useLocale()
  const [search, setSearch] = useState('')
  const [options, setOptions] = useState<AddressPickerOption[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    setLoading(true)
    pickAddresses({ customerId, search, take: 50 })
      .then((data) => {
        if (active) setOptions(data.filter((o) => !alreadyLinked.has(o.locationId)))
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [customerId, search, alreadyLinked])

  return (
    <Modal
      title={t('customers.addresses.linkTitle')}
      onClose={onClose}
      footer={
        <Button variant="secondary" onClick={onClose}>
          {t('ui.actions.close')}
        </Button>
      }
    >
      <FormField label={t('customers.addresses.searchLabel')} htmlFor="link-address-search">
        <input
          id="link-address-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('customers.addresses.searchPlaceholder')}
        />
      </FormField>
      {loading && <p className="placeholder-text">{t('customers.addresses.loading')}</p>}
      {!loading && options.length === 0 && <p className="placeholder-text">{t('customers.addresses.noCandidates')}</p>}
      <ul className="followup-results">
        {options.map((option) => (
          <li key={option.locationId}>
            <button type="button" className="link-button" disabled={busy} onClick={() => onPick(option.locationId)}>
              {option.name}
            </button>{' '}
            <span className="customer-form-muted">
              {formatAddress(option)} · {t(ADDRESS_PICKER_GROUP_KEYS[option.group])}
            </span>
          </li>
        ))}
      </ul>
    </Modal>
  )
}

interface EditLinkDialogProps {
  customerId: string
  link: CustomerAddress
  onClose: (saved: boolean) => void
}

/** The customer-specific side of the relationship; the address itself is edited on its own page. */
function EditLinkDialog({ customerId, link, onClose }: EditLinkDialogProps) {
  const { t } = useLocale()
  const toast = useToast()
  const [alias, setAlias] = useState(link.alias ?? '')
  const [reference, setReference] = useState(link.customerReference ?? '')
  const [role, setRole] = useState<CustomerLocationRole>(link.role)
  const [defaultLoading, setDefaultLoading] = useState(link.isDefaultLoading)
  const [defaultUnloading, setDefaultUnloading] = useState(link.isDefaultUnloading)
  const [defaultBilling, setDefaultBilling] = useState(link.isDefaultBilling)
  const [instructions, setInstructions] = useState(link.instructions ?? '')
  const [isActive, setIsActive] = useState(link.isActive)
  const [saving, setSaving] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    try {
      await updateCustomerAddressLink(customerId, link.linkId, {
        alias: alias.trim() || null,
        customerReference: reference.trim() || null,
        role,
        isDefaultLoading: defaultLoading,
        isDefaultUnloading: defaultUnloading,
        isDefaultBilling: defaultBilling,
        instructions: instructions.trim() || null,
        isActive,
      })
      toast.showSuccess(t('customers.addresses.updated'))
      onClose(true)
    } catch (err) {
      toast.showError(describeApiError(err, t('customers.addresses.updateFailed')).message)
      setSaving(false)
    }
  }

  return (
    <Modal
      title={t('customers.addresses.editTitle')}
      onClose={() => onClose(false)}
      busy={saving}
      footer={
        <>
          <Button variant="secondary" onClick={() => onClose(false)} disabled={saving}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="customer-address-link-form" disabled={saving}>
            {saving ? t('customers.common.savingEllipsis') : t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="customer-address-link-form" onSubmit={handleSubmit} noValidate>
        <p className="customer-form-muted">{formatAddress(link)}</p>
        <FormField label={t('customers.addresses.aliasField')} htmlFor="cal-alias" hint={t('customers.addresses.aliasHint')}>
          <input id="cal-alias" value={alias} onChange={(e) => setAlias(e.target.value)} maxLength={200} />
        </FormField>
        <FormField label={t('customers.addresses.referenceField')} htmlFor="cal-ref" hint={t('customers.addresses.referenceHint')}>
          <input id="cal-ref" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
        </FormField>
        <FormField label={t('customers.addresses.columnRole')} htmlFor="cal-role">
          <select id="cal-role" value={role} onChange={(e) => setRole(e.target.value as CustomerLocationRole)}>
            {ROLES.map((option) => (
              <option key={option} value={option}>
                {t(CUSTOMER_LOCATION_ROLE_KEYS[option])}
              </option>
            ))}
          </select>
        </FormField>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={defaultLoading} onChange={(e) => setDefaultLoading(e.target.checked)} />
          {t('customers.locations.defaultLoadingBadge')}
        </label>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={defaultUnloading} onChange={(e) => setDefaultUnloading(e.target.checked)} />
          {t('customers.locations.defaultUnloadingBadge')}
        </label>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={defaultBilling} onChange={(e) => setDefaultBilling(e.target.checked)} />
          {t('customers.locations.defaultBillingBadge')}
        </label>
        <FormField label={t('customers.addresses.instructionsField')} htmlFor="cal-instructions">
          <textarea id="cal-instructions" value={instructions} onChange={(e) => setInstructions(e.target.value)} rows={3} maxLength={2000} />
        </FormField>
        <label className="customer-form-checkbox">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          {t('ui.statusBadges.active')}
        </label>
      </form>
    </Modal>
  )
}
