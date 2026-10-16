import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import * as api from '../../api/employeesApi'
import { EmployeesPage } from '../EmployeesPage'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))

function Url() {
  const location = useLocation()
  return <output data-testid="url">{location.pathname + location.search}</output>
}

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
  vi.spyOn(api, 'searchEmployees').mockResolvedValue({ items: [], totalCount: 60, page: 2, pageSize: 25 })
})

// HR acceptance P2-19: employee → Back must restore the search text and page.
describe('EmployeesPage URL state', () => {
  it('restores search and page from the URL', async () => {
    render(
      <MemoryRouter initialEntries={['/employees?q=TEST%20HR&page=2']}>
        <Routes><Route path="/employees" element={<EmployeesPage />} /></Routes>
      </MemoryRouter>,
    )
    expect(screen.getByRole('searchbox')).toHaveValue('TEST HR')
    await waitFor(() =>
      expect(api.searchEmployees).toHaveBeenCalledWith(expect.objectContaining({ search: 'TEST HR', page: 2 })),
    )
  })

  it('writes typed search into the URL and returns to page 1', async () => {
    render(
      <MemoryRouter initialEntries={['/employees?page=3']}>
        <Routes><Route path="/employees" element={<><EmployeesPage /><Url /></>} /></Routes>
      </MemoryRouter>,
    )
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'Jan' } })
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/employees?q=Jan'))
  })
})
