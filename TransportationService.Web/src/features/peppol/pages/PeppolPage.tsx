import { useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { OverviewTab } from '../components/OverviewTab'
import { OutgoingTab } from '../components/OutgoingTab'
import { IncomingTab } from '../components/IncomingTab'
import { SettingsTab } from '../components/SettingsTab'
import { ValidationTab } from '../components/ValidationTab'
import './peppol.css'

const TAB_IDS = ['overzicht', 'uitgaand', 'inkomend', 'configuratie', 'validatie'] as const
type TabId = (typeof TAB_IDS)[number]

/**
 * Peppol (Phase 13): e-facturatie-lifecycle — configuratie per eigen bedrijf, uitgaande
 * transmissies met retry/annulering, inkomende documenten en validatieproblemen. Bewust
 * gescheiden van EDI (ordersuitwisseling per handelspartner): Peppol is het wettelijke
 * e-invoicing-kanaal. De Inkomend-tab verbergt zich zonder peppol.view_incoming.
 */
export function PeppolPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const canView = hasPermission('peppol.view')
  const canViewIncoming = hasPermission('peppol.view_incoming')
  const canConfigure = hasPermission('peppol.configure')
  const canValidate = hasPermission('peppol.validate')
  const canRetry = hasPermission('peppol.retry')
  const canSend = hasPermission('peppol.send')

  const tabs: TabItem[] = useMemo(() => {
    const list: TabItem[] = [
      { id: 'overzicht', label: t('peppol.page.tabs.overview') },
      { id: 'uitgaand', label: t('peppol.page.tabs.outgoing') },
    ]
    if (canViewIncoming) list.push({ id: 'inkomend', label: t('peppol.page.tabs.incoming') })
    list.push({ id: 'configuratie', label: t('peppol.page.tabs.settings') })
    list.push({ id: 'validatie', label: t('peppol.page.tabs.validation') })
    return list
  }, [canViewIncoming, t])

  const requestedTab = searchParams.get('tab')
  const tab: TabId =
    TAB_IDS.includes(requestedTab as TabId) && tabs.some((t) => t.id === requestedTab)
      ? (requestedTab as TabId)
      : 'overzicht'

  function setTab(next: string) {
    setSearchParams(next === 'overzicht' ? {} : { tab: next }, { replace: true })
  }

  if (!canView) {
    return <p className="placeholder-text">{t('peppol.page.noPermission')}</p>
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('peppol.page.title') }]} />
      <PageHeader title={t('peppol.page.title')} subtitle={t('peppol.page.subtitle')} />

      <Tabs tabs={tabs} activeId={tab} onChange={setTab} />

      {tab === 'overzicht' && (
        <TabPanel tabId="overzicht">
          <OverviewTab />
        </TabPanel>
      )}
      {tab === 'uitgaand' && (
        <TabPanel tabId="uitgaand">
          <OutgoingTab canRetry={canRetry} canCancel={canSend} />
        </TabPanel>
      )}
      {tab === 'inkomend' && canViewIncoming && (
        <TabPanel tabId="inkomend">
          <IncomingTab />
        </TabPanel>
      )}
      {tab === 'configuratie' && (
        <TabPanel tabId="configuratie">
          <SettingsTab canConfigure={canConfigure} canValidate={canValidate} />
        </TabPanel>
      )}
      {tab === 'validatie' && (
        <TabPanel tabId="validatie">
          <ValidationTab />
        </TabPanel>
      )}
    </div>
  )
}
