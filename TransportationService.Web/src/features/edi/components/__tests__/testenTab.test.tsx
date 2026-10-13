import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { TestenTab } from '../TestenTab'
import type { EdiMessageDetail, EdiPartner } from '../../api/ediApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listPartners: vi.fn(),
  validatePayload: vi.fn(),
  simulate: vi.fn(),
  getMessage: vi.fn(),
  replayMessage: vi.fn(),
}))
vi.mock('../../api/ediApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/ediApi')>()),
  listPartners: api.listPartners,
  validatePayload: api.validatePayload,
  simulate: api.simulate,
  getMessage: api.getMessage,
  replayMessage: api.replayMessage,
}))

function partner(overrides: Partial<EdiPartner> = {}): EdiPartner {
  return {
    id: 'partner-1', code: 'haven-edi', name: 'Haven EDI', customerId: 'cust-1', customerName: 'Haven BV',
    externalCustomerIdentifier: null, mappingProfile: 'generic-json-v1', isActive: true, notes: null, locations: [],
    ...overrides,
  }
}

function messageDetail(overrides: Partial<EdiMessageDetail> = {}): EdiMessageDetail {
  return {
    id: 'msg-sim-1', direction: 'Inbound', partnerCode: 'haven-edi', messageType: 'order',
    externalReference: 'SIM-1', status: 'Failed', attemptCount: 1, processedAt: null,
    errorDetail: 'Validatie mislukt.', mappingIssue: false, resultEntityType: null,
    resultEntityId: null, createdAt: '2026-07-30T10:00:00Z', validationErrors: null,
    payloadJson: '{}',
    ...overrides,
  }
}

function renderTab(canRetry: boolean) {
  return render(
    <MemoryRouter>
      <TestenTab canRetry={canRetry} />
    </MemoryRouter>,
  )
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

    renderTab(false)
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

    renderTab(false)
    await screen.findByDisplayValue('Haven EDI (haven-edi)')

    await user.click(screen.getByRole('button', { name: 'Valideren zonder te versturen' }))

    expect(await screen.findByText('Ongeldig')).toBeInTheDocument()
    expect(screen.getByText('Minstens één stop is verplicht.')).toBeInTheDocument()
  })

  it('prefills the payload textarea with the generic-json-v1 sample shape', async () => {
    renderTab(false)
    const textarea = await screen.findByLabelText('Payload (generiek JSON-profiel)')
    expect((textarea as HTMLTextAreaElement).value).toContain('"externalOrderId": "TEST-0001"')
    expect((textarea as HTMLTextAreaElement).value).toContain('"goodsDescription"')
  })

  it('opens the detail modal WITHOUT a retry button for an edi.test-only user (no edi.retry/manage)', async () => {
    const user = userEvent.setup()
    api.simulate.mockResolvedValue({ id: 'msg-sim-1' })
    api.getMessage.mockResolvedValue(messageDetail())

    renderTab(false)
    await screen.findByDisplayValue('Haven EDI (haven-edi)')
    await user.click(screen.getByRole('button', { name: 'Versturen naar test' }))

    expect(await screen.findByText('haven-edi')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opnieuw verwerken' })).not.toBeInTheDocument()
  })

  it('opens the detail modal WITH a retry button when the caller has edi.retry (or edi.manage)', async () => {
    const user = userEvent.setup()
    api.simulate.mockResolvedValue({ id: 'msg-sim-1' })
    api.getMessage.mockResolvedValue(messageDetail())

    renderTab(true)
    await screen.findByDisplayValue('Haven EDI (haven-edi)')
    await user.click(screen.getByRole('button', { name: 'Versturen naar test' }))

    expect(await screen.findByRole('button', { name: 'Opnieuw verwerken' })).toBeInTheDocument()
  })
})
