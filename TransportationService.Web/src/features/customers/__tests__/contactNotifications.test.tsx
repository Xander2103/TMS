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

  it('surfaces a failed routing update inline and keeps the dialog open', async () => {
    api.set.mockRejectedValue(new Error('boom'))
    const onChanged = vi.fn()
    render(
      <CustomerContactsPanel
        customerId="c1"
        contacts={[contact()]}
        isSubmitting={false}
        onAdd={vi.fn()}
        onUpdate={vi.fn().mockResolvedValue(true)}
        onRemove={vi.fn()}
        onChanged={onChanged}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))

    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    await userEvent.click(within(dialog).getByLabelText('ETA / vertraging'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.set).toHaveBeenCalledTimes(1))
    // describeApiError keeps the server's own message when there is one — shown in the dialog.
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('boom')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('ETA / vertraging')).toBeChecked()
    expect(onChanged).not.toHaveBeenCalled()
    expect(toast.showError).not.toHaveBeenCalled()
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

/**
 * UX-sprint 2026-09-09 §1.1/§2.2 — the three proven root causes of "meldingen worden niet
 * opgeslagen": reload racing the notification PUT, the async GET overwriting user ticks, and a
 * swallowed GET turning one save into a mass unsubscribe.
 */
describe('contact — meldingen: volgorde van opslaan en laadtoestand', () => {
  function deferred<T>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void
    const promise = new Promise<T>((res, rej) => {
      resolve = res
      reject = rej
    })
    return { promise, resolve, reject }
  }

  function renderWith(overrides: Partial<Parameters<typeof CustomerContactsPanel>[0]> = {}) {
    return render(
      <CustomerContactsPanel
        customerId="c1"
        contacts={[contact()]}
        isSubmitting={false}
        onAdd={vi.fn().mockResolvedValue(contact({ id: 'new-1' }))}
        onUpdate={vi.fn().mockResolvedValue(true)}
        onRemove={vi.fn().mockResolvedValue(true)}
        {...overrides}
      />,
    )
  }

  async function openEdit() {
    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    return screen.findByRole('dialog')
  }

  it('(a) writes the chosen set after the contact update resolved and before onChanged', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
    const calls: string[] = []
    const onUpdate = vi.fn(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20))
      calls.push('update-resolved')
      return true
    })
    api.set.mockImplementation((...args: unknown[]) => {
      calls.push('set')
      return Promise.resolve({ contactId: 'ct-1', optionKeys: args[2] as string[] })
    })
    const onChanged = vi.fn(() => calls.push('changed'))
    renderWith({ onUpdate, onChanged })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeEnabled())
    await userEvent.click(within(dialog).getByLabelText('Planning / levervenster'))
    await userEvent.click(within(dialog).getByLabelText('ETA / vertraging'))
    await userEvent.click(within(dialog).getByLabelText('Facturen'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    expect(calls).toEqual(['update-resolved', 'set', 'changed'])
    const [, , keys] = api.set.mock.calls[0] as [string, string, string[]]
    expect([...keys].sort()).toEqual(['eta', 'invoice', 'planning'])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(toast.showSuccess).toHaveBeenCalledTimes(1)
  })

  it('(b)+(c) reopens with the saved set and persists a removal', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning', 'eta', 'invoice'] })
    const onChanged = vi.fn()
    renderWith({ onChanged })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    expect(within(dialog).getByLabelText('ETA / vertraging')).toBeChecked()
    expect(within(dialog).getByLabelText('Facturen')).toBeChecked()
    expect(within(dialog).getByLabelText('Orderbevestigingen')).not.toBeChecked()

    await userEvent.click(within(dialog).getByLabelText('ETA / vertraging'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.set).toHaveBeenCalledTimes(1))
    const [, , keys] = api.set.mock.calls[0] as [string, string, string[]]
    expect([...keys].sort()).toEqual(['invoice', 'planning'])
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
  })

  it('(d) a failed GET locks the boxes, shows the error and never sends optionKeys', async () => {
    api.get.mockRejectedValue(new Error('offline'))
    const onUpdate = vi.fn().mockResolvedValue(true)
    const onChanged = vi.fn()
    renderWith({ onUpdate, onChanged })

    const dialog = await openEdit()
    expect(
      await within(dialog).findByText('Meldingen konden niet geladen worden; voorkeuren blijven ongewijzigd.'),
    ).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Planning / levervenster')).toBeDisabled()
    expect(within(dialog).getByLabelText('Facturen')).toBeDisabled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onUpdate).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    expect(api.set).not.toHaveBeenCalled()
  })

  it('(e) a failed notifications PUT keeps the dialog open with the error and no reload', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning'] })
    api.set.mockRejectedValue(new Error('routing down'))
    const onChanged = vi.fn()
    renderWith({ onChanged })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    await userEvent.click(within(dialog).getByLabelText('Facturen'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(api.set).toHaveBeenCalledTimes(1))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('routing down')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Facturen')).toBeChecked()
    expect(onChanged).not.toHaveBeenCalled()
    // The controls are usable again for a retry.
    expect(within(dialog).getByRole('button', { name: 'Opslaan' })).toBeEnabled()
  })

  it('(e-ter) after a failed notifications PUT a retry re-sends the fields and converges on the chosen set', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: ['planning'] })
    api.set.mockRejectedValueOnce(new Error('routing down')).mockResolvedValueOnce({ contactId: 'ct-1', optionKeys: ['planning', 'invoice'] })
    const onUpdate = vi.fn().mockResolvedValue(true)
    const onChanged = vi.fn()
    renderWith({ onUpdate, onChanged })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked())
    await userEvent.type(within(dialog).getByLabelText(/Functie/), 'Planner')
    await userEvent.click(within(dialog).getByLabelText('Facturen'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('routing down')

    // Selections and typed values survive the failure; nothing was reset from the server.
    expect(within(dialog).getByLabelText('Facturen')).toBeChecked()
    expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked()
    expect(within(dialog).getByLabelText(/Functie/)).toHaveValue('Planner')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    // Idempotent PUT of the fields, then the routing PUT that now succeeds — with the full intended set.
    expect(onUpdate).toHaveBeenCalledTimes(2)
    expect(onUpdate).toHaveBeenLastCalledWith('ct-1', expect.objectContaining({ role: 'Planner' }))
    expect(api.set).toHaveBeenCalledTimes(2)
    const [, , keys] = api.set.mock.calls[1] as [string, string, string[]]
    expect([...keys].sort()).toEqual(['invoice', 'planning'])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('(e-quater) a NEW contact whose notifications PUT failed is not created twice on retry', async () => {
    api.set.mockRejectedValueOnce(new Error('routing down')).mockResolvedValueOnce({ contactId: 'new-1', optionKeys: ['planning'] })
    const onAdd = vi.fn().mockResolvedValue(contact({ id: 'new-1', firstName: 'Nieuw', lastName: 'Contact' }))
    const onUpdate = vi.fn().mockResolvedValue(true)
    const onChanged = vi.fn()
    renderWith({ onAdd, onUpdate, onChanged })

    await userEvent.click(screen.getByRole('button', { name: '+ Contact toevoegen' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText(/Voornaam/), 'Nieuw')
    await userEvent.type(within(dialog).getByLabelText(/Achternaam/), 'Contact')
    await userEvent.type(within(dialog).getByLabelText(/E-mail/), 'nieuw@example.com')
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeEnabled())
    await userEvent.click(within(dialog).getByLabelText('Planning / levervenster'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('routing down')
    expect(onAdd).toHaveBeenCalledTimes(1)
    expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    // The contact exists since the first attempt: the retry updates it, never creates a duplicate.
    expect(onAdd).toHaveBeenCalledTimes(1)
    expect(onUpdate).toHaveBeenCalledTimes(1)
    expect(onUpdate).toHaveBeenCalledWith('new-1', expect.objectContaining({ firstName: 'Nieuw' }))
    expect(api.set).toHaveBeenLastCalledWith('c1', 'new-1', ['planning'])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('(e-bis) a failed contact update keeps the dialog open and never touches notifications', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
    const onUpdate = vi.fn().mockRejectedValue(new Error('naam te lang'))
    const onChanged = vi.fn()
    renderWith({ onUpdate, onChanged })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Facturen')).toBeEnabled())
    await userEvent.click(within(dialog).getByLabelText('Facturen'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('naam te lang')
    expect(api.set).not.toHaveBeenCalled()
    expect(onChanged).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('(f) keeps the boxes disabled with a loading hint until the GET resolves', async () => {
    const pending = deferred<{ contactId: string; optionKeys: string[] }>()
    api.get.mockReturnValue(pending.promise)
    renderWith()

    const dialog = await openEdit()
    const planning = await within(dialog).findByLabelText('Planning / levervenster')
    expect(planning).toBeDisabled()
    expect(within(dialog).getByText('Meldingen laden…')).toBeInTheDocument()

    pending.resolve({ contactId: 'ct-1', optionKeys: ['planning'] })
    await waitFor(() => expect(within(dialog).getByLabelText('Planning / levervenster')).toBeEnabled())
    expect(within(dialog).getByLabelText('Planning / levervenster')).toBeChecked()
    expect(within(dialog).queryByText('Meldingen laden…')).not.toBeInTheDocument()
  })

  it('(g) the contact payload carries the fields intact and never the notification keys', async () => {
    api.get.mockResolvedValue({ contactId: 'ct-1', optionKeys: [] })
    const onUpdate = vi.fn().mockResolvedValue(true)
    renderWith({ onUpdate, contacts: [contact({ role: 'Planner', phoneNumber: '+32 3 000 00 00', isPrimary: true })] })

    const dialog = await openEdit()
    await waitFor(() => expect(within(dialog).getByLabelText('Facturen')).toBeEnabled())
    await userEvent.click(within(dialog).getByLabelText('Facturen'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

    await waitFor(() => expect(onUpdate).toHaveBeenCalledTimes(1))
    const [contactId, input] = onUpdate.mock.calls[0] as [string, Record<string, unknown>]
    expect(contactId).toBe('ct-1')
    expect(input).toEqual(
      expect.objectContaining({
        firstName: 'Jan',
        lastName: 'Peeters',
        email: 'jan@example.com',
        role: 'Planner',
        phoneNumber: '+32 3 000 00 00',
        contactType: 'Algemeen',
        isPrimary: true,
        isActive: true,
      }),
    )
    expect('optionKeys' in input).toBe(false)
    expect('notifications' in input).toBe(false)
    await waitFor(() => expect(api.set).toHaveBeenCalledWith('c1', 'ct-1', ['invoice']))
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
