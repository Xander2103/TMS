import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'
import type { CompanyRegistryResult } from '../../types'

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

function registryResult(overrides: Partial<CompanyRegistryResult>): CompanyRegistryResult {
  return {
    legalName: null,
    companyNumber: null,
    vatNumber: null,
    street: null,
    houseNumber: null,
    postalCode: null,
    city: null,
    countryCode: null,
    peppolId: null,
    peppolScheme: null,
    ...overrides,
  }
}

beforeEach(() => {
  auth.permissions = ['customers.manage_fiscal', 'customers.view']
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
    { code: '9925', label: 'Belgisch BTW-nummer', countryCode: 'BE' },
  ])
})

function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      <CustomerForm mode="create" isSubmitting={false} submitError={null} onSubmit={vi.fn()} onCancel={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

async function openFiscaalAndLookup() {
  await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
  // A number is required before a registry lookup can run.
  await userEvent.type(screen.getByLabelText(/Ondernemingsnummer/i), '0123456749')
  await userEvent.click(screen.getByRole('button', { name: /Gegevens opzoeken/i }))
  // Review panel appears; adopt the found values.
  await userEvent.click(await screen.findByRole('button', { name: /Overnemen/i }))
}

describe('Customer Peppol registry lookup', () => {
  it('auto-fills the combined Peppol-ID from a provider hit and marks it validated', async () => {
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true,
      result: registryResult({ peppolId: '0123456789', peppolScheme: '0208', legalName: 'Acme NV' }),
    })
    renderForm()
    await openFiscaalAndLookup()
    expect(await screen.findByText('Gevalideerd')).toBeInTheDocument()
    expect(screen.getByLabelText(/Peppol-ID/i)).toHaveValue('0208:0123456789')
  })

  it('shows not-found when the provider returns no peppol data', async () => {
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true,
      result: registryResult({ legalName: 'Acme NV' }),
    })
    renderForm()
    await openFiscaalAndLookup()
    expect(await screen.findByText(/niet gevonden/i)).toBeInTheDocument()
  })

  it('does not overwrite existing manual peppol data without confirmation', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true,
      result: registryResult({ peppolId: '9999999999', peppolScheme: '9925' }),
    })
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.type(screen.getByLabelText(/Peppol-ID/i), '0123456789')
    await userEvent.type(screen.getByLabelText(/Ondernemingsnummer/i), '0123456749')
    await userEvent.click(screen.getByRole('button', { name: /Gegevens opzoeken/i }))
    await userEvent.click(await screen.findByRole('button', { name: /Overnemen/i }))
    expect(confirmSpy).toHaveBeenCalled()
    expect(screen.getByLabelText(/Peppol-ID/i)).toHaveValue('0123456789') // unchanged, still manual
    expect(screen.getByText('Manueel ingevoerd')).toBeInTheDocument()
  })
})
