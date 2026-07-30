import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TemplatesTab } from '../components/TemplatesTab'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 500 }),
}))

const api = vi.hoisted(() => ({
  listMessageTemplates: vi.fn(),
  getMessageTemplateKinds: vi.fn(),
  getPlaceholders: vi.fn(),
  saveMessageTemplate: vi.fn(),
  previewTemplate: vi.fn(),
}))
vi.mock('../api/notificationAdminApi', () => api)

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
    api.listMessageTemplates.mockResolvedValue([])
    api.getMessageTemplateKinds.mockResolvedValue(['order_created'])
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
})
