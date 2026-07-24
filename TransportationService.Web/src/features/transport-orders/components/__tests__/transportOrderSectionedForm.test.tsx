import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TransportOrderForm } from '../TransportOrderForm'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['locations.create']) }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({ items: [{ id: 'cust-1', name: 'Klant X', customerNumber: 'KL-1' }], total: 1 }),
  getCustomer: () => Promise.resolve({ id: 'cust-1', isBlocked: false }),
}))
vi.mock('../../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: () => Promise.resolve([]),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' },
      { id: 'u-colli', code: 'COLLI', name: 'Colli' },
      { id: 'u-kg', code: 'KG', name: 'Kilogram' },
    ],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

const previewSpy = vi.hoisted(() => vi.fn())

vi.mock('../../../tarification/api/pricingApi', async () => {
  const actual = await vi.importActual<typeof import('../../../tarification/api/pricingApi')>(
    '../../../tarification/api/pricingApi',
  )
  return {
    ...actual,
    listServiceOptions: () =>
      Promise.resolve([
        { id: 'opt-8', code: 'VOOR8', name: 'Levering vóór 08:00', kind: 'Fixed', defaultValue: 25, isActive: true, sortOrder: 0 },
      ]),
    getCustomerPricingConfig: () =>
      Promise.resolve({
        preferredUnits: [{ unitTypeId: 'u-pallet', code: 'EUROPALLET', name: 'Europallet', sortOrder: 0 }],
        serviceOptions: [],
      }),
    previewPrice: previewSpy.mockResolvedValue({
      lines: [
        { label: '3 × Europallet (zone Z3)', amount: 145, source: 'Pallets klant X', informational: false },
        { label: 'Dieseltoeslag 8%', amount: 11.6, source: 'Dieseltoeslag', informational: true },
      ],
      total: 145,
      totalWithInformational: 156.6,
      currency: 'EUR',
      zoneCode: 'Z3',
      zoneName: 'Zone 3',
      requiresManualPrice: false,
      serviceLines: [],
    }),
  }
})

function renderForm() {
  return render(
    <MemoryRouter>
      <TransportOrderForm submitLabel="Opdracht aanmaken" onSubmit={vi.fn()} />
    </MemoryRouter>,
  )
}

describe('TransportOrderForm sections + pricing', () => {
  beforeEach(() => {
    auth.permissions = new Set(['locations.create'])
    previewSpy.mockClear()
  })

  it('renders the seven tabs and preserves values across switches', async () => {
    renderForm()
    for (const label of ['Algemeen', 'Route & stops', 'Goederen', 'Services & toeslagen', 'Documenten', 'Prijs', 'Samenvatting']) {
      expect(screen.getByRole('tab', { name: new RegExp(label) })).toBeInTheDocument()
    }

    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')

    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
    expect(screen.getByLabelText('Klant *')).toHaveValue('cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    expect(screen.getByLabelText('Aantal')).toHaveValue(3)
  })

  it('lists customer-preferred units first with a toggle for the rest', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))

    const unitSelect = screen.getByLabelText('Eenheid')
    await waitFor(() => expect(unitSelect.querySelector('optgroup[label="Gebruikelijk voor deze klant"]')).toBeTruthy())
    expect(unitSelect.querySelectorAll('option')).toHaveLength(2) // placeholder + preferred only

    await userEvent.click(screen.getByRole('button', { name: 'Andere eenheden tonen' }))
    expect(unitSelect.querySelector('optgroup[label="Andere eenheden"]')).toBeTruthy()
    expect(unitSelect.querySelectorAll('option').length).toBeGreaterThan(2)
  })

  it('shows the calculated breakdown on the Prijs tab', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.type(screen.getByLabelText('Aantal'), '3')
    await userEvent.selectOptions(screen.getByLabelText('Eenheid'), 'EUROPALLET')

    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    await waitFor(() => expect(previewSpy).toHaveBeenCalled(), { timeout: 3000 })
    await waitFor(() => expect(screen.getByText('3 × Europallet (zone Z3)')).toBeInTheDocument())
    expect(screen.getByText(/Berekend totaal/)).toBeInTheDocument()
    expect(screen.getAllByText('€ 145.00').length).toBeGreaterThan(0)
    // Without the permission there is no manual-override checkbox.
    expect(screen.queryByLabelText(/Handmatige prijs/)).not.toBeInTheDocument()
  })

  it('offers the manual override only with orders.override_price', async () => {
    auth.permissions = new Set(['orders.override_price'])
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /^Prijs$/ }))
    expect(screen.getByLabelText(/Handmatige prijs \(overschrijft/)).toBeInTheDocument()
  })

  it('selecting a service option shows its price', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Services & toeslagen/ }))
    await waitFor(() => expect(screen.getByText(/Levering vóór 08:00/)).toBeInTheDocument())
    expect(screen.getByText(/€ 25.00/)).toBeInTheDocument()
  })
})
