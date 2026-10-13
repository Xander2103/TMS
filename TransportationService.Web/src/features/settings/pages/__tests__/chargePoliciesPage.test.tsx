import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ChargePoliciesPage } from '../ChargePoliciesPage'
import type { ChargePolicy } from '../../api/chargePoliciesApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['problems.approve_charge']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  policies: [] as ChargePolicy[],
  save: vi.fn(),
}))

vi.mock('../../api/chargePoliciesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/chargePoliciesApi')>()),
  listChargePolicies: () => Promise.resolve(state.policies),
  saveChargePolicies: state.save,
}))
vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({
      items: [
        {
          id: 'cust-1', customerNumber: 'K-0001', name: 'Transport Janssens',
          city: null, countryCode: null, categoryName: null, isActive: true, isBlocked: false,
        },
      ],
      totalCount: 1, page: 1, pageSize: 200,
    }),
}))

function policy(overrides: Partial<ChargePolicy> = {}): ChargePolicy {
  return {
    id: 'pol-1', customerId: null, customerName: null, incidentType: null,
    mode: 'Propose', defaultAmount: null, defaultDescription: null, ...overrides,
  }
}

describe('ChargePoliciesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['problems.approve_charge'])
    state.policies = [
      policy({ id: 'pol-1', customerId: 'cust-1', customerName: 'Transport Janssens', incidentType: 'Damage', mode: 'Auto', defaultAmount: 125.5, defaultDescription: 'Schadeforfait' }),
      policy({ id: 'pol-2', mode: 'Never' }),
    ]
  })

  it('renders the policy rows from the api with customer, type, mode and amount', async () => {
    render(
      <MemoryRouter>
        <ChargePoliciesPage />
      </MemoryRouter>,
    )

    const customerSelect = await screen.findByLabelText('Klant beleid 1')
    expect(customerSelect).toHaveValue('cust-1')
    expect(screen.getByLabelText('Incidenttype beleid 1')).toHaveValue('Damage')
    expect(screen.getByLabelText('Modus beleid 1')).toHaveValue('Auto')
    expect(screen.getByLabelText('Standaardbedrag beleid 1')).toHaveValue(125.5)
    expect(screen.getByLabelText('Omschrijving beleid 1')).toHaveValue('Schadeforfait')

    // Tweede rij: algemeen beleid (alle klanten, alle types).
    expect(screen.getByLabelText('Klant beleid 2')).toHaveValue('')
    expect(screen.getByLabelText('Modus beleid 2')).toHaveValue('Never')
    expect(screen.getByRole('button', { name: 'Beleid toevoegen' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Opslaan' })).toBeInTheDocument()
  })

  it('is read-only zonder problems.approve_charge maar met incidents.manage', async () => {
    auth.permissions = new Set(['incidents.manage'])
    render(
      <MemoryRouter>
        <ChargePoliciesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByLabelText('Modus beleid 1')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })
})
