import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerContactsPanel } from '../components/CustomerContactsPanel'
import type { CustomerContact, CustomerContactInput } from '../types'

// Mutable permission set so each test controls what useAuth() reports.
const auth = vi.hoisted(() => ({ permissions: ['contact_departments.view', 'contact_departments.manage'] }))

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
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn(), showToast: vi.fn() }),
}))

vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [{ id: 'dep-1', code: 'SALES', name: 'Verkoop' }],
    isLoading: false,
    error: null,
  }),
}))

function makeContact(overrides: Partial<CustomerContact>): CustomerContact {
  return {
    id: 'contact-1',
    firstName: 'An',
    lastName: 'Peeters',
    role: 'Boekhouding',
    email: 'an@acme.be',
    phoneNumber: '+32 3 123 45 67',
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

function renderPanel(
  contacts: CustomerContact[],
  handlers: { onUpdate?: (contactId: string, input: CustomerContactInput) => Promise<boolean> } = {},
) {
  return render(
    <CustomerContactsPanel
      customerId="c1"
      contacts={contacts}
      isSubmitting={false}
      onAdd={vi.fn().mockResolvedValue(true)}
      onUpdate={handlers.onUpdate ?? vi.fn().mockResolvedValue(true)}
      onRemove={vi.fn().mockResolvedValue(true)}
    />,
  )
}

describe('CustomerContactsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = ['contact_departments.view', 'contact_departments.manage']
  })

  it('resolves the department name via the lookup options and shows GSM', () => {
    renderPanel([
      makeContact({ departmentId: 'dep-1', mobilePhone: '+32 470 12 34 56' }),
      makeContact({ id: 'contact-2', firstName: 'Jan', lastName: 'Claes', departmentId: null }),
    ])

    expect(screen.getByText('Verkoop')).toBeInTheDocument()
    expect(screen.getByText('+32 470 12 34 56')).toBeInTheDocument()
  })

  it('marks inactive contacts with an Inactief badge and prefers the display name', () => {
    renderPanel([makeContact({ isActive: false, displayName: 'An P. (facturatie)' })])

    expect(screen.getByText('Inactief')).toBeInTheDocument()
    expect(screen.getByText(/An P\. \(facturatie\)/)).toBeInTheDocument()
    expect(screen.queryByText('An Peeters')).not.toBeInTheDocument()
  })

  it('opens the contact dialog with the new fields from "+ Contact toevoegen"', async () => {
    renderPanel([])

    await userEvent.click(screen.getByRole('button', { name: '+ Contact toevoegen' }))

    expect(screen.getByLabelText('Weergavenaam')).toBeInTheDocument()
    expect(screen.getByLabelText('GSM')).toBeInTheDocument()
    expect(screen.getByText('Afdeling')).toBeInTheDocument()
    expect(screen.getByLabelText('Voorkeurstaal')).toBeInTheDocument()
    expect(screen.getByLabelText('Actief')).toBeInTheDocument()
    expect(screen.getByLabelText('Type', { selector: 'select#ct-type' })).toBeInTheDocument()
  })

  it('shows the type column with a per-type Primair badge', () => {
    renderPanel([
      makeContact({ contactType: 'Planning', isPrimary: true }),
      makeContact({ id: 'contact-2', firstName: 'Jan', lastName: 'Claes', contactType: 'Facturatie' }),
    ])

    const table = within(screen.getByRole('table'))
    expect(table.getByText('Planning')).toBeInTheDocument()
    expect(table.getByText('Facturatie')).toBeInTheDocument()
    // Only the primary-within-its-type contact carries the badge.
    expect(table.getAllByText('Primair')).toHaveLength(1)
  })

  it('filters the table on contact type', async () => {
    renderPanel([
      makeContact({ contactType: 'Planning' }),
      makeContact({ id: 'contact-2', firstName: 'Jan', lastName: 'Claes', contactType: 'Facturatie' }),
    ])

    expect(screen.getByText('An Peeters')).toBeInTheDocument()
    expect(screen.getByText('Jan Claes')).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Type'), 'Planning')

    expect(screen.getByText('An Peeters')).toBeInTheDocument()
    expect(screen.queryByText('Jan Claes')).not.toBeInTheDocument()
  })

  it('round-trips the contact type through the edit dialog', async () => {
    const onUpdate = vi.fn().mockResolvedValue(true)
    renderPanel([makeContact({ contactType: 'Planning' })], { onUpdate })

    await userEvent.click(screen.getByRole('button', { name: 'Bewerken' }))
    const typeSelect = screen.getByLabelText('Type', { selector: 'select#ct-type' })
    expect(typeSelect).toHaveValue('Planning')

    await userEvent.selectOptions(typeSelect, 'Facturatie')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(onUpdate).toHaveBeenCalledWith(
      'contact-1',
      expect.objectContaining({ contactType: 'Facturatie', firstName: 'An', lastName: 'Peeters' }),
    )
  })
})
