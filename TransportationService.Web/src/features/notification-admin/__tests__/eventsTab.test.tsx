import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EventsTab } from '../components/EventsTab'
import type { NotificationRule } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const permissionsState = vi.hoisted(() => ({
  permissions: [{ id: 'p1', code: 'orders.change_status', module: 'orders', action: 'change_status', description: 'Status wijzigen' }],
}))
vi.mock('../../roles/hooks/usePermissions', () => ({
  usePermissions: () => ({ permissions: permissionsState.permissions, isLoading: false, error: null }),
}))

const api = vi.hoisted(() => ({
  listNotificationRules: vi.fn(),
  updateNotificationRule: vi.fn(),
}))
vi.mock('../api/notificationAdminApi', () => api)

function rule(overrides: Partial<NotificationRule> = {}): NotificationRule {
  return {
    eventKey: 'order_created',
    label: 'Opdracht aangemaakt',
    group: 'Orders',
    allowedTokens: ['orderNumber'],
    enabled: true,
    inAppEnabled: true,
    emailEnabled: false,
    allowCustomerOverride: false,
    recipients: [{ type: 'InternalPermission', value: 'orders.change_status' }],
    isCustomized: false,
    peppolPending: false,
    ...overrides,
  }
}

describe('EventsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.updateNotificationRule.mockResolvedValue(undefined)
  })

  it('renders events grouped by group and toggles in-app via a PUT with the other fields unchanged', async () => {
    const user = userEvent.setup()
    api.listNotificationRules.mockResolvedValue([
      rule(),
      rule({ eventKey: 'invoice_sent', label: 'Factuur verzonden', group: 'Facturatie', emailEnabled: true, recipients: [] }),
    ])

    render(<EventsTab canManage />)

    expect(await screen.findByText('Orders')).toBeInTheDocument()
    expect(screen.getByText('Facturatie')).toBeInTheDocument()
    expect(screen.getByText('Opdracht aangemaakt')).toBeInTheDocument()
    expect(screen.getByText('Factuur verzonden')).toBeInTheDocument()

    await user.click(screen.getByLabelText('In-app voor Opdracht aangemaakt'))

    await waitFor(() =>
      expect(api.updateNotificationRule).toHaveBeenCalledWith('order_created', {
        enabled: true,
        inAppEnabled: false, // toggled
        emailEnabled: false,
        allowCustomerOverride: false,
        recipients: [{ type: 'InternalPermission', value: 'orders.change_status' }],
      }),
    )
  })

  it('disables channel/active toggles for Peppol-pending events and shows the badge', async () => {
    api.listNotificationRules.mockResolvedValue([
      rule({
        eventKey: 'invoice_peppol_queued',
        label: 'Peppol-factuur in wachtrij',
        group: 'Facturatie',
        peppolPending: true,
      }),
    ])

    render(<EventsTab canManage />)

    expect(await screen.findByText('Nog niet actief')).toBeInTheDocument()
    expect(screen.getByLabelText('In-app voor Peppol-factuur in wachtrij')).toBeDisabled()
    expect(screen.getByLabelText('E-mail voor Peppol-factuur in wachtrij')).toBeDisabled()
    expect(screen.getByLabelText('Actief: Peppol-factuur in wachtrij')).toBeDisabled()
  })

  it('hides the edit action without manage permission', async () => {
    api.listNotificationRules.mockResolvedValue([rule()])
    render(<EventsTab canManage={false} />)

    await screen.findByText('Opdracht aangemaakt')
    expect(screen.queryByRole('button', { name: 'Bewerken' })).not.toBeInTheDocument()
  })

  describe('recipients modal', () => {
    it('adds an explicit e-mail recipient with client-side validation before saving', async () => {
      const user = userEvent.setup()
      api.listNotificationRules.mockResolvedValue([rule()])
      render(<EventsTab canManage />)

      await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
      const dialog = await screen.findByRole('dialog')

      await user.click(within(dialog).getByRole('button', { name: '+ E-mailadres toevoegen' }))
      const emailInput = within(dialog).getByLabelText('E-mailadres ontvanger 2')
      await user.type(emailInput, 'niet-geldig')
      await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

      expect(await within(dialog).findByRole('alert')).toHaveTextContent('geldig e-mailadres')
      expect(api.updateNotificationRule).not.toHaveBeenCalled()

      await user.clear(emailInput)
      await user.type(emailInput, 'ops@acme.test')
      await user.click(within(dialog).getByRole('button', { name: 'Opslaan' }))

      await waitFor(() =>
        expect(api.updateNotificationRule).toHaveBeenCalledWith(
          'order_created',
          expect.objectContaining({
            recipients: [
              { type: 'InternalPermission', value: 'orders.change_status' },
              { type: 'ExplicitEmail', value: 'ops@acme.test' },
            ],
          }),
        ),
      )
    })
  })
})
