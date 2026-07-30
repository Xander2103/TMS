import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TestenTab } from '../TestenTab'
import type { EdiPartner } from '../../api/ediApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listPartners: vi.fn(),
  validatePayload: vi.fn(),
  simulate: vi.fn(),
}))
vi.mock('../../api/ediApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/ediApi')>()),
  listPartners: api.listPartners,
  validatePayload: api.validatePayload,
  simulate: api.simulate,
}))

function partner(overrides: Partial<EdiPartner> = {}): EdiPartner {
  return {
    id: 'partner-1', code: 'haven-edi', name: 'Haven EDI', customerId: 'cust-1', customerName: 'Haven BV',
    externalCustomerIdentifier: null, mappingProfile: 'generic-json-v1', isActive: true, notes: null, locations: [],
    ...overrides,
  }
}

describe('TestenTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listPartners.mockResolvedValue([partner()])
  })

  it('renders a wouldCreate summary for a valid dry-run', async () => {
    const user = userEvent.setup()
    api.validatePayload.mockResolvedValue({
      valid: true,
      errors: [],
      wouldCreate: {
        externalOrderId: 'TEST-0001',
        customerReference: 'SIM-REF',
        goodsDescription: 'Simulatie-opdracht via EDI',
        stopCount: 2,
        cargoLineCount: 1,
        resolvedLocationCodes: [],
        resolvedUnitCodes: [],
      },
    })

    render(<TestenTab />)
    await screen.findByDisplayValue('Haven EDI (haven-edi)')

    await user.click(screen.getByRole('button', { name: 'Valideren zonder te versturen' }))

    expect(await screen.findByText('Geldig ✓')).toBeInTheDocument()
    expect(screen.getByText('TEST-0001')).toBeInTheDocument()
    expect(api.validatePayload).toHaveBeenCalledWith(
      expect.objectContaining({ partnerCode: 'haven-edi', messageType: 'order' }),
    )
  })

  it('renders Dutch validation errors for an invalid dry-run', async () => {
    const user = userEvent.setup()
    api.validatePayload.mockResolvedValue({
      valid: false,
      errors: ['Minstens één stop is verplicht.'],
      wouldCreate: null,
    })

    render(<TestenTab />)
    await screen.findByDisplayValue('Haven EDI (haven-edi)')

    await user.click(screen.getByRole('button', { name: 'Valideren zonder te versturen' }))

    expect(await screen.findByText('Ongeldig')).toBeInTheDocument()
    expect(screen.getByText('Minstens één stop is verplicht.')).toBeInTheDocument()
  })

  it('prefills the payload textarea with the generic-json-v1 sample shape', async () => {
    render(<TestenTab />)
    const textarea = await screen.findByLabelText('Payload (generiek JSON-profiel)')
    expect((textarea as HTMLTextAreaElement).value).toContain('"externalOrderId": "TEST-0001"')
    expect((textarea as HTMLTextAreaElement).value).toContain('"goodsDescription"')
  })
})
