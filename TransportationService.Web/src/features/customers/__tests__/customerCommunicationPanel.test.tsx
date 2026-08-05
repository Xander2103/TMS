import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CustomerCommunicationPanel } from '../components/CustomerCommunicationPanel'
import { communicationTypeLabel, type CustomerCommunicationRule, type CustomerContact } from '../types'

const auth = vi.hoisted(() => ({ permissions: ['customers.view'] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const rules = vi.hoisted(() => ({ value: [] as CustomerCommunicationRule[] }))

vi.mock('../api/customerCommunicationApi', () => ({
  listCommunicationRules: () => Promise.resolve(rules.value),
  createCommunicationRule: vi.fn(),
  updateCommunicationRule: vi.fn(),
  deleteCommunicationRule: vi.fn(),
}))

function makeContact(overrides: Partial<CustomerContact>): CustomerContact {
  return {
    id: 'contact-1',
    firstName: 'An',
    lastName: 'Peeters',
    role: null,
    email: 'an@acme.be',
    phoneNumber: null,
    isPrimary: false,
    notes: null,
    displayName: null,
    nickname: null,
    mobilePhone: null,
    departmentId: null,
    preferredLanguageCode: null,
    isActive: true,
    contactType: 'Algemeen',
    ...overrides,
  }
}

function makeRule(overrides: Partial<CustomerCommunicationRule>): CustomerCommunicationRule {
  return {
    id: 'rule-1',
    type: 'Invoice',
    customTypeLabel: null,
    channel: 'Email',
    ccEmail: null,
    languageCode: null,
    fallbackContactId: null,
    isActive: true,
    contactIds: ['contact-1'],
    ...overrides,
  }
}

describe('communicationTypeLabel', () => {
  it('returns the static label for known types', () => {
    expect(communicationTypeLabel('Invoice', null)).toBe('Factuur')
    expect(communicationTypeLabel('PlanningAlert', null)).toBe('Planningsmeldingen')
  })

  it('appends the custom label for the Other type', () => {
    expect(communicationTypeLabel('Other', 'Speciale melding')).toBe('Andere: Speciale melding')
  })

  it('falls back to the generic Other label when no custom label is set', () => {
    expect(communicationTypeLabel('Other', null)).toBe('Andere')
    expect(communicationTypeLabel('Other', '   ')).toBe('Andere')
  })
})

describe('CustomerCommunicationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = ['customers.view']
  })

  it('renders the rules and shows the custom Other label', async () => {
    rules.value = [
      makeRule({ id: 'rule-1', type: 'Invoice', contactIds: ['contact-1'] }),
      makeRule({ id: 'rule-2', type: 'Other', customTypeLabel: 'Speciale melding', contactIds: ['contact-1'] }),
    ]

    render(<CustomerCommunicationPanel customerId="cust-1" contacts={[makeContact({})]} />)

    expect(await screen.findByText('Factuur')).toBeInTheDocument()
    expect(await screen.findByText('Andere: Speciale melding')).toBeInTheDocument()
    expect(screen.getAllByText('An Peeters').length).toBeGreaterThan(0)
  })
})
