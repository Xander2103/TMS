import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerContactsPanel } from '../components/CustomerContactsPanel'
import { CustomerCommunicationPanel } from '../components/CustomerCommunicationPanel'
import type { CustomerContact } from '../types'
import type { NotificationOverviewLine } from '../api/customerNotificationsApi'

/**
 * Sprint 3 — the normal user answers "who receives what?" on the contact, and reads it back
 * per notification type. Raw event codes, CC addresses and fallback contacts stay advanced.
 */

const auth = vi.hoisted(() => ({ permissions: ['customers.view', 'customers.manage_communication'] }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))
const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))
vi.mock('../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))

const api = vi.hoisted(() => ({
  options: vi.fn(),
  get: vi.fn(),
  set: vi.fn(),
  overview: vi.fn(),
}))
vi.mock('../api/customerNotificationsApi', async (orig) => ({
  ...(await orig<typeof import('../api/customerNotificationsApi')>()),
  getNotificationOptions: () => api.options(),
  getContactNotifications: (...a: unknown[]) => api.get(...a),
  setContactNotifications: (...a: unknown[]) => api.set(...a),
  getNotificationOverview: (...a: unknown[]) => api.overview(...a),
}))

vi.mock('../api/customerCommunicationApi', () => ({
  listCommunicationRules: () => Promise.resolve([]),
  createCommunicationRule: vi.fn(),
  updateCommunicationRule: vi.fn(),
  deleteCommunicationRule: vi.fn(),
}))

const OPTIONS = [
  { key: 'order-confirmation', group: 'Transport' as const },
  { key: 'planning', group: 'Transport' as const },
  { key: 'eta', group: 'Transport' as const },
  { key: 'invoice', group: 'Facturatie' as const },
  { key: 'general', group: 'Algemeen' as const },
]

function contact(overrides: Partial<CustomerContact> = {}): CustomerContact {
  return {
    id: 'ct-1',
    firstName: 'Jan',
    lastName: 'Peeters',
    displayName: null,
    nickname: null,
    role: null,
    contactType: 'Algemeen',
    departmentId: null,
    departmentName: null,
    email: 'jan@example.com',
    phoneNumber: null,
    mobilePhone: null,
    preferredLanguageCode: null,
    isPrimary: false,
    isActive: true,
    notes: null,
    ...overrides,
  } as CustomerContact
}

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = ['customers.view', 'customers.manage_communication']
  api.options.mockResolvedValue(OPTIONS)
  api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning'] })
  api.set.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
  api.overview.mockResolvedValue([])
})

function renderContacts(contacts: CustomerContact[] = [contact()]) {
  return render(
    <CustomerContactsPanel
      customerId="c1"
      contacts={contacts}
      isSubmitting={false}
      onAdd={vi.fn().mockResolvedValue(contact({ id: 'new-1' }))}
      onUpdate={vi.fn().mockResolvedValue(true)}
      onRemove={vi.fn().mockResolvedValue(true)}
    />,
  )
}

