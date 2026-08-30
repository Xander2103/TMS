import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TransportOrderForm } from '../TransportOrderForm'

/**
 * Wave 1 §12 progressive disclosure + targeted validation:
 * - validate() lists field-targeted errors in the ValidationSummary; entries navigate.
 * - stop rows show 7 default controls, the rest behind a collapsed "Geavanceerd".
 * - cargo lines show 4 default fields + ADR, the rest behind "Meer details".
 * - the derived volume summary multiplies per-piece volume by the expected quantity.
 */

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
    options: [{ id: 'u-pallet', code: 'EUROPALLET', name: 'Europallet' }],
    isLoading: false,
    error: null,
  }),
}))
vi.mock('../../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../warehousing/api/warehousingApi', () => ({
  listWarehouses: () => Promise.resolve([]),
}))
vi.mock('../../../tarification/api/pricingApi', async () => {
  const actual = await vi.importActual<typeof import('../../../tarification/api/pricingApi')>(
    '../../../tarification/api/pricingApi',
  )
  return {
    ...actual,
    listServiceOptions: () => Promise.resolve([]),
    getCustomerPricingConfig: () => Promise.resolve({ preferredUnits: [], serviceOptions: [] }),
    listUnitTypeMaster: () => Promise.resolve([]),
    previewPrice: () =>
      Promise.resolve({
        lines: [], total: 0, totalWithInformational: 0, currency: 'EUR',
        zoneCode: null, zoneName: null, requiresManualPrice: false, serviceLines: [],
      }),
  }
})

function renderForm(onSubmit = vi.fn().mockResolvedValue(undefined)) {
  render(
    <MemoryRouter>
      <TransportOrderForm mode="create" submitLabel="Opdracht aanmaken" onSubmit={onSubmit} />
    </MemoryRouter>,
  )
  return { onSubmit }
}

beforeEach(() => {
  auth.permissions = new Set(['locations.create'])
})

describe('TransportOrderForm targeted validation (Wave 1 §12)', () => {
  it('lists every failing field in the validation summary, jumps to the first failing section and navigates on click', async () => {
    const { onSubmit } = renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByLabelText('Klant *'), 'cust-1')

    // No stop cities, no goods: submitting from Algemeen collects BOTH problems at once.
    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))
    expect(onSubmit).not.toHaveBeenCalled()

    // Inline FormField errors are alerts too — target the summary block itself.
    const summary = document.querySelector('.ui-validation-summary') as HTMLElement
    expect(within(summary).getAllByText('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')).toHaveLength(2)
    expect(within(summary).getByText(
      'Vul minstens een hoeveelheid en eenheid in, voeg een goederenlijn toe of beschrijf de goederen.',
    )).toBeInTheDocument()

    // Submit jumped to the FIRST failing section (route comes before goederen).
    expect(screen.getByRole('tab', { name: /Route & stops/ })).toHaveAttribute('aria-selected', 'true')

    // Clicking a summary entry navigates to the section that owns the field.
    await userEvent.click(within(summary).getByRole('button', { name: /Vul minstens een hoeveelheid/ }))
    expect(screen.getByRole('tab', { name: /Goederen/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByLabelText('Omschrijving goederen')).toBeInTheDocument()

    // The failing field carries the inline error + aria-invalid.
    expect(screen.getByLabelText('Omschrijving goederen')).toHaveAttribute('aria-invalid', 'true')
  })

  it('marks the customer field invalid when no customer is selected', async () => {
    const { onSubmit } = renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Opdracht aanmaken' }))
    expect(onSubmit).not.toHaveBeenCalled()

    expect(screen.getByLabelText('Klant *')).toHaveAttribute('aria-invalid', 'true')
    const summary = document.querySelector('.ui-validation-summary') as HTMLElement
    expect(within(summary).getByText('Selecteer een klant.')).toBeInTheDocument()
  })
})

