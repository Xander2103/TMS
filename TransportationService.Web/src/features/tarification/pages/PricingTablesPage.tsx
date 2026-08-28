import { useCallback, useEffect, useState } from 'react'
import { formatDate } from '../../../utils/dates'
import { useNavigate } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { AGREEMENT_STATUS_LABELS, AGREEMENT_STATUS_TONE, agreementSamenstelling, agreementStatus } from '../agreementStatus'
import { listAllPricingAgreements, type PricingAgreement } from '../api/pricingApi'
import { PricingTableWizard } from '../components/PricingTableWizard'

/**
 * Overview of every rate table (pricing agreement) across all customers — the "Tarieventabellen"
 * area. Row click opens the table's detail page; the wizard pre-creates skeleton rules for a
 * chosen calculation basis so a new table starts from a sensible template rather than blank.
 */
export function PricingTablesPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('tariffs.manage')
  const canImport = hasPermission('tariffs.import') || canManage

  const [agreements, setAgreements] = useState<PricingAgreement[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [showWizard, setShowWizard] = useState(false)

  const reload = useCallback(() => {
    listAllPricingAgreements()
      .then((data) => {
        setAgreements(data)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('tarification.tables.loadError'))
  }, [])

  useEffect(() => {
    reload()
  }, [reload])

  const columns: Column<PricingAgreement>[] = [
    {
      key: 'name',
      header: t('tarification.common.name'),
      render: (a) => (
        <>
          {a.name}
          {a.baseAgreementId && (
            <Badge tone="info"> {t('tarification.tables.derivedFrom', { name: a.baseAgreementName ?? '—' })}</Badge>
          )}
        </>
      ),
    },
    {
      key: 'samenstelling',
      header: t('tarification.tables.colComposition'),
      render: (a) => {
        const composition = agreementSamenstelling(a)
        return <Badge tone="neutral">{t(composition.key, composition.params)}</Badge>
      },
    },
    {
      key: 'validity',
      header: t('tarification.tables.colValidity'),
      render: (a) => `${formatDate(a.effectiveFrom)} — ${a.effectiveUntil ? formatDate(a.effectiveUntil) : t('tarification.common.unlimited')}`,
    },
    {
      key: 'status',
      header: t('tarification.common.status'),
      render: (a) => {
        const status = agreementStatus(a)
        return <Badge tone={AGREEMENT_STATUS_TONE[status]}>{t(AGREEMENT_STATUS_LABELS[status])}</Badge>
      },
    },
    {
      key: 'customers',
      header: t('tarification.tables.colCustomers'),
      render: (a) => t('tarification.tables.usedBy', { count: a.customerCount }),
    },
    { key: 'surcharges', header: t('tarification.tables.colSurcharges'), render: (a) => a.surcharges.length, align: 'right' },
    // Excel import lives on the table itself (a file always targets ONE table); the list
    // offers the entry point so nobody has to know that first.
    ...(canImport
      ? [
          {
            key: 'import',
            header: '',
            align: 'right' as const,
            render: (a: PricingAgreement) => (
              <Button
                variant="ghost"
                onClick={(event: React.MouseEvent) => {
                  event.stopPropagation()
                  navigate(`/pricing/tables/${a.id}?import=1`)
                }}
              >
                {t('tarification.importDialog.importFromList')}
              </Button>
            ),
          } satisfies Column<PricingAgreement>,
        ]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('tarification.common.pricing') }, { label: t('tarification.tables.title') }]} />
      <PageHeader
        title={t('tarification.tables.title')}
        subtitle={t('tarification.tables.subtitle')}
        action={canManage && <Button onClick={() => setShowWizard(true)}>{t('tarification.tables.newTable')}</Button>}
      />

      <DataTable
        columns={columns}
        rows={agreements ?? []}
        rowKey={(a) => a.id}
        isLoading={agreements === null && !loadErrorKey}
        error={loadErrorKey ? t(loadErrorKey) : null}
        emptyMessage={t('tarification.tables.empty')}
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
