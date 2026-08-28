import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PricingImportProfilePanel } from '../PricingImportProfilePanel'
import type { PricingImportProfile } from '../../api/pricingImportApi'

const api = vi.hoisted(() => ({
  readPricingImportHeaders: vi.fn(),
  listPricingImportFields: vi.fn(),
  createPricingImportProfile: vi.fn(),
  updatePricingImportProfile: vi.fn(),
  deletePricingImportProfile: vi.fn(),
}))
vi.mock('../../api/pricingImportApi', async (orig) => ({
  ...(await orig<typeof import('../../api/pricingImportApi')>()),
  readPricingImportHeaders: api.readPricingImportHeaders,
  listPricingImportFields: api.listPricingImportFields,
  createPricingImportProfile: api.createPricingImportProfile,
  updatePricingImportProfile: api.updatePricingImportProfile,
  deletePricingImportProfile: api.deletePricingImportProfile,
}))
const auth = vi.hoisted(() => ({ permissions: new Set<string>(['tariffs.manage']) }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

const file = new File(['x'], 'klant.xlsx')
const headersResult = {
  headers: ['Artikel', 'Prijs', 'Van', 'Tot'],
  fields: [
    { key: 'naam', standardHeader: 'Naam', required: true },
    { key: 'basis', standardHeader: 'Basis', required: true },
    { key: 'staffelprijs', standardHeader: 'Staffelprijs', required: false },
  ],
}
const existing: PricingImportProfile = {
  id: 'p1', name: 'Klant A layout', notes: null, headerRow: 1, sheetName: null, isActive: true,
  mapping: { naam: 'Artikel', basis: 'Soort' },
}

beforeEach(() => {
  auth.permissions = new Set(['tariffs.manage'])
  api.readPricingImportHeaders.mockReset().mockResolvedValue(headersResult)
  api.listPricingImportFields.mockReset().mockResolvedValue(headersResult.fields)
  api.createPricingImportProfile.mockReset().mockImplementation((input) => Promise.resolve({ id: 'p-new', ...input }))
  api.updatePricingImportProfile.mockReset().mockImplementation((id, input) => Promise.resolve({ id, ...input }))
  api.deletePricingImportProfile.mockReset().mockResolvedValue(undefined)
})

describe('PricingImportProfilePanel', () => {
  it('reads the workbook columns, maps business-labelled fields, and refuses to save without the required ones', async () => {
    const onProfilesChanged = vi.fn()
    render(<PricingImportProfilePanel file={file} profile={null} onProfilesChanged={onProfilesChanged} onMessage={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Kolommen uit bestand lezen' }))
    await screen.findByText('4 kolommen gevonden.')
    // Business labels, not field keys; required fields are marked.
    expect(screen.getByText('Naam van de regel')).toBeInTheDocument()
    expect(screen.getByText('Prijsbasis')).toBeInTheDocument()
    expect(screen.getByText('Staffelprijs')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Profielnaam/), 'Layout klant B')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Naam van de regel' }), 'Artikel')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan als nieuw profiel' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Verplichte velden zonder kolom: Prijsbasis')
    expect(api.createPricingImportProfile).not.toHaveBeenCalled()

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Prijsbasis' }), 'Van')
    await userEvent.click(screen.getByRole('button', { name: 'Opslaan als nieuw profiel' }))

    await waitFor(() => expect(api.createPricingImportProfile).toHaveBeenCalledWith(expect.objectContaining({
      name: 'Layout klant B', headerRow: 1, sheetName: null, isActive: true, mapping: { naam: 'Artikel', basis: 'Van' },
    })))
    expect(onProfilesChanged).toHaveBeenCalledWith(expect.objectContaining({ id: 'p-new' }))
  })

  it('updates (rename + remap) and deletes an existing profile without ever showing JSON', async () => {
    const onProfilesChanged = vi.fn()
    const onMessage = vi.fn()
    render(<PricingImportProfilePanel file={file} profile={existing} onProfilesChanged={onProfilesChanged} onMessage={onMessage} />)

    const name = screen.getByLabelText(/Profielnaam/)
    expect(name).toHaveValue('Klant A layout')
    await userEvent.clear(name)
    await userEvent.type(name, 'Klant A layout v2')
    await userEvent.click(screen.getByRole('button', { name: 'Kolommen uit bestand lezen' }))
    await screen.findByText('4 kolommen gevonden.')
    // The stored header "Soort" is not in this file but stays selected — nothing silently disappears.
    expect(screen.getByRole('combobox', { name: 'Prijsbasis' })).toHaveValue('Soort')

    await userEvent.click(screen.getByRole('button', { name: 'Profiel bijwerken' }))
    await waitFor(() => expect(api.updatePricingImportProfile).toHaveBeenCalledWith('p1', expect.objectContaining({
      name: 'Klant A layout v2', mapping: { naam: 'Artikel', basis: 'Soort' },
    })))
    expect(onMessage).toHaveBeenCalledWith("Profiel 'Klant A layout v2' bijgewerkt.")
    expect(document.body.textContent).not.toContain('{')

    await userEvent.click(screen.getByRole('button', { name: 'Profiel verwijderen' }))
    await screen.findByText('Profiel verwijderen?')
    await userEvent.click(screen.getAllByRole('button', { name: 'Profiel verwijderen' }).at(-1)!)
    await waitFor(() => expect(api.deletePricingImportProfile).toHaveBeenCalledWith('p1'))
    expect(onProfilesChanged).toHaveBeenLastCalledWith(null)
  })

  it('shows the mapping table from the canonical fields on mount, without a workbook', async () => {
    render(<PricingImportProfilePanel file={null} profile={existing} onProfilesChanged={vi.fn()} onMessage={vi.fn()} />)

    // No file, no "read columns" click — the saved mapping is reviewable right away.
    expect(await screen.findByRole('combobox', { name: 'Naam van de regel' })).toHaveValue('Artikel')
    expect(screen.getByRole('combobox', { name: 'Prijsbasis' })).toHaveValue('Soort')
    expect(api.listPricingImportFields).toHaveBeenCalledTimes(1)
    expect(api.readPricingImportHeaders).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Kolommen uit bestand lezen' })).toBeDisabled()
  })

  it('reads the columns with the header row and sheet the operator typed, and flags mapped headers absent from the file', async () => {
    render(<PricingImportProfilePanel file={file} profile={existing} onProfilesChanged={vi.fn()} onMessage={vi.fn()} />)

    const headerRow = screen.getByLabelText(/Kopregel/)
    await userEvent.clear(headerRow)
    await userEvent.type(headerRow, '3')
    await userEvent.type(screen.getByLabelText(/Werkblad/), 'Tarieven 2026')
    await userEvent.click(screen.getByRole('button', { name: 'Kolommen uit bestand lezen' }))
    await screen.findByText('4 kolommen gevonden.')

    expect(api.readPricingImportHeaders).toHaveBeenCalledWith(file, { profileId: 'p1', headerRow: 3, sheetName: 'Tarieven 2026' })
    // "Soort" is mapped but not one of the file's headers: kept selectable, but marked.
    const basis = screen.getByRole('combobox', { name: 'Prijsbasis' })
    expect(basis).toHaveValue('Soort')
    expect(within(basis).getByRole('option', { name: 'Soort (niet in dit bestand)' })).toBeInTheDocument()
    expect(within(basis).getByRole('option', { name: 'Artikel' })).toBeInTheDocument()
  })

  it('is read-only without tariffs.manage: no save/update/delete buttons, inputs disabled', async () => {
    auth.permissions = new Set(['tariffs.import'])
    render(<PricingImportProfilePanel file={file} profile={existing} onProfilesChanged={vi.fn()} onMessage={vi.fn()} />)

    await screen.findByRole('combobox', { name: 'Naam van de regel' })
    expect(screen.getByRole('note')).toHaveTextContent('alleen-lezen')
    expect(screen.queryByRole('button', { name: 'Profiel bijwerken' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Opslaan als nieuw profiel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Profiel verwijderen' })).not.toBeInTheDocument()
    expect(screen.getByLabelText(/Profielnaam/)).toBeDisabled()
    expect(screen.getByRole('combobox', { name: 'Prijsbasis' })).toBeDisabled()
  })
})
