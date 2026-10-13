import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NotificationAdminPage } from '../pages/NotificationAdminPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../components/EventsTab', () => ({ EventsTab: () => <div>EventsTabStub</div> }))
vi.mock('../components/TemplatesTab', () => ({ TemplatesTab: () => <div>TemplatesTabStub</div> }))
vi.mock('../components/RecipientsInfoTab', () => ({ RecipientsInfoTab: () => <div>RecipientsInfoStub</div> }))
vi.mock('../components/CustomerOverridesTab', () => ({ CustomerOverridesTab: () => <div>CustomerOverridesStub</div> }))
vi.mock('../components/OutboxTab', () => ({ OutboxTab: () => <div>OutboxStub</div> }))

function renderPage(initialEntry = '/settings/notifications') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <NotificationAdminPage />
    </MemoryRouter>,
  )
}

describe('NotificationAdminPage', () => {
  beforeEach(() => {
    auth.permissions = new Set()
  })

  it('shows a permission placeholder without notification_rules.view', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om meldingen en e-mails te beheren.')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('with only notification_rules.view: shows Gebeurtenissen/Ontvangers/Klantafwijkingen but not Sjablonen/Verzonden/Mislukt', () => {
    auth.permissions = new Set(['notification_rules.view'])
    renderPage()

    expect(screen.getByRole('tab', { name: 'Gebeurtenissen' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Ontvangers' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Klantafwijkingen' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Sjablonen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Verzonden berichten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Mislukte berichten' })).not.toBeInTheDocument()
  })

  it('adds the Sjablonen tab with message_templates.manage', () => {
    auth.permissions = new Set(['notification_rules.view', 'message_templates.manage'])
    renderPage()
    expect(screen.getByRole('tab', { name: 'Sjablonen' })).toBeInTheDocument()
  })

  it('adds the outbox tabs with messaging.manage', () => {
    auth.permissions = new Set(['notification_rules.view', 'messaging.manage'])
    renderPage()
    expect(screen.getByRole('tab', { name: 'Verzonden berichten' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Mislukte berichten' })).toBeInTheDocument()
  })

  it('falls back to Gebeurtenissen when ?tab= names a tab the user cannot see', () => {
    auth.permissions = new Set(['notification_rules.view'])
    renderPage('/settings/notifications?tab=sjablonen')

    expect(screen.getByRole('tab', { name: 'Gebeurtenissen', selected: true })).toBeInTheDocument()
    expect(screen.getByText('EventsTabStub')).toBeInTheDocument()
  })

  it('honours a valid ?tab= deep link', () => {
    auth.permissions = new Set(['notification_rules.view'])
    renderPage('/settings/notifications?tab=klantafwijkingen')

    expect(screen.getByRole('tab', { name: 'Klantafwijkingen', selected: true })).toBeInTheDocument()
    expect(screen.getByText('CustomerOverridesStub')).toBeInTheDocument()
  })
})
