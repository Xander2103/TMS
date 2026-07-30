import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PartnerModal } from '../PartnerModal'
import type { EdiPartner } from '../../api/ediApi'

const api = vi.hoisted(() => ({
  createPartner: vi.fn(),
  updatePartner: vi.fn(),
}))
vi.mock('../../api/ediApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/ediApi')>()),
  createPartner: api.createPartner,
  updatePartner: api.updatePartner,
}))

vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: vi.fn().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 }),
}))

function partner(overrides: Partial<EdiPartner> = {}): EdiPartner {
  return {
    id: 'partner-1',
    code: 'haven-edi',
    name: 'Haven EDI',
    customerId: null,
    customerName: null,
    externalCustomerIdentifier: null,
    mappingProfile: 'generic-json-v1',
    isActive: true,
    notes: null,
    locations: [],
    ...overrides,
  }
}

describe('PartnerModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.createPartner.mockResolvedValue({ id: 'new-id' })
    api.updatePartner.mockResolvedValue({ id: 'partner-1' })
  })

  it('creates a new partner with code, name and defaults', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<PartnerModal partner={null} onClose={vi.fn()} onSaved={onSaved} />)

    await user.type(screen.getByLabelText(/^Code/), 'nieuw-partner')
    await user.type(screen.getByLabelText(/^Naam/), 'Nieuwe Partner BV')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(api.createPartner).toHaveBeenCalledWith({
        code: 'nieuw-partner',
        name: 'Nieuwe Partner BV',
        customerId: null,
        externalCustomerIdentifier: null,
        isActive: true,
      }),
    )
    expect(onSaved).toHaveBeenCalled()
  })

  it('rejects a create submit missing code or name', async () => {
    const user = userEvent.setup()
    render(<PartnerModal partner={null} onClose={vi.fn()} onSaved={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Code en naam zijn verplicht.')
    expect(api.createPartner).not.toHaveBeenCalled()
  })

  it('updates an existing partner without a code field, submitting the update payload', async () => {
    const user = userEvent.setup()
    const onSaved = vi.fn()
    render(<PartnerModal partner={partner()} onClose={vi.fn()} onSaved={onSaved} />)

    expect(screen.queryByLabelText(/^Code/)).not.toBeInTheDocument()
    const nameInput = screen.getByLabelText(/^Naam/)
    await user.clear(nameInput)
    await user.type(nameInput, 'Haven EDI (bijgewerkt)')
    await user.click(screen.getByLabelText('Actief'))
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(api.updatePartner).toHaveBeenCalledWith('partner-1', {
        name: 'Haven EDI (bijgewerkt)',
        customerId: null,
        externalCustomerIdentifier: null,
        mappingProfile: 'generic-json-v1',
        isActive: false,
        notes: null,
      }),
    )
    expect(onSaved).toHaveBeenCalled()
  })

  it('shows the generic-JSON profile note', () => {
    render(<PartnerModal partner={null} onClose={vi.fn()} onSaved={vi.fn()} />)
    expect(screen.getByText(/Generiek JSON/)).toBeInTheDocument()
  })
})
