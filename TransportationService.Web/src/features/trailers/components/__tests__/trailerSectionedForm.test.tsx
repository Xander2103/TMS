import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { TrailerForm } from '../TrailerForm'
import type { TrailerInput } from '../../types'

vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))

const EMPTY: TrailerInput = {
  licensePlate: '',
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

function renderForm() {
  const router = createMemoryRouter(
    [
      {
        path: '/trailers/new',
        element: <TrailerForm mode="create" initial={EMPTY} isSubmitting={false} onSubmit={vi.fn()} onCancel={vi.fn()} />,
      },
    ],
    { initialEntries: ['/trailers/new'] },
  )
  render(<RouterProvider router={router} />)
}

describe('TrailerForm sections', () => {
  it('renders tabs and keeps values across tab switches without the unsaved modal', async () => {
    renderForm()
    for (const label of ['Algemeen', 'Registratie', 'Capaciteit & afmetingen', 'Techniek', 'Documenten', 'Onderhoud & keuringen', 'Notities']) {
      expect(screen.getByRole('tab', { name: new RegExp(label) })).toBeInTheDocument()
    }

    await userEvent.type(screen.getByLabelText(/Kenteken/), 'O-XYZ-9')
    await userEvent.click(screen.getByRole('tab', { name: /Capaciteit/ }))
    expect(screen.getByLabelText(/Laadvermogen/)).toBeInTheDocument()
    expect(screen.queryByText(/wijzigingen die nog niet zijn opgeslagen/i)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
    expect(screen.getByLabelText(/Kenteken/)).toHaveValue('O-XYZ-9')
  })
})
