import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PricingImportProfilePanel } from '../PricingImportProfilePanel'
import type { PricingImportProfile } from '../../api/pricingImportApi'

const api = vi.hoisted(() => ({
  readPricingImportHeaders: vi.fn(),
  createPricingImportProfile: vi.fn(),
  updatePricingImportProfile: vi.fn(),
  deletePricingImportProfile: vi.fn(),
}))
vi.mock('../../api/pricingImportApi', async (orig) => ({
  ...(await orig<typeof import('../../api/pricingImportApi')>()),
  readPricingImportHeaders: api.readPricingImportHeaders,
  createPricingImportProfile: api.createPricingImportProfile,
  updatePricingImportProfile: api.updatePricingImportProfile,
  deletePricingImportProfile: api.deletePricingImportProfile,
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
  api.readPricingImportHeaders.mockReset().mockResolvedValue(headersResult)
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
})
