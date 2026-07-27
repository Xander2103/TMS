import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useAuth } from '../../auth/authContextValue'
import { AGREEMENT_STATUS_TONE, agreementSamenstelling, agreementStatus } from '../agreementStatus'
import { listAllPricingAgreements, type PricingAgreement } from '../api/pricingApi'
import { PricingTableWizard } from '../components/PricingTableWizard'

/**
 * Overview of every rate table (pricing agreement) across all customers — the "Tarieventabellen"
 * area. Row click opens the table's detail page; the wizard pre-creates skeleton rules for a
 * chosen calculation basis so a new table starts from a sensible template rather than blank.
 */
export function PricingTablesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('tariffs.manage')

  const [agreements, setAgreements] = useState<PricingAgreement[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [showWizard, setShowWizard] = useState(false)

  const reload = useCallback(() => {
    listAllPricingAgreements()
      .then((data) => {
        setAgreements(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De tarieventabellen konden niet worden geladen.'))
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<PricingAgreement>[] = [
    {
      key: 'name',
      header: 'Naam',
      render: (a) => (
        <>
          {a.name}
          {a.baseAgreementId && <Badge tone="info"> Afgeleid van {a.baseAgreementName ?? '—'}</Badge>}
        </>
      ),
    },
    { key: 'samenstelling', header: 'Samenstelling', render: (a) => <Badge tone="neutral">{agreementSamenstelling(a)}</Badge> },
    {
      key: 'validity',
      header: 'Geldigheid',
      render: (a) => `${a.effectiveFrom} — ${a.effectiveUntil ?? 'onbeperkt'}`,
    },
    {
      key: 'status',
      header: 'Status',
      render: (a) => <Badge tone={AGREEMENT_STATUS_TONE[agreementStatus(a)]}>{agreementStatus(a)}</Badge>,
    },
    { key: 'customers', header: 'Klanten', render: (a) => `Gebruikt door ${a.customerCount} klant${a.customerCount === 1 ? '' : 'en'}` },
    { key: 'surcharges', header: 'Toeslagen', render: (a) => a.surcharges.length, align: 'right' },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Prijzen' }, { label: 'Tarieventabellen' }]} />
      <PageHeader
        title="Tarieventabellen"
        subtitle="Alle prijsafspraken (tarieventabellen): gedeelde, algemene en klantspecifieke tabellen."
        action={canManage && <Button onClick={() => setShowWizard(true)}>+ Nieuwe tarieventabel</Button>}
      />

      <DataTable
        columns={columns}
        rows={agreements ?? []}
        rowKey={(a) => a.id}
        isLoading={agreements === null && !loadError}
        error={loadError}
        emptyMessage="Nog geen tarieventabellen. Maak er één aan om te starten."
        onRowClick={(a) => navigate(`/pricing/tables/${a.id}`)}
      />

      {showWizard && (
        <PricingTableWizard
          onClose={() => setShowWizard(false)}
          onCreated={(id, openImport) => {
            setShowWizard(false)
            navigate(openImport ? `/pricing/tables/${id}?import=1` : `/pricing/tables/${id}`)
          }}
        />
      )}
    </div>
  )
}
