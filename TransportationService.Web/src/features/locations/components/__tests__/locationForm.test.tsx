import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LocationForm } from '../LocationForm'
import { EMPTY_LOCATION_INPUT, type LocationInput } from '../../types'

// Mutable permission set so each test controls what useAuth() reports.
const auth = vi.hoisted(() => ({ permissions: [] as string[] }))

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

const api = vi.hoisted(() => ({
  searchCustomers: vi.fn(),
  getCustomer: vi.fn(),
}))
vi.mock('../../../customers/api/customersApi', () => api)

vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

// The guard needs the data router; the form's behaviour under test does not.
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: () => null,
}))

/** Matches a FormField label exactly, ignoring the trailing required-asterisk decoration. */
const nameField = () => screen.getByLabelText((text) => text.replace(/\s*\*\s*$/, '') === 'Naam')

const goTo = (section: RegExp) => userEvent.click(screen.getByRole('tab', { name: section }))

/** The action bar renders twice (top + sticky bottom); either submit button works. */
const clickSubmit = (label: string) => userEvent.click(screen.getAllByRole('button', { name: label })[0])

function renderForm(overrides: Partial<Parameters<typeof LocationForm>[0]> = {}) {
  const onSubmit = vi.fn()
  render(
    <LocationForm
      mode="create"
      initial={EMPTY_LOCATION_INPUT}
      submitting={false}
      onSubmit={onSubmit}
      onCancel={vi.fn()}
      {...overrides}
    />,
  )
  return onSubmit
}

beforeEach(() => {
  auth.permissions = []
  api.searchCustomers.mockReset().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 200 })
  api.getCustomer.mockReset()
})

