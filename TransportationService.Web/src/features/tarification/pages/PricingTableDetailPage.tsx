import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { formatDate } from '../../../utils/dates'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Tabs, TabPanel } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { AGREEMENT_STATUS_LABELS, AGREEMENT_STATUS_TONE, agreementSamenstelling, agreementStatus } from '../agreementStatus'
import { getPricingAgreement, updatePricingAgreement, type PricingAgreement } from '../api/pricingApi'
import { listSalesCategories, type SalesCategory } from '../../accounting/api/accountingApi'
import { agreementToInput } from '../agreementInputHelpers'
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

/** One-line orientation per tab: what lives here and where the rest is managed. */
function tabIntros(t: TranslateFn): Record<TabId, ReactNode> {
  return {
    regels: (
      <>
        {t('tarification.detail.tabIntroRules1')} <strong>{t('tarification.grid.colMinQuantity')}</strong>{' '}
        {t('tarification.detail.tabIntroRulesAnd')} <strong>{t('tarification.grid.colRoundingStep')}</strong>
        {t('tarification.detail.tabIntroRules2')} <strong>{t('tarification.grid.overrideAction')}</strong>{' '}
        {t('tarification.detail.tabIntroRules3')} <Link to="/settings/pricing">{t('tarification.detail.pricingSettingsLink')}</Link>.
      </>
    ),
    klanten: t('tarification.detail.tabIntroKlanten'),
    afleiding: t('tarification.detail.tabIntroAfleiding'),
    toeslagen: (
      <>
        {t('tarification.detail.tabIntroToeslagen1')}{' '}
        <Link to="/settings/pricing">{t('tarification.detail.tabIntroToeslagenLink')}</Link>.
      </>
    ),
    kortingen: t('tarification.detail.tabIntroKortingen'),
    aanpassing: t('tarification.detail.tabIntroAanpassing'),
    versies: t('tarification.detail.tabIntroVersies'),
  }
}

/**
 * A single rate table (pricing agreement): its rules (grid editor), customer assignments,
 * derivation config, automatic surcharges, bulk price adjustments (v2) and version management
 * (duplicate). The "used by N customers" banner warns before editing a shared table's rules.
 */
export function PricingTableDetailPage() {
  const { t } = useLocale()
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
  const [salesCategories, setSalesCategories] = useState<SalesCategory[]>([])
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
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
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('tarification.detail.loadError'))
  }, [id])

  useEffect(() => {
    reload()
    // Sales codes feed the table-level verkoopcategorie; unavailable is fine.
    listSalesCategories()
      .then(setSalesCategories)
      .catch(() => {})
  }, [reload])

  async function saveSalesCategory(salesCategoryId: string | null) {
    if (!agreement) return
    try {
      const updated = await updatePricingAgreement(agreement.id, {
        ...agreementToInput(agreement),
        salesCategoryId,
      })
      setAgreement(updated)
      showSuccess(t('tarification.detail.salesCategorySaved'))
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.detail.salesCategorySaveError')))
    }
  }

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
      showError(localizeApiError(t, err, t('tarification.detail.exportError')))
    }
  }

  if (!canView) return <ErrorState message={t('tarification.detail.noViewPermission')} />
  if (loadErrorKey) return <ErrorState message={t(loadErrorKey)} />
  if (!agreement || !id) return <LoadingState message={t('tarification.detail.loading')} />

  const showAfleiding = canManage || agreement.baseAgreementId !== null
  const status = agreementStatus(agreement)
  const composition = agreementSamenstelling(agreement)

  const tabs = [
    { id: 'regels', label: t('tarification.detail.tabRegels') },
    { id: 'klanten', label: t('tarification.detail.tabKlanten') },
    ...(showAfleiding ? [{ id: 'afleiding', label: t('tarification.detail.tabAfleiding') }] : []),
    { id: 'toeslagen', label: t('tarification.detail.tabToeslagen'), badge: agreement.surcharges.length || undefined },
    { id: 'kortingen', label: t('tarification.detail.tabKortingen') },
    { id: 'aanpassing', label: t('tarification.detail.tabAanpassing') },
    { id: 'versies', label: t('tarification.detail.tabVersies') },
  ]

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: t('tarification.common.pricing') },
          { label: t('tarification.tables.title'), to: '/pricing/tables' },
          { label: agreement.name },
        ]}
      />
      <BackButton to="/pricing/tables" label={t('tarification.detail.back')} />
      <PageHeader
        title={agreement.name}
        subtitle={
          <span className="pricing-table-header-meta">
            {formatDate(agreement.effectiveFrom)} — {agreement.effectiveUntil ? formatDate(agreement.effectiveUntil) : t('tarification.common.unlimited')}
            <Badge tone={AGREEMENT_STATUS_TONE[status]}>{t(AGREEMENT_STATUS_LABELS[status])}</Badge>
            <Badge tone="neutral">{t(composition.key, composition.params)}</Badge>
          </span>
        }
        action={
          <div className="pricing-table-header-actions">
            <Button variant="secondary" onClick={() => void handleExport()}>
              {t('tarification.detail.export')}
            </Button>
            {canImport && (
              <Button variant="secondary" onClick={() => setImportOpen(true)}>
                {t('tarification.detail.import')}
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
            showSuccess(
              t('tarification.importDialog.done', { added: result.added, updated: result.updated, removed: result.removed }),
            )
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
          {t('tarification.detail.sharedWarning', { count: agreement.customerCount })}
        </div>
      )}

      <AgreementValidationPanel agreementId={id} />

      <Tabs tabs={tabs} activeId={activeTab} onChange={(next) => setActiveTab(next as TabId)} />

      <p className="ui-form-section-description">{tabIntros(t)[activeTab]}</p>

      {activeTab === 'regels' && (
        <TabPanel tabId="regels">
          <div className="pricing-table-sales-category">
            <label htmlFor="table-sales-cat">{t('tarification.detail.defaultSalesCategory')}</label>
            <select
              id="table-sales-cat"
              value={agreement.salesCategoryId ?? ''}
              disabled={!canManage}
              onChange={(e) => void saveSalesCategory(e.target.value || null)}
            >
              <option value="">{t('tarification.detail.defaultTransportRole')}</option>
              {salesCategories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
            <span className="customer-form-muted">{t('tarification.detail.salesCategoryHint')}</span>
          </div>
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
