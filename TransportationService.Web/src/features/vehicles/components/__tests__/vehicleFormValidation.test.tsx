import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { VehicleForm } from '../VehicleForm'
import { TrailerForm } from '../../../trailers/components/TrailerForm'
import type { CreateVehicleInput } from '../../types'
import type { TrailerInput } from '../../../trailers/types'

// FL-M5/M6: negative numbers are refused client-side, and lowering the odometer asks for a reason.

vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))

const VEHICLE: CreateVehicleInput = {
  licensePlate: '1-ABC-123',
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
  odometerKm: 120000,
  consumptionLPer100Km: null,
  axleCount: 2,
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

const TRAILER: TrailerInput = {
  licensePlate: 'O-XYZ-9',
  vin: null,
  categoryId: null,
  brand: null,
  model: null,
  year: null,
  firstRegistrationDate: null,
  capacityKg: null,
  lengthMeters: null,
  widthMeters: null,
  heightMeters: null,
  volumeM3: null,
  volumeIsManual: false,
  axleCount: 0,
  loadingMeters: 0,
  hasRefrigeration: false,
  adrSuitable: false,
  ownershipType: 'Owned',
  operationalStatus: 'Available',
  statusReason: null,
  isActive: true,
  notes: null,
}

function renderAt(element: React.ReactElement) {
  const router = createMemoryRouter([{ path: '/x', element }], { initialEntries: ['/x'] })
  render(<RouterProvider router={router} />)
}

function renderVehicle(mode: 'create' | 'edit', onSubmit = vi.fn(), submitError: string | null = null) {
  renderAt(<VehicleForm mode={mode} initial={VEHICLE} isSubmitting={false} submitError={submitError} onSubmit={onSubmit} onCancel={vi.fn()} />)
  return onSubmit
}

const submitButton = (name: RegExp) => screen.getByRole('button', { name })

describe('VehicleForm numeric validation', () => {
  it('refuses a negative payload with a message and does not submit', async () => {
    const onSubmit = renderVehicle('create')
    await userEvent.click(screen.getByRole('tab', { name: /Capaciteit/ }))
    const payload = document.getElementById('vf-payload') as HTMLInputElement
    expect(payload).toHaveAttribute('min', '0')
    await userEvent.type(payload, '-5')
    await userEvent.click(submitButton(/Voertuig aanmaken|Aanmaken/))
    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Deze waarde kan niet negatief zijn.')).toBeInTheDocument()
    expect(document.getElementById('vf-payload')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows the server message passed as submitError', () => {
    renderVehicle('edit', vi.fn(), 'Kilometerstand kan niet verlaagd worden zonder reden.')
    expect(screen.getByRole('alert')).toHaveTextContent('Kilometerstand kan niet verlaagd worden zonder reden.')
  })
})

describe('VehicleForm odometer correction', () => {
  it('asks for a reason when the odometer is lowered on edit and sends it', async () => {
    const onSubmit = renderVehicle('edit')
    await userEvent.click(screen.getByRole('tab', { name: /Techniek/ }))
    expect(document.getElementById('vf-odometer-reason')).toBeNull()

    const odometer = document.getElementById('vf-odometer') as HTMLInputElement
    await userEvent.clear(odometer)
    await userEvent.type(odometer, '119000')
    expect(document.getElementById('vf-odometer-reason')).not.toBeNull()

    await userEvent.click(submitButton(/Opslaan/))
    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Geef een reden op voor de lagere kilometerstand.')).toBeInTheDocument()

    await userEvent.type(document.getElementById('vf-odometer-reason') as HTMLInputElement, 'Teller vervangen')
    await userEvent.click(submitButton(/Opslaan/))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ odometerKm: 119000, odometerCorrectionReason: 'Teller vervangen' }))
  })

  it('does not ask for a reason when the odometer goes up, and sends no reason', async () => {
    const onSubmit = renderVehicle('edit')
    await userEvent.click(screen.getByRole('tab', { name: /Techniek/ }))
    const odometer = document.getElementById('vf-odometer') as HTMLInputElement
    await userEvent.clear(odometer)
    await userEvent.type(odometer, '125000')
    expect(document.getElementById('vf-odometer-reason')).toBeNull()
    await userEvent.click(submitButton(/Opslaan/))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ odometerKm: 125000, odometerCorrectionReason: null }))
  })
})

describe('TrailerForm numeric validation', () => {
  it('refuses a negative length', async () => {
    const onSubmit = vi.fn()
    renderAt(<TrailerForm mode="create" initial={TRAILER} isSubmitting={false} onSubmit={onSubmit} onCancel={vi.fn()} />)
    await userEvent.click(screen.getByRole('tab', { name: /Capaciteit/ }))
    const length = document.getElementById('tf-length') as HTMLInputElement
    expect(length).toHaveAttribute('min', '0')
    await userEvent.type(length, '-2')
    await userEvent.click(screen.getByRole('button', { name: /aanmaken/i }))
    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('Deze waarde kan niet negatief zijn.')).toBeInTheDocument()
  })
})
