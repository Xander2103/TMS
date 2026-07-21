import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { LegalEntitySwitcher } from '../components/LegalEntitySwitcher'
import { getActiveLegalEntity, getLegalEntityOptions, setActiveLegalEntity } from '../api/legalEntitiesApi'
import type { LegalEntityOption } from '../types'

vi.mock('../api/legalEntitiesApi', () => ({
  getLegalEntityOptions: vi.fn(),
  getActiveLegalEntity: vi.fn(),
  setActiveLegalEntity: vi.fn(),
}))

// Mutable permission set so each test controls what useAuth() reports.
const auth = vi.hoisted(() => ({ permissions: [] as string[] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))

const twoOptions: LegalEntityOption[] = [
  { id: 'entity-1', displayName: 'Acme BV', vatNumber: 'BE0123456789', isDefault: true, isActive: true },
  { id: 'entity-2', displayName: 'Beta NV', vatNumber: null, isDefault: false, isActive: true },
]

function renderSwitcher() {
  return render(
    <MemoryRouter>
      <LegalEntitySwitcher />
    </MemoryRouter>,
  )
}

describe('LegalEntitySwitcher', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = []
  })

  it('renders the active options and marks the default entity', async () => {
    vi.mocked(getLegalEntityOptions).mockResolvedValue(twoOptions)
    vi.mocked(getActiveLegalEntity).mockResolvedValue({ legalEntityId: 'entity-2' })

    renderSwitcher()

    const select = await screen.findByLabelText('Actief bedrijf')
    expect(select).toHaveValue('entity-2')
    expect(screen.getByRole('option', { name: 'Acme BV (standaard)' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Beta NV' })).toBeInTheDocument()
  })

  it('persists the selection via setActiveLegalEntity', async () => {
    vi.mocked(getLegalEntityOptions).mockResolvedValue(twoOptions)
    vi.mocked(getActiveLegalEntity).mockResolvedValue({ legalEntityId: 'entity-1' })
    vi.mocked(setActiveLegalEntity).mockResolvedValue({ legalEntityId: 'entity-2' })

    renderSwitcher()

    const select = await screen.findByLabelText('Actief bedrijf')
    await userEvent.selectOptions(select, 'entity-2')

    expect(setActiveLegalEntity).toHaveBeenCalledWith('entity-2')
    expect(select).toHaveValue('entity-2')
  })

  it('renders nothing with a single option and no manage permission', async () => {
    vi.mocked(getLegalEntityOptions).mockResolvedValue([twoOptions[0]])
    vi.mocked(getActiveLegalEntity).mockResolvedValue({ legalEntityId: null })

    const { container } = renderSwitcher()

    await waitFor(() => expect(getLegalEntityOptions).toHaveBeenCalled())
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })
})
