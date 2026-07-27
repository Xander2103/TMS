import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import { AGREEMENT_STATUS_TONE, agreementSamenstelling, agreementStatus } from '../agreementStatus'
import { getPricingAgreement, type PricingAgreement } from '../api/pricingApi'
import { downloadAgreementExport } from '../api/pricingImportApi'
import { AgreementAdjustmentsPanel } from '../components/AgreementAdjustmentsPanel'
import { AgreementAssignmentsPanel } from '../components/AgreementAssignmentsPanel'
import { AgreementDerivationPanel } from '../components/AgreementDerivationPanel'
import { AgreementSurchargesPanel } from '../components/AgreementSurchargesPanel'
import { AgreementValidationPanel } from '../components/AgreementValidationPanel'
import { AgreementVersionsPanel } from '../components/AgreementVersionsPanel'
import { CombinedDiscountsPanel } from '../components/CombinedDiscountsPanel'
import { PricingImportDialog } from '../components/PricingImportDialog'
import { RuleGridEditor } from '../components/RuleGridEditor'
import './../components/pricingTableDetail.css'

type TabId = 'regels' | 'klanten' | 'afleiding' | 'toeslagen' | 'kortingen' | 'aanpassing' | 'versies'

/**
 * A single rate table (pricing agreement): its rules (grid editor), customer assignments,
 * derivation config, automatic surcharges, bulk price adjustments (v2) and version management
 * (duplicate). The "used by N customers" banner warns before editing a shared table's rules.
 */
export function PricingTableDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('tariffs.manage')
  const canImport = hasPermission('tariffs.import') || canManage
  // A role holding only tariffs.import must still be able to reach the import feature it was
  // granted — gating the whole page on view/manage alone would lock it out.
  const canView = hasPermission('tariffs.view') || canManage || canImport

  const [agreement, setAgreement] = useState<PricingAgreement | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<TabId>('regels')
  // The "Excel-import" wizard card creates an empty table and lands here with ?import=1 so the
  // import dialog opens immediately on the fresh table. Computed once at mount (not in an
  // effect) — closing the dialog strips the param so a later refresh won't reopen it.
  const [importOpen, setImportOpen] = useState(() => searchParams.get('import') === '1' && canImport)

  const reload = useCallback(() => {
    if (!id) return
    getPricingAgreement(id)
      .then((data) => {
        setAgreement(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('De tarieventabel kon niet worden geladen.'))
  }, [id])

  useEffect(() => {
    reload()
  }, [reload])

  function closeImportDialog() {
    setImportOpen(false)
    if (searchParams.has('import')) {
      searchParams.delete('import')
      setSearchParams(searchParams, { replace: true })
    }
  }

  async function handleExport() {
    if (!agreement) return
    try {
      await downloadAgreementExport(agreement.id, agreement.name)
    } catch (err) {
      showError(describeApiError(err, 'De tabel kon niet worden geëxporteerd.').message)
    }
  }

  if (!canView) return <ErrorState message="Je hebt geen rechten om tarieventabellen te bekijken." />
  if (loadError) return <ErrorState message={loadError} />
  if (!agreement || !id) return <LoadingState message="Tarieventabel laden…" />

  const showAfleiding = canManage || agreement.baseAgreementId !== null

  const tabs = [
    { id: 'regels', label: 'Regels' },
    { id: 'klanten', label: 'Klanten' },
    ...(showAfleiding ? [{ id: 'afleiding', label: 'Afleiding' }] : []),
    { id: 'toeslagen', label: 'Toeslagen', badge: agreement.surcharges.length || undefined },
    { id: 'kortingen', label: 'Kortingen' },
    { id: 'aanpassing', label: 'Prijsaanpassing' },
    { id: 'versies', label: 'Versies' },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Prijzen' }, { label: 'Tarieventabellen', to: '/pricing/tables' }, { label: agreement.name }]} />
      <BackButton to="/pricing/tables" label="Terug naar tarieventabellen" />
      <PageHeader
        title={agreement.name}
        subtitle={
          <span className="pricing-table-header-meta">
            {agreement.effectiveFrom} — {agreement.effectiveUntil ?? 'onbeperkt'}
            <Badge tone={AGREEMENT_STATUS_TONE[agreementStatus(agreement)]}>{agreementStatus(agreement)}</Badge>
            <Badge tone="neutral">{agreementSamenstelling(agreement)}</Badge>
          </span>
        }
        action={
          <div className="pricing-table-header-actions">
            <Button variant="secondary" onClick={() => void handleExport()}>
              Exporteren
            </Button>
            {canImport && (
              <Button variant="secondary" onClick={() => setImportOpen(true)}>
                Importeren
              </Button>
            )}
          </div>
        }
      />

      {importOpen && (
        <PricingImportDialog
          agreementId={id}
          agreementName={agreement.name}
          onClose={closeImportDialog}
          onImported={(result) => {
            showSuccess(`Import klaar: ${result.added} toegevoegd, ${result.updated} gewijzigd, ${result.removed} verwijderd.`)
            closeImportDialog()
            if (result.agreementId !== id) {
              navigate(`/pricing/tables/${result.agreementId}`)
            } else {
              reload()
            }
          }}
        />
      )}

      {agreement.customerCount > 0 && (
        <div className="pricing-table-warning" role="alert">
          Deze tabel wordt gebruikt door {agreement.customerCount} klant{agreement.customerCount === 1 ? '' : 'en'}.
          Wijzigingen gelden voor al deze klanten — maak bij twijfel een nieuwe versie.
        </div>
      )}

      <AgreementValidationPanel agreementId={id} />

      <Tabs tabs={tabs} activeId={activeTab} onChange={(next) => setActiveTab(next as TabId)} />

      {activeTab === 'regels' && (
        <TabPanel tabId="regels">
          <RuleGridEditor agreementId={id} agreementCustomerId={agreement.customerId} canManage={canManage} />
        </TabPanel>
      )}

      {activeTab === 'klanten' && (
        <TabPanel tabId="klanten">
          <AgreementAssignmentsPanel agreementId={id} isShared={agreement.isShared} canManage={canManage} />
        </TabPanel>
      )}

      {activeTab === 'afleiding' && showAfleiding && (
        <TabPanel tabId="afleiding">
          <AgreementDerivationPanel agreement={agreement} canManage={canManage} onUpdated={setAgreement} />
        </TabPanel>
      )}

      {activeTab === 'toeslagen' && (
        <TabPanel tabId="toeslagen">
          <AgreementSurchargesPanel agreement={agreement} canManage={canManage} onUpdated={setAgreement} />
        </TabPanel>
      )}

      {activeTab === 'kortingen' && (
        <TabPanel tabId="kortingen">
          <CombinedDiscountsPanel agreementId={id} />
        </TabPanel>
      )}

      {activeTab === 'aanpassing' && (
        <TabPanel tabId="aanpassing">
          <AgreementAdjustmentsPanel agreementId={id} canManage={canManage} />
        </TabPanel>
      )}

      {activeTab === 'versies' && (
        <TabPanel tabId="versies">
          <AgreementVersionsPanel
            agreement={agreement}
            canManage={canManage}
            onDuplicated={(newId) => navigate(`/pricing/tables/${newId}`)}
          />
        </TabPanel>
      )}
    </div>
  )
}
