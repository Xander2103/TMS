import { useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { DashboardTab } from '../components/DashboardTab'
import { MessagesTab } from '../components/MessagesTab'
import { PartnersTab } from '../components/PartnersTab'
import { MappingsTab } from '../components/MappingsTab'
import { TestenTab } from '../components/TestenTab'
import './edi.css'

const TAB_IDS = ['dashboard', 'berichten', 'handelspartners', 'mappings', 'testen'] as const
type TabId = (typeof TAB_IDS)[number]

/**
 * EDI (corrections wave 4, Phase 10): inbound orders and outbound statuses per trading partner
 * — kept strictly separate from Peppol, which is a different integration module. Read access
 * (`edi.view`), replay (`edi.retry`) and test/validate (`edi.test`) are split from full partner
 * management (`edi.manage`); each tab hides itself — rather than render disabled — when the
 * caller lacks the permission its endpoints need.
 */
export function EdiPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const canView = hasPermission('edi.view') || hasPermission('edi.manage')
  const canManage = hasPermission('edi.manage')
  const canTest = hasPermission('edi.test') || canManage
  const canRetry = hasPermission('edi.retry') || canManage

  const tabs: TabItem[] = useMemo(() => {
    const list: TabItem[] = [
      { id: 'dashboard', label: t('edi.page.tabDashboard') },
      { id: 'berichten', label: t('edi.page.tabMessages') },
    ]
    if (canManage) {
      list.push({ id: 'handelspartners', label: t('edi.page.tabPartners') })
      list.push({ id: 'mappings', label: t('edi.page.tabMappings') })
    }
    if (canTest) list.push({ id: 'testen', label: t('edi.page.tabTest') })
    return list
  }, [canManage, canTest, t])

  const requestedTab = searchParams.get('tab')
  const tab: TabId =
    TAB_IDS.includes(requestedTab as TabId) && tabs.some((item) => item.id === requestedTab)
      ? (requestedTab as TabId)
      : 'dashboard'

  function setTab(next: string) {
    setSearchParams(next === 'dashboard' ? {} : { tab: next }, { replace: true })
  }

  if (!canView) {
    return <p className="placeholder-text">{t('edi.page.noAccess')}</p>
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('edi.page.title') }]} />
      <PageHeader
        title={t('edi.page.title')}
        subtitle={t('edi.page.subtitle')}
      />

      <Tabs tabs={tabs} activeId={tab} onChange={setTab} />

      {tab === 'dashboard' && (
        <TabPanel tabId="dashboard">
          <DashboardTab />
        </TabPanel>
      )}
      {tab === 'berichten' && (
        <TabPanel tabId="berichten">
          <MessagesTab canRetry={canRetry} />
        </TabPanel>
      )}
      {tab === 'handelspartners' && canManage && (
        <TabPanel tabId="handelspartners">
          <PartnersTab />
        </TabPanel>
      )}
      {tab === 'mappings' && canManage && (
        <TabPanel tabId="mappings">
          <MappingsTab />
        </TabPanel>
      )}
      {tab === 'testen' && canTest && (
        <TabPanel tabId="testen">
          <TestenTab canRetry={canRetry} />
        </TabPanel>
      )}
    </div>
  )
}