describe('contact — Ontvangt meldingen', () => {
  it('offers the business options grouped, not raw event codes', async () => {
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: '+ Contact toevoegen' }))

    const dialog = await screen.findByRole('dialog')
    // Scoped to the section: "Facturatie" is also a contact TYPE in the dropdown above.
    const section = within(dialog).getByRole('group', { name: 'Ontvangt meldingen' })
    expect(within(section).getByText('Transport')).toBeInTheDocument()
    expect(within(section).getByText('Facturatie')).toBeInTheDocument()
    expect(within(section).getByLabelText('Planning / levervenster')).toBeInTheDocument()
    expect(within(section).getByLabelText('Facturen')).toBeInTheDocument()
    // No routing vocabulary anywhere on the normal surface.
    expect(within(dialog).queryByText(/PlanningAlert|EtaUpdate|fallback/i)).not.toBeInTheDocument()
  })

  it('preloads what an existing contact already receives and saves the change', async () => {
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    expect(within(dialog).getByLabelText('ETA / vertraging')).not.toBeChecked()

    await userEvent.click(within(dialog).getByLabelText('ETA / vertraging'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.set).toHaveBeenCalledTimes(1))
    expect(api.set).toHaveBeenCalledWith('c1', 'ct-1', expect.arrayContaining(['planning', 'eta']))
  })

  it('hides the section without customers.manage_communication and never writes routing', async () => {
    auth.permissions = ['customers.view']
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByRole('group', { name: 'Ontvangt meldingen' })).not.toBeInTheDocument()
    expect(api.options).not.toHaveBeenCalled()
    expect(api.get).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.set).not.toHaveBeenCalled()
  })

  it('does not rewrite the routing when the boxes were left as preloaded', async () => {
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.set).not.toHaveBeenCalled()
  })

  it('surfaces a failed routing update instead of swallowing it', async () => {
    api.set.mockRejectedValue(new Error('boom'))
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    await userEvent.click(within(dialog).getByLabelText('ETA / vertraging'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.set).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(toast.showError).toHaveBeenCalledTimes(1))
    // describeApiError keeps the server's own message when there is one.
    expect(toast.showError).toHaveBeenCalledWith('boom')
  })

  it('keeps a stored language outside the offered list so a save does not wipe it', async () => {
    const onUpdate = vi.fn().mockResolvedValue(true)
    render(
      <CustomerContactsPanel
        customerId="c1"
        contacts={[contact({ preferredLanguageCode: 'it' })]}
        isSubmitting={false}
        onAdd={vi.fn()}
        onUpdate={onUpdate}
        onRemove={vi.fn()}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    const select = within(dialog).getByLabelText('Voorkeurstaal') as HTMLSelectElement
    expect(select).toHaveValue('it')
    expect(within(select).getByRole('option', { name: 'Andere: it' })).toBeInTheDocument()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onUpdate).toHaveBeenCalledTimes(1))
    expect(onUpdate).toHaveBeenCalledWith('ct-1', expect.objectContaining({ preferredLanguageCode: 'it' }))
  })

  it('offers the four supported languages as a dropdown, never a locale code', async () => {
    renderContacts()
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    const select = within(dialog).getByLabelText('Voorkeurstaal')
    expect(select.tagName).toBe('SELECT')
    expect(within(select as HTMLSelectElement).getByRole('option', { name: 'Nederlands' })).toBeInTheDocument()
    expect(within(select as HTMLSelectElement).getByRole('option', { name: 'Français' })).toBeInTheDocument()
    expect(within(select as HTMLSelectElement).getByRole('option', { name: 'English' })).toBeInTheDocument()
    expect(within(select as HTMLSelectElement).getByRole('option', { name: 'Deutsch' })).toBeInTheDocument()
  })
})

describe('customer communication overview', () => {
  function overviewLine(overrides: Partial<NotificationOverviewLine> = {}): NotificationOverviewLine {
    return {
      optionKey: 'planning',
      group: 'Transport',
      recipients: [
        { contactId: 'ct-1', name: 'Jan Peeters', email: 'jan@example.com', isAdvanced: false, isActive: true },
        { contactId: 'ct-2', name: 'Sofie Janssens', email: 'sofie@example.com', isAdvanced: false, isActive: true },
      ],
      ...overrides,
    }
  }

  it('lists every recipient of a notification type', async () => {
    api.overview.mockResolvedValue([overviewLine()])
    render(<CustomerCommunicationPanel customerId="c1" contacts={[]} />)

    expect(await screen.findByText('Planning / levervenster')).toBeInTheDocument()
    expect(screen.getByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByText('Sofie Janssens')).toBeInTheDocument()
  })

  it('hides CC/fallback routing until it is asked for', async () => {
    api.overview.mockResolvedValue([
      overviewLine({
        recipients: [
          { contactId: 'ct-1', name: 'Jan Peeters', email: 'jan@example.com', isAdvanced: false, isActive: true },
          { contactId: null, name: 'cc@klant.be', email: 'cc@klant.be', isAdvanced: true, isActive: true },
        ],
      }),
    ])
    render(<CustomerCommunicationPanel customerId="c1" contacts={[]} />)

    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.queryByText('cc@klant.be')).not.toBeInTheDocument()

    await userEvent.click(screen.getByLabelText('Toon CC-adressen en terugvalcontacten'))
    expect(await screen.findByText('cc@klant.be')).toBeInTheDocument()
  })
})

describe('contact — meldingen vereisen een e-mailadres', () => {
  it('blocks saving a contact that should receive notifications but has no e-mail address', async () => {
    const onAdd = vi.fn().mockResolvedValue(contact({ id: 'new-1' }))
    render(
      <CustomerContactsPanel
        customerId="c1"
        contacts={[]}
        isSubmitting={false}
        onAdd={onAdd}
        onUpdate={vi.fn().mockResolvedValue(true)}
        onRemove={vi.fn().mockResolvedValue(true)}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: '+ Contact toevoegen' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/Voornaam/), 'Jan')
    await userEvent.type(within(dialog).getByLabelText(/Achternaam/), 'Logistiek')
    await userEvent.click(within(dialog).getByLabelText('Planning / levervenster'))

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    // Notifications go out by e-mail only: a recipient without an address is refused up front.
    expect(await within(dialog).findByText(/Meldingen worden per e-mail verstuurd/)).toBeInTheDocument()
    expect(onAdd).not.toHaveBeenCalled()
    expect(api.set).not.toHaveBeenCalled()

    // With an address the same form saves.
    await userEvent.type(within(dialog).getByLabelText(/E-mail/), 'jan@test.example')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onAdd).toHaveBeenCalledTimes(1))
  })
})
