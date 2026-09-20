import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { NotFoundPage } from '../../../components/feedback/NotFoundPage'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { LookupManager } from '../../master-data/components/LookupManager'
import { findLookupResource } from '../../master-data/lookupRegistry'
import { IssuedItemTemplatesPanel } from '../components/IssuedItemTemplatesPanel'
import '../issued-items.css'

type AdminTab = 'categories' | 'templates'

const ADMIN_TABS: AdminTab[] = ['categories', 'templates']

function isAdminTab(value: string | undefined): value is AdminTab {
  return value !== undefined && (ADMIN_TABS as string[]).includes(value)
}

/**
 * "Bedrijfsmiddelen" under Personeel: categories and templates side by side, since the
 * categories only exist to group the templates. Each tab is a sub-route
 * (/issued-items/categories · /issued-items/templates) so deep links and back/forward work.
 * The UI gates per tab; the backend enforces the same permissions on every call.
 */
export function IssuedItemsAdminPage() {
  const { tab } = useParams<{ tab: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManageTemplates = hasPermission('issued_items.manage_templates')
  const canManageCategories = hasPermission('inventory.manage')
  const categoriesConfig = findLookupResource('issued-item-categories')

  if (!isAdminTab(tab)) {
    return <NotFoundPage />
  }

  const tabs: TabItem[] = [
    ...(canManageCategories ? [{ id: 'categories', label: t('issuedItems.admin.tabCategories') }] : []),
    ...(canManageTemplates ? [{ id: 'templates', label: t('issuedItems.admin.tabTemplates') }] : []),
  ]

  const header = (
    <>
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.personeel') }, { label: t('issuedItems.admin.title') }]} />
      <PageHeader title={t('issuedItems.admin.title')} subtitle={t('issuedItems.admin.subtitle')} />
    </>
  )

  if (tabs.length === 0) {
    return (
      <div>
        {header}
        <p className="placeholder-text">{t('issuedItems.admin.noPermission')}</p>
      </div>
    )
  }

  // A tab the user may not open (deep link, revoked right) lands on the first permitted one.
  if (!tabs.some((item) => item.id === tab)) {
    return <Navigate to={`/issued-items/${tabs[0].id}`} replace />
  }

  return (
    <div>
      {header}
      <Tabs tabs={tabs} activeId={tab} onChange={(id) => navigate(`/issued-items/${id}`)} />

      {tab === 'categories' && categoriesConfig && (
        <TabPanel tabId="categories">
          {/* LookupManager brings its own header; the wrapper tones it down to a section inside this page. */}
          <div className="issued-items-embedded-lookup">
            <LookupManager key={categoriesConfig.slug} config={categoriesConfig} />
          </div>
        </TabPanel>
      )}

      {tab === 'templates' && (
        <TabPanel tabId="templates">
          <IssuedItemTemplatesPanel />
        </TabPanel>
      )}
    </div>
  )
}
