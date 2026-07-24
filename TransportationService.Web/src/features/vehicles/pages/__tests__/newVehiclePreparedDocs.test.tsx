import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { NewVehiclePage } from '../NewVehiclePage'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../drivers/api/driversApi', () => ({
  searchDrivers: () => Promise.resolve({ items: [], total: 0 }),
}))

const calls = vi.hoisted(() => ({ order: [] as string[] }))
const createVehicleMock = vi.hoisted(() =>
  vi.fn(() => {
    calls.order.push('create-vehicle')
    return Promise.resolve({ id: 'veh-1', internalNumber: 'VRT-0001' })
  }),
)
const createDocMock = vi.hoisted(() =>
  vi.fn(() => {
    calls.order.push('create-doc')
    return Promise.resolve({ id: 'doc-1' })
  }),
)
const uploadMock = vi.hoisted(() =>
  vi.fn(() => {
    calls.order.push('upload-file')
    return Promise.resolve({ id: 'doc-1' })
  }),
)

vi.mock('../../api/vehiclesApi', () => ({
  createVehicle: createVehicleMock,
}))
vi.mock('../../../fleet-documents/api/fleetDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../../../fleet-documents/api/fleetDocumentsApi')>(
    '../../../fleet-documents/api/fleetDocumentsApi',
  )
  return {
    ...actual,
    createFleetDocument: createDocMock,
    uploadFleetDocumentFile: uploadMock,
  }
})

function renderPage() {
  const router = createMemoryRouter(
    [
      { path: '/vehicles/new', element: <NewVehiclePage /> },
      { path: '/vehicles/:id', element: <div>Detailpagina</div> },
      { path: '/vehicles', element: <div>Lijst</div> },
    ],
    { initialEntries: ['/vehicles/new'] },
  )
  render(<RouterProvider router={router} />)
}

async function stageDocumentAndSubmit() {
  await userEvent.click(screen.getByRole('tab', { name: /Documenten/ }))
  const file = new File(['pdf-bytes'], 'leasingcontract.pdf', { type: 'application/pdf' })
  const input = document.querySelector<HTMLInputElement>('input[type="file"][multiple]')!
  await userEvent.upload(input, file)
  await screen.findByText('leasingcontract.pdf')

  await userEvent.click(screen.getByRole('tab', { name: /Algemeen/ }))
  await userEvent.type(screen.getByLabelText(/Kenteken/), '1-ABC-123')
  await userEvent.click(screen.getByRole('button', { name: 'Voertuig aanmaken' }))
}

describe('NewVehiclePage prepared documents', () => {
  beforeEach(() => {
    calls.order = []
    createVehicleMock.mockClear()
    createDocMock.mockClear()
    uploadMock.mockClear()
  })

  it('creates the vehicle FIRST, then persists staged documents (no orphans on failure)', async () => {
    renderPage()
    await stageDocumentAndSubmit()

    await waitFor(() => expect(uploadMock).toHaveBeenCalled())
    expect(calls.order).toEqual(['create-vehicle', 'create-doc', 'upload-file'])
    await screen.findByText('Detailpagina')
  })

  it('does not touch document endpoints when vehicle creation fails', async () => {
    createVehicleMock.mockImplementationOnce(() => {
      calls.order.push('create-vehicle-failed')
      return Promise.reject(new Error('kapot'))
    })
    renderPage()
    await stageDocumentAndSubmit()

    await screen.findByRole('alert')
    expect(createDocMock).not.toHaveBeenCalled()
    expect(uploadMock).not.toHaveBeenCalled()
  })

  it('offers retry for failed uploads without duplicating the created metadata record', async () => {
    uploadMock.mockImplementationOnce(() => {
      calls.order.push('upload-failed')
      return Promise.reject(new Error('netwerkfout'))
    })
    renderPage()
    await stageDocumentAndSubmit()

    await screen.findByText(/documenten deels mislukt/i)
    expect(createDocMock).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('button', { name: /Mislukte opnieuw proberen/ }))
    await screen.findByText('Detailpagina')
    // Retry only re-uploaded the file — the metadata record was reused, not recreated.
    expect(createDocMock).toHaveBeenCalledTimes(1)
    expect(uploadMock).toHaveBeenCalledTimes(2)
  })
})
