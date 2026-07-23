import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'

const auth = vi.hoisted(() => ({ permissions: ['customers.manage_fiscal', 'customers.view'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: () => null,
}))

beforeEach(() => {
  auth.permissions = ['customers.manage_fiscal', 'customers.view']
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
    { code: '9925', label: 'Belgisch BTW-nummer', countryCode: 'BE' },
  ])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
})

function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      <CustomerForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

describe('CustomerForm section navigation', () => {
  it('preserves values when switching sections', async () => {
    renderForm()
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    expect(screen.queryByRole('textbox', { name: 'Naam' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByRole('textbox', { name: 'Naam' })).toHaveValue('Acme')
  })

  it('renders one combined Peppol-ID field with the advanced fields hidden', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    expect(screen.getByRole('group', { name: 'Peppol' })).toBeInTheDocument()
    expect(screen.getByLabelText(/Peppol-ID/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Schema/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Participant-ID/i)).not.toBeInTheDocument()
  })

  it('blocks submit on an invalid combined Peppol-ID and routes to the fiscal section', async () => {
    const onSubmit = vi.fn()
    renderForm({ onSubmit })
    await userEvent.type(screen.getByRole('textbox', { name: 'Naam' }), 'Acme')
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.type(screen.getByLabelText(/Peppol-ID/i), 'abcd:123')
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    expect(onSubmit).not.toHaveBeenCalled()
    const fiscaal = screen.getByRole('tab', { name: /Fiscaal & Peppol/i })
    expect(fiscaal).toHaveAttribute('aria-selected', 'true')
    expect(fiscaal).toHaveAttribute('data-has-error', 'true')
  })

  it('shows Bank section content immediately on tab click (no second accordion click)', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    expect(screen.getByRole('textbox', { name: /IBAN/i })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: /BIC/i })).toBeInTheDocument()
  })

  it('routes to Algemeen when a required field fails on submit', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    const algemeen = screen.getByRole('tab', { name: /Algemeen/i })
    expect(algemeen).toHaveAttribute('aria-selected', 'true')
    expect(algemeen).toHaveAttribute('data-has-error', 'true')
  })
})
