import { useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useAuth } from '../../auth/authContextValue'
import { CustomerOverridesTab } from '../components/CustomerOverridesTab'
import { EventsTab } from '../components/EventsTab'
import { OutboxTab } from '../components/OutboxTab'
import { RecipientsInfoTab } from '../components/RecipientsInfoTab'
import { TemplatesTab } from '../components/TemplatesTab'
import './notification-admin.css'

const TAB_IDS = ['gebeurtenissen', 'sjablonen', 'ontvangers', 'klantafwijkingen', 'controle', 'verzonden', 'mislukt'] as const
type TabId = (typeof TAB_IDS)[number]

/**
 * "Meldingen en e-mails" admin screen (corrections wave 4, Phase 7): configures which events
 * trigger a notification, to whom, on which channel, plus the templates and outbox behind them.
 * Gated on `notification_rules.view`; individual tabs additionally require the permission that
 * their underlying endpoints require (`message_templates.manage`, `messaging.manage`) and hide
 * themselves — rather than render disabled — when the user lacks it, same as the nav item.
 */
export function NotificationAdminPage() {
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()

  const canView = hasPermission('notification_rules.view')
  const canManageRules = hasPermission('notification_rules.manage')
  const canTemplates = hasPermission('message_templates.manage')
  const canMessaging = hasPermission('messaging.manage')

  const tabs: TabItem[] = useMemo(() => {
    const list: TabItem[] = [{ id: 'gebeurtenissen', label: 'Gebeurtenissen' }]
    if (canTemplates) list.push({ id: 'sjablonen', label: 'Sjablonen' })
    list.push({ id: 'ontvangers', label: 'Ontvangers' })
    list.push({ id: 'klantafwijkingen', label: 'Klantafwijkingen' })
    if (canMessaging) {
      list.push({ id: 'controle', label: 'Wacht op controle' })
      list.push({ id: 'verzonden', label: 'Verzonden berichten' })
      list.push({ id: 'mislukt', label: 'Mislukte berichten' })
    }
    return list
  }, [canTemplates, canMessaging])

  const requestedTab = searchParams.get('tab')
  const tab: TabId =
    TAB_IDS.includes(requestedTab as TabId) && tabs.some((t) => t.id === requestedTab)
      ? (requestedTab as TabId)
      : 'gebeurtenissen'

  function setTab(next: string) {
    setSearchParams(next === 'gebeurtenissen' ? {} : { tab: next }, { replace: true })
  }

  if (!canView) {
    return <p className="placeholder-text">Je hebt geen rechten om meldingen en e-mails te beheren.</p>
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Meldingen en e-mails' }]} />
      <PageHeader
        title="Meldingen en e-mails"
        subtitle="Wie wordt wanneer verwittigd — per gebeurtenis, per klant, en met welke tekst."
      />

      <Tabs tabs={tabs} activeId={tab} onChange={setTab} />

      {tab === 'gebeurtenissen' && (
        <TabPanel tabId="gebeurtenissen">
          <EventsTab canManage={canManageRules} />
        </TabPanel>
      )}
      {tab === 'sjablonen' && canTemplates && (
        <TabPanel tabId="sjablonen">
          <TemplatesTab canManage={canTemplates} />
        </TabPanel>
      )}
      {tab === 'ontvangers' && (
        <TabPanel tabId="ontvangers">
          <RecipientsInfoTab />
        </TabPanel>
      )}
      {tab === 'klantafwijkingen' && (
        <TabPanel tabId="klantafwijkingen">
          <CustomerOverridesTab canManage={canManageRules} />
        </TabPanel>
      )}
      {tab === 'controle' && canMessaging && (
        <TabPanel tabId="controle">
          <OutboxTab variant="review" />
        </TabPanel>
      )}
      {tab === 'verzonden' && canMessaging && (
        <TabPanel tabId="verzonden">
          <OutboxTab variant="sent" />
        </TabPanel>
      )}
      {tab === 'mislukt' && canMessaging && (
        <TabPanel tabId="mislukt">
          <OutboxTab variant="failed" includeSuppressedToggle />
        </TabPanel>
      )}
    </div>
  )
}