describe('LocationForm (sectioned)', () => {
  it('shows the section rail with all seven sections', () => {
    renderForm()
    const tabs = screen.getAllByRole('tab')
    expect(tabs.map((t) => t.textContent)).toEqual([
      expect.stringContaining('Algemeen'),
      expect.stringContaining('Adres'),
      expect.stringContaining('Contact'),
      expect.stringContaining('Openingstijden'),
      expect.stringContaining('Operationeel'),
      expect.stringContaining('Instructies'),
      expect.stringContaining('Planning'),
    ])
  })

  it('maps the operational fields into the submit payload (gate, accessCode, adrAllowed, intervals)', async () => {
    auth.permissions = ['locations.view_sensitive']
    const onSubmit = renderForm()

    await userEvent.type(nameField(), 'Magazijn Gent')

    await goTo(/Operationeel/)
    await userEvent.type(screen.getByLabelText('Poort'), 'Poort 4')
    await userEvent.type(screen.getByLabelText('Toegangscode'), '1234#')
    await userEvent.selectOptions(screen.getByLabelText('ADR toegelaten'), 'true')

    await goTo(/Openingstijden/)
    await userEvent.click(screen.getByRole('button', { name: 'Tijdvak toevoegen (Ma)' }))

    await clickSubmit('Locatie aanmaken')

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const payload = onSubmit.mock.calls[0][0] as LocationInput
    expect(payload.name).toBe('Magazijn Gent')
    expect(payload.gate).toBe('Poort 4')
    expect(payload.accessCode).toBe('1234#')
    expect(payload.adrAllowed).toBe(true)
    expect(payload.openingIntervals).toEqual([{ dayOfWeek: 1, fromTime: '08:00', toTime: '17:00', note: null }])
  })

  it('keeps adrAllowed null (Onbekend) when untouched and maps "Nee" to false', async () => {
    const first = renderForm()
    await userEvent.type(nameField(), 'Depot')
    await goTo(/Operationeel/)
    await userEvent.selectOptions(screen.getByLabelText('ADR toegelaten'), 'false')
    await clickSubmit('Locatie aanmaken')
    expect((first.mock.calls[0][0] as LocationInput).adrAllowed).toBe(false)
  })

  it('hides the access code and omits it from the payload without locations.view_sensitive', async () => {
    auth.permissions = []
    const onSubmit = renderForm({
      mode: 'edit',
      initial: { ...EMPTY_LOCATION_INPUT, code: 'LOC-1', name: 'Depot Gent', accessCode: null },
    })

    await goTo(/Operationeel/)
    expect(screen.queryByLabelText('Toegangscode')).not.toBeInTheDocument()
    await clickSubmit('Opslaan')

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const payload = onSubmit.mock.calls[0][0] as LocationInput
    expect('accessCode' in payload).toBe(false)
  })

  it('requires only the name, badges + focuses the failing section, and blocks on invalid opening hours', async () => {
    const onSubmit = renderForm()

    // Submit while empty: error badge on Algemeen, focus jumps to the name field.
    await clickSubmit('Locatie aanmaken')
    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getAllByText('Naam is verplicht.').length).toBeGreaterThan(0)
    expect(screen.getByRole('tab', { name: /Algemeen/ })).toHaveAttribute('data-has-error', 'true')
    await waitFor(() => expect(nameField()).toHaveFocus())

    await userEvent.type(nameField(), 'Depot')
    await goTo(/Openingstijden/)
    await userEvent.click(screen.getByRole('button', { name: 'Tijdvak toevoegen (Di)' }))
    // Make the interval invalid: end before start.
    fireEvent.change(screen.getByLabelText('Tot (Di)'), { target: { value: '07:00' } })
    await clickSubmit('Locatie aanmaken')
    expect(screen.getAllByText('Corrigeer eerst de openingsuren.').length).toBeGreaterThan(0)
    expect(screen.getByRole('tab', { name: /Openingstijden/ })).toHaveAttribute('data-has-error', 'true')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('navigates to the failing section when a validation-summary entry is clicked', async () => {
    const onSubmit = renderForm()
    await goTo(/Planning/)
    await clickSubmit('Locatie aanmaken')
    expect(onSubmit).not.toHaveBeenCalled()

    await goTo(/Planning/) // walk away from the auto-activated section again
    await userEvent.click(screen.getByRole('button', { name: /Naam:/ }))

    expect(screen.getByRole('tab', { name: /Algemeen/ })).toHaveAttribute('aria-selected', 'true')
    await waitFor(() => expect(nameField()).toHaveFocus())
  })

  it('reports semantic section status: ✓ when vereiste velden geldig, ● when optionele data aanwezig', async () => {
    renderForm()
    const algemeenTab = () => screen.getByRole('tab', { name: /Algemeen/ })
    expect(algemeenTab().querySelector('.ui-section-tab-complete')).toBeNull()

    await userEvent.type(nameField(), 'Depot Gent')
    expect(algemeenTab().querySelector('.ui-section-tab-complete')).not.toBeNull()

    // Optional section: ○ while empty, ● once it holds data.
    const contactTab = () => screen.getByRole('tab', { name: /Contact/ })
    expect(contactTab().querySelector('.ui-section-tab-empty')).not.toBeNull()
    await goTo(/Contact/)
    await userEvent.type(screen.getByLabelText('Naam contactpersoon'), 'An Peeters')
    expect(contactTab().querySelector('.ui-section-tab-filled')).not.toBeNull()
    expect(contactTab().querySelector('.ui-section-tab-empty')).toBeNull()
  })

  it('offers every location type with Dutch labels, including the new ones', () => {
    renderForm()
    const typeSelect = screen.getByLabelText('Type') as HTMLSelectElement
    const labels = Array.from(typeSelect.options).map((o) => o.textContent)
    expect(labels).toContain('Werf')
    expect(labels).toContain('Tijdelijke locatie')
    expect(labels).toContain('Overig')
    expect(labels).toContain('Maatschappelijke zetel')
    expect(labels).toHaveLength(16)
  })

  it('shows the auto-code helper on create only', () => {
    renderForm()
    expect(screen.getByText('Leeg laten voor automatische code.')).toBeInTheDocument()
  })

  it('prefills the on-site contact snapshot from a selected customer contact', async () => {
    api.getCustomer.mockResolvedValue({
      name: 'Alfa NV',
      contacts: [
        {
          id: 'ct-1',
          firstName: 'An',
          lastName: 'Peeters',
          displayName: null,
          nickname: null,
          role: null,
          departmentId: null,
          preferredLanguageCode: null,
          email: 'an@klant.be',
          phoneNumber: '03 123 45 67',
          mobilePhone: '0470 11 22 33',
          isPrimary: true,
          isActive: true,
          notes: null,
        },
      ],
    })
    const onSubmit = renderForm({ initial: { ...EMPTY_LOCATION_INPUT, customerId: 'cust-1' } })

    await waitFor(() => expect(api.getCustomer).toHaveBeenCalledWith('cust-1'))
    await userEvent.type(nameField(), 'Site klant')

    await goTo(/Contact/)
    await userEvent.selectOptions(await screen.findByLabelText(/Contactpersoon van klant/), 'ct-1')

    expect(screen.getByLabelText('Naam contactpersoon')).toHaveValue('An Peeters')
    expect(screen.getByLabelText('Telefoon')).toHaveValue('03 123 45 67')
    expect(screen.getByLabelText('Gsm')).toHaveValue('0470 11 22 33')
    expect(screen.getByLabelText(/E-mail/)).toHaveValue('an@klant.be')

    await clickSubmit('Locatie aanmaken')
    const payload = onSubmit.mock.calls[0][0] as LocationInput
    expect(payload.customerContactId).toBe('ct-1')
  })
})