describe('TransportOrderForm stop disclosure (Wave 1 §12)', () => {
  it('replaces the stop type select with a static chip', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    expect(screen.queryByLabelText('Stoptype')).not.toBeInTheDocument()
    // The chip (exact text) — the legend reads "1. Laden" and therefore never matches exactly.
    expect(screen.getAllByText('Laden')).toHaveLength(1)
    expect(screen.getAllByText('Lossen')).toHaveLength(1)
  })

  it('keeps the advanced stop fields functional but hidden behind a collapsed "Geavanceerd" disclosure', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))

    // Default view: the 7 controls are visible.
    expect(screen.getByLabelText('Laaddatum')).toBeVisible()
    expect(screen.getAllByLabelText('Tijdseis')).toHaveLength(2)
    expect(screen.getAllByLabelText('Instructies')).toHaveLength(2)

    // Advanced fields exist (round-trip) but sit inside a closed <details>.
    const advanced = document.querySelectorAll('details.tof-stop-extended')
    expect(advanced).toHaveLength(2)
    for (const details of advanced) {
      expect(details.hasAttribute('open')).toBe(false)
    }
    expect(screen.getAllByLabelText('Gevraagd van')[0]).not.toBeVisible()
    expect(screen.getAllByText('Afspraak verplicht')[0]).not.toBeVisible()

    // Opening the disclosure reveals them.
    advanced[0].setAttribute('open', '')
    expect(screen.getAllByLabelText('Gevraagd van')[0]).toBeVisible()
    expect(screen.getAllByLabelText('Vroegst toegelaten')[0]).toBeVisible()
  })
})

describe('TransportOrderForm cargo disclosure (Wave 1 §12)', () => {
  it('shows the four default line fields + ADR and keeps the rest behind "Meer details"', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))

    // Default view: omschrijving, verwacht aantal, eenheid, totaal gewicht + ADR checkbox.
    expect(screen.getByLabelText('Omschrijving')).toBeVisible()
    expect(screen.getByLabelText('Verwacht aantal *')).toBeVisible()
    expect(screen.getByLabelText('Totaal gewicht (kg)')).toBeVisible()
    expect(screen.getByText('ADR-goederen')).toBeVisible()

    // Everything else is inside the collapsed "Meer details".
    expect(screen.getByText('Meer details')).toBeInTheDocument()
    const details = document.querySelector('details.tof-stop-details')
    expect(details).toBeTruthy()
    expect(details!.hasAttribute('open')).toBe(false)
    expect(screen.getByLabelText('Barcode')).not.toBeVisible()
    expect(screen.getByLabelText('Verpakkingstype')).not.toBeVisible()

    // Opening the disclosure reveals them.
    details!.setAttribute('open', '')
    expect(screen.getByLabelText('Barcode')).toBeVisible()
    expect(screen.getByLabelText('Gewicht per stuk (kg)')).toBeVisible()
  })

  it('hides the stop-pinning selects while there is only one stop of each side', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))

    // One loading + one unloading stop: linkage is automatic, no pinning offered.
    expect(screen.queryByLabelText('Laadstop')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Losstop')).not.toBeInTheDocument()

    // A second unloading stop makes the losstop pinning relevant.
    await userEvent.click(screen.getByRole('tab', { name: /Route & stops/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Losstop' }))
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    expect(screen.queryByLabelText('Laadstop')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Losstop')).toBeInTheDocument()
  })
})

describe('TransportOrderForm derived volume (Wave 1 §12 audit fix)', () => {
  it('multiplies the per-piece volume by the expected quantity in the Lading summary', async () => {
    renderForm()
    await waitFor(() => expect(screen.getByLabelText('Klant *')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('tab', { name: /Goederen/ }))
    await userEvent.click(screen.getByRole('button', { name: '+ Goederenlijn' }))

    const qty = screen.getByLabelText('Verwacht aantal *')
    await userEvent.clear(qty)
    await userEvent.type(qty, '3')

    // Manual per-piece volume of 2 m³ (fields live inside "Meer details", still reachable).
    await userEvent.click(screen.getByRole('checkbox', { name: 'Handmatig' }))
    await userEvent.type(screen.getByLabelText('Volume per stuk (m³)'), '2')

    // 3 stuks × 2 m³/stuk → 6 m³ (the old summary showed 2 m³).
    expect(screen.getByText(/Volume: 6 m³/)).toBeInTheDocument()
  })
})
