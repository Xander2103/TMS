import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LookupSelect } from '../components/LookupSelect'

const auth = vi.hoisted(() => ({ permissions: ['unit_types.manage'] }))

vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({
    options: [
      { id: 'ut-1', code: 'PALLET', name: 'Pallet' },
      { id: 'ut-2', code: 'KG', name: 'Kilogram' },
    ],
    isLoading: false,
    error: null,
  }),
}))

describe('LookupSelect with valueKey="code"', () => {
  it('returns the selected option code, not its id', async () => {
    const onChange = vi.fn()
    render(
      <LookupSelect
        basePath="/api/unit-types"
        managePermission="unit_types.manage"
        singular="eenheid"
        valueKey="code"
        value={null}
        onChange={onChange}
      />,
    )

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(screen.getByText('Kilogram'))

    expect(onChange).toHaveBeenCalledWith('KG')
    // id form would have returned 'ut-2' — assert we did NOT emit the id.
    expect(onChange).not.toHaveBeenCalledWith('ut-2')
  })
})
