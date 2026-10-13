import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { listPartners, updatePartner, type EdiPartner } from '../api/ediApi'
import { PartnerModal } from './PartnerModal'

/** "Handelspartners" tab: partner administration table. Creation always goes through the
 * modal — never an inline form under the list. */
export function PartnersTab() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [partners, setPartners] = useState<EdiPartner[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState<EdiPartner | null | 'new'>(null)
  const [toggleTarget, setToggleTarget] = useState<EdiPartner | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listPartners()
      .then((data) => {
        setPartners(data)
        setLoadError(null)
      })
      .catch(() => setLoadError(t('edi.partners.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  async function toggleActive(partner: EdiPartner) {
    setBusy(true)
    try {
      await updatePartner(partner.id, {
        name: partner.name,
        customerId: partner.customerId,
        externalCustomerIdentifier: partner.externalCustomerIdentifier,
        mappingProfile: partner.mappingProfile,
        isActive: !partner.isActive,
        notes: partner.notes,
      })
      showSuccess(partner.isActive ? t('edi.partners.deactivated') : t('edi.partners.activated'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.partners.toggleFailed')))
    } finally {
      setBusy(false)
      setToggleTarget(null)
    }
  }

  const columns: Column<EdiPartner>[] = [
    { key: 'code', header: t('edi.partners.codeHeader'), render: (p) => <code>{p.code}</code> },
    { key: 'name', header: t('edi.partners.nameHeader'), render: (p) => p.name },
    {
      key: 'customer',
      header: t('edi.partners.customerHeader'),
      render: (p) => (p.customerName ? p.customerName : <Badge tone="warning">{t('edi.partners.noCustomer')}</Badge>),
    },
    { key: 'profile', header: t('edi.partners.profileHeader'), render: () => t('edi.partners.profileGeneric') },
    { key: 'locations', header: t('edi.partners.mappingsHeader'), align: 'right', render: (p) => p.locations.length },
    {
      key: 'status',
      header: t('edi.partners.statusHeader'),
      render: (p) => <Badge tone={p.isActive ? 'success' : 'neutral'}>{p.isActive ? t('edi.partners.active') : t('edi.partners.inactive')}</Badge>,
    },
    {
      key: 'actions',
      header: t('edi.partners.actionsHeader'),
      render: (p) => (
        <span className="edi-row-actions">
          <Button variant="ghost" onClick={() => setEditing(p)}>
            {t('edi.partners.edit')}
          </Button>
          <Button variant="ghost" onClick={() => setToggleTarget(p)}>
            {p.isActive ? t('edi.partners.deactivate') : t('edi.partners.activate')}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <div>
      <div className="edi-tab-toolbar">
        <Button onClick={() => setEditing('new')}>{t('edi.partners.new')}</Button>
      </div>

      <DataTable
        columns={columns}
        rows={partners ?? []}
        rowKey={(p) => p.id}
        isLoading={partners === null}
        error={loadError}
        emptyMessage={t('edi.partners.empty')}
      />

      {editing && (
        <PartnerModal
          partner={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            showSuccess(editing === 'new' ? t('edi.partners.added') : t('edi.partners.updated'))
            reload()
          }}
        />
      )}

      {toggleTarget && (
        <ConfirmDialog
          title={toggleTarget.isActive ? t('edi.partners.deactivateTitle') : t('edi.partners.activateTitle')}
          message={
            toggleTarget.isActive
              ? t('edi.partners.deactivateMessage', { name: toggleTarget.name })
              : t('edi.partners.activateMessage', { name: toggleTarget.name })
          }
          confirmLabel={toggleTarget.isActive ? t('edi.partners.deactivate') : t('edi.partners.activate')}
          destructive={toggleTarget.isActive}
          busy={busy}
          onConfirm={() => void toggleActive(toggleTarget)}
          onCancel={() => setToggleTarget(null)}
        />
      )}
    </div>
  )
}
