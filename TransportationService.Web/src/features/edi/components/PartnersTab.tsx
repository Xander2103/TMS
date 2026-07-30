import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { listPartners, updatePartner, type EdiPartner } from '../api/ediApi'
import { PartnerModal } from './PartnerModal'

/** "Handelspartners" tab: partner administration table. Creation always goes through the
 * modal — never an inline form under the list. */
export function PartnersTab() {
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
      .catch(() => setLoadError('De handelspartners konden niet worden geladen.'))
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
      showSuccess(partner.isActive ? 'Partner gedeactiveerd.' : 'Partner geactiveerd.')
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De partnerstatus kon niet worden gewijzigd.').message)
    } finally {
      setBusy(false)
      setToggleTarget(null)
    }
  }

  const columns: Column<EdiPartner>[] = [
    { key: 'code', header: 'Code', render: (p) => <code>{p.code}</code> },
    { key: 'name', header: 'Naam', render: (p) => p.name },
    {
      key: 'customer',
      header: 'Gekoppelde klant',
      render: (p) => (p.customerName ? p.customerName : <Badge tone="warning">geen klant gekoppeld</Badge>),
    },
    { key: 'profile', header: 'Profiel', render: () => 'Generiek JSON' },
    { key: 'locations', header: 'Locatiemappings', align: 'right', render: (p) => p.locations.length },
    {
      key: 'status',
      header: 'Status',
      render: (p) => <Badge tone={p.isActive ? 'success' : 'neutral'}>{p.isActive ? 'Actief' : 'Inactief'}</Badge>,
    },
    {
      key: 'actions',
      header: 'Acties',
      render: (p) => (
        <span className="edi-row-actions">
          <Button variant="ghost" onClick={() => setEditing(p)}>
            Bewerken
          </Button>
          <Button variant="ghost" onClick={() => setToggleTarget(p)}>
            {p.isActive ? 'Deactiveren' : 'Activeren'}
          </Button>
        </span>
      ),
    },
  ]

  return (
    <div>
      <div className="edi-tab-toolbar">
        <Button onClick={() => setEditing('new')}>+ Nieuwe partner</Button>
      </div>

      <DataTable
        columns={columns}
        rows={partners ?? []}
        rowKey={(p) => p.id}
        isLoading={partners === null}
        error={loadError}
        emptyMessage="Nog geen handelspartners."
      />

      {editing && (
        <PartnerModal
          partner={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            showSuccess(editing === 'new' ? 'Partner toegevoegd.' : 'Partner bijgewerkt.')
            reload()
          }}
        />
      )}

      {toggleTarget && (
        <ConfirmDialog
          title={toggleTarget.isActive ? 'Partner deactiveren' : 'Partner activeren'}
          message={
            toggleTarget.isActive
              ? `'${toggleTarget.name}' deactiveren? Inkomende berichten van deze partner worden dan geweigerd.`
              : `'${toggleTarget.name}' opnieuw activeren?`
          }
          confirmLabel={toggleTarget.isActive ? 'Deactiveren' : 'Activeren'}
          destructive={toggleTarget.isActive}
          busy={busy}
          onConfirm={() => void toggleActive(toggleTarget)}
          onCancel={() => setToggleTarget(null)}
        />
      )}
    </div>
  )
}
