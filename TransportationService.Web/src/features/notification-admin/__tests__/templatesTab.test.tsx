import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TemplatesTab } from '../components/TemplatesTab'
import type { CustomerMessageTemplate } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const customersState = vi.hoisted(() => ({
  items: [] as { id: string; customerNumber: string; name: string; city: null; countryCode: null; categoryName: null; isActive: boolean; isBlocked: boolean }[],
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn(() =>
    Promise.resolve({ items: customersState.items, totalCount: customersState.items.length, page: 1, pageSize: 500 }),
  ),
}))

const api = vi.hoisted(() => ({
  listMessageTemplates: vi.fn(),
  listCustomerMessageTemplates: vi.fn(),
  deleteMessageTemplate: vi.fn(),
  getMessageTemplateKinds: vi.fn(),
  getPlaceholders: vi.fn(),
  saveMessageTemplate: vi.fn(),
  previewTemplate: vi.fn(),
}))
vi.mock('../api/notificationAdminApi', () => api)

function customerRow(overrides: Partial<CustomerMessageTemplate> = {}): CustomerMessageTemplate {
  return {
    kind: 'order_created',
    channel: 'Email',
    language: 'nl',
    isOverridden: false,
    id: null,
    subject: null,
    body: 'Standaardtekst',
    bodyHtml: null,
    isActive: true,
    ...overrides,
  }
}

async function selectCustomerScope(user: ReturnType<typeof userEvent.setup>) {
  const combobox = await screen.findByRole('combobox', { name: 'Klantweergave' })
  await user.click(combobox)
  await user.click(await screen.findByRole('option', { name: 'KL-1 — Haven BV' }))
}

class FakeApiError extends Error {
  fieldErrors: Record<string, string[]>
  constructor(message: string, fieldErrors: Record<string, string[]>) {
    super(message)
    this.fieldErrors = fieldErrors
  }
}

describe('TemplatesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    customersState.items = []
    api.listMessageTemplates.mockResolvedValue([])
    api.listCustomerMessageTemplates.mockResolvedValue([])
    api.deleteMessageTemplate.mockResolvedValue(undefined)
    api.getMessageTemplateKinds.mockResolvedValue(['order_created', 'invoice_sent'])
    api.getPlaceholders.mockResolvedValue(['orderNumber', 'customerName'])
  })

  it('does not render for a user without message_templates.manage', () => {
    const { container } = render(<TemplatesTab canManage={false} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('inserts a placeholder chip at the cursor in the body field', async () => {
    const user = userEvent.setup()
    render(<TemplatesTab canManage />)

    await user.click(await screen.findByRole('button', { name: '+ Sjabloon' }))
    const body = await screen.findByRole('textbox', { name: /^Inhoud/ })
    await user.type(body, 'Hallo ')

    await user.click(await screen.findByRole('button', { name: '{{orderNumber}}' }))

    await waitFor(() => expect(body).toHaveValue('Hallo {{orderNumber}}'))
  })

  it('surfaces an unknown-placeholder save error next to the body field', async () => {
    const user = userEvent.setup()
    api.saveMessageTemplate.mockRejectedValue(
      new FakeApiError('Onbekende placeholder {{rariteit}}', { body: ['Onbekende placeholder {{rariteit}}'] }),
    )

    render(<TemplatesTab canManage />)

    await user.click(await screen.findByRole('button', { name: '+ Sjabloon' }))
    const body = await screen.findByRole('textbox', { name: /^Inhoud/ })
    await user.type(body, 'Beste {{rariteit}}')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((el) => el.textContent?.includes('Onbekende placeholder'))).toBe(true)
    // The FormField-level error (next to "Inhoud") is a distinct alert from the top banner.
    expect(alerts.length).toBeGreaterThanOrEqual(2)
  })

  describe('customer-override round-trip', () => {
    beforeEach(() => {
      customersState.items = [
        { id: 'cust-1', customerNumber: 'KL-1', name: 'Haven BV', city: null, countryCode: null, categoryName: null, isActive: true, isBlocked: false },
      ]
    })

    it('lists a selected customer\'s effective templates with Standaard/Klantspecifiek badges', async () => {
      const user = userEvent.setup()
      api.listCustomerMessageTemplates.mockResolvedValue([
        customerRow({ kind: 'order_created', isOverridden: false }),
        customerRow({ kind: 'invoice_sent', isOverridden: true, id: 'ovr-1', subject: 'Klant onderwerp', body: 'Klant tekst' }),
      ])

      render(<TemplatesTab canManage />)
      await selectCustomerScope(user)

      await waitFor(() => expect(api.listCustomerMessageTemplates).toHaveBeenCalledWith('cust-1'))
      expect(await screen.findByText('Standaard')).toBeInTheDocument()
      expect(screen.getByText('Klantspecifiek: Haven BV')).toBeInTheDocument()
    })

    it('edits an inherited row into a customer override, pre-filled with the default content', async () => {
      const user = userEvent.setup()
      api.listCustomerMessageTemplates.mockResolvedValue([customerRow({ kind: 'order_created', isOverridden: false, body: 'Standaardtekst' })])

      render(<TemplatesTab canManage />)
      await selectCustomerScope(user)

      await user.click(await screen.findByRole('button', { name: 'Bewerken' }))
      expect(await screen.findByRole('dialog', { name: 'Klantspecifiek sjabloon aanmaken' })).toBeInTheDocument()
      const body = screen.getByRole('textbox', { name: /^Inhoud/ })
      expect(body).toHaveValue('Standaardtekst')

      await user.click(screen.getByRole('button', { name: 'Opslaan' }))

      await waitFor(() =>
        expect(api.saveMessageTemplate).toHaveBeenCalledWith(
          expect.objectContaining({ kind: 'order_created', customerId: 'cust-1', body: 'Standaardtekst' }),
        ),
      )
    })

    it('deletes a customer override via the confirm dialog and reloads the customer list', async () => {
      const user = userEvent.setup()
      api.listCustomerMessageTemplates.mockResolvedValue([
        customerRow({ kind: 'invoice_sent', isOverridden: true, id: 'ovr-1', subject: 'Klant onderwerp', body: 'Klant tekst' }),
      ])

      render(<TemplatesTab canManage />)
      await selectCustomerScope(user)

      await user.click(await screen.findByRole('button', { name: 'Verwijderen' }))
      const confirmDialog = await screen.findByRole('dialog', { name: 'Sjabloon verwijderen' })
      await user.click(within(confirmDialog).getByRole('button', { name: 'Verwijderen' }))

      await waitFor(() => expect(api.deleteMessageTemplate).toHaveBeenCalledWith('ovr-1'))
      await waitFor(() => expect(api.listCustomerMessageTemplates).toHaveBeenCalledTimes(2))
    })

    it('does not offer a delete action for an inherited (non-overridden) row', async () => {
      const user = userEvent.setup()
      api.listCustomerMessageTemplates.mockResolvedValue([customerRow({ kind: 'order_created', isOverridden: false })])

      render(<TemplatesTab canManage />)
      await selectCustomerScope(user)

      await screen.findByRole('button', { name: 'Bewerken' })
      expect(screen.queryByRole('button', { name: 'Verwijderen' })).not.toBeInTheDocument()
    })
  })
})
