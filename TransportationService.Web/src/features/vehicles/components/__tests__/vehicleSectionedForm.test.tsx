import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { VehicleForm } from '../VehicleForm'
import type { CreateVehicleInput } from '../../types'

// The real UnsavedChangesGuard is deliberately NOT mocked: switching sections must be
// internal form navigation and must never pop the unsaved-changes modal.

vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))

const EMPTY: CreateVehicleInput = {
  licensePlate: '',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  fuelType: 'Diesel',
  emissionClass: null,
  grossVehicleWeightKg: null,
  payloadKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  volumeIsManual: false,
  odometerKm: 0,
  consumptionLPer100Km: null,
  axleCount: 0,
  loadingMeters: 0,
  requiredLicenceCode: null,
  hasCrane: false,
  hasRefrigeration: false,
  hasTailLift: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  fixedDriverId: null,
  currentDriverId: null,
  notes: null,
}

function renderForm(onSubmit = vi.fn()) {
  const router = createMemoryRouter(
    [
      {
        path: '/vehicles/new',
        element: (
          <VehicleForm mode="create" initial={EMPTY} isSubmitting={false} onSubmit={onSubmit} onCancel={vi.fn()} drivers={[]} />
        ),
      },
    ],
    { initialEntries: ['/vehicles/new'] },
  )
  render(<RouterProvider router={router} />)
  return { onSubmit }
}

const MODAL_TEXT = /wijzigingen die nog niet zijn opgeslagen/i

describe('VehicleForm sections', () => {
  it('renders all sections as tabs', () => {
    renderForm()
    for (const label of ['Algemeen', 'Registratie', 'Capaciteit & afmetingen', 'Techniek', 'Documenten', 'Onderhoud & keuringen', 'Toewijzing', 'Notities']) {
      expect(screen.getByRole('tab', { name: new RegExp(label) })).toBeInTheDocument()
    }
  })

  it('keeps values and never triggers the unsaved-changes modal when switching tabs', async () => {
    renderForm()
    await userEvent.type(screen.getByLabelText(/Kenteken/), '1-ABC-123')

    await userEvent.click(screen.getByRole('tab', { name: /Techniek/ }))
    expect(screen.getByLabelText('Brandstof')).toBeInTheDocument()
    expect(screen.queryByText(MODAL_TEXT)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
    expect(screen.getByLabelText(/Kenteken/)).toHaveValue('1-ABC-123')
    expect(screen.queryByText(MODAL_TEXT)).not.toBeInTheDocument()
  })

  it('routes to the section owning the first validation error on submit', async () => {
    const { onSubmit } = renderForm()
    // Move away from Algemeen, then submit with an empty licence plate.
    await userEvent.click(screen.getByRole('tab', { name: /Notities/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Voertuig aanmaken' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByRole('tab', { name: /Algemeen/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('Kenteken is verplicht.')).toBeInTheDocument()
  })
})
