import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom'
import { LocationsPage } from '../LocationsPage'
import type { LocationListItem } from '../../types'

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

const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => toast,
}))

const api = vi.hoisted(() => ({
  searchLocations: vi.fn(),
  duplicateLocation: vi.fn(),
  setLocationActive: vi.fn(),
}))
vi.mock('../../api/locationsApi', () => api)

vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

function row(overrides: Partial<LocationListItem> = {}): LocationListItem {
  return {
    id: 'loc-1',
    code: 'LOC-AAA11111',
    name: 'Depot Antwerpen',
    type: 'ConstructionSite',
    city: 'Antwerpen',
    countryCode: 'BE',
    customerName: null,
    isActive: true,
    isDefaultLoadingLocation: false,
    isDefaultUnloadingLocation: false,
    isDefaultBillingLocation: false,
    ...overrides,
  }
}

function DetailStub() {
  const { id } = useParams()
  return <div>detail-{id}</div>
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/locations']}>
      <Routes>
        <Route path="/locations" element={<LocationsPage />} />
        <Route path="/locations/:id" element={<DetailStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.permissions = ['locations.create', 'locations.edit']
  toast.showSuccess.mockReset()
  toast.showError.mockReset()
  api.searchLocations.mockReset().mockResolvedValue({ items: [row()], totalCount: 1 })
  api.duplicateLocation.mockReset()
  api.setLocationActive.mockReset()
})

describe('LocationsPage', () => {
  it('shows the Dutch type label for the new location types', async () => {
    renderPage()
    expect(await screen.findByText('Depot Antwerpen')).toBeInTheDocument()
    // "Werf" also exists as a filter option; assert on the table cell specifically.
    expect(within(screen.getByRole('table')).getByText('Werf')).toBeInTheDocument()
  })

  it('duplicates a location and navigates to the copy', async () => {
    api.duplicateLocation.mockResolvedValue({ id: 'copy-1', code: 'LOC-BBB22222' })
    renderPage()
    await screen.findByText('Depot Antwerpen')

    await userEvent.click(screen.getByRole('button', { name: 'Dupliceren' }))

    expect(api.duplicateLocation).toHaveBeenCalledWith('loc-1')
    expect(await screen.findByText('detail-copy-1')).toBeInTheDocument()
    expect(toast.showSuccess).toHaveBeenCalledWith('Locatie gedupliceerd.')
  })

  it('deactivates an active location from the row actions', async () => {
    api.setLocationActive.mockResolvedValue(undefined)
    renderPage()
    await screen.findByText('Depot Antwerpen')

    await userEvent.click(screen.getByRole('button', { name: 'Deactiveren' }))

    expect(api.setLocationActive).toHaveBeenCalledWith('loc-1', false)
    expect(toast.showSuccess).toHaveBeenCalledWith('Locatie gedeactiveerd.')
  })

  it('offers "Activeren" for inactive locations', async () => {
    api.searchLocations.mockResolvedValue({ items: [row({ isActive: false })], totalCount: 1 })
    api.setLocationActive.mockResolvedValue(undefined)
    renderPage()
    await screen.findByText('Depot Antwerpen')

    await userEvent.click(screen.getByRole('button', { name: 'Activeren' }))
    expect(api.setLocationActive).toHaveBeenCalledWith('loc-1', true)
  })

  it('hides the row actions without create/edit permissions', async () => {
    auth.permissions = []
    renderPage()
    await screen.findByText('Depot Antwerpen')
    expect(screen.queryByRole('button', { name: 'Dupliceren' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactiveren' })).not.toBeInTheDocument()
  })
})
