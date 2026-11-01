import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const auth = vi.hoisted(() => ({ permissions: [] as string[], customerId: null as string | null }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    user: { customerId: auth.customerId, permissions: auth.permissions },
    hasPermission: (code: string) => auth.permissions.includes(code),
  }),
}))

import { HelpCenterPage, CustomerHelpPage } from '../pages/HelpCenterPage'

function renderAt(url: string, page = <HelpCenterPage />, base = '/help') {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path={base} element={page} />
        <Route path={`${base}/:articleId`} element={page} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('HelpCenterPage', () => {
  beforeEach(() => { auth.permissions = ['planning.edit', 'orders.create']; auth.customerId = null })

  it('shows the planner their own quickstart and the planbord first, not the HR quickstart', () => {
    renderAt('/help')
    expect(screen.getByRole('link', { name: /Snel starten: Planner/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Snel starten: HR/ })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Het planbord/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volledige handleiding (PDF)' })).toHaveAttribute('href', '/manual-assets/TMS-Complete-Gebruikershandleiding.pdf')
  })

  it('searches the articles', async () => {
    renderAt('/help')
    await userEvent.type(screen.getByLabelText('Zoek in de handleidingen'), 'herlevering')
    expect(await screen.findByRole('link', { name: /Een herlevering maken/ })).toBeInTheDocument()
  })

  it('opens an article with its sections, screenshots with alt text and next/previous', () => {
    renderAt('/help/planbord')
    expect(screen.getByRole('heading', { name: 'Het planbord' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'STAPPEN' })).toBeInTheDocument()
    expect(screen.getByAltText(/drie zones/)).toHaveAttribute('src', '/manual-assets/img/planbord.png')
    expect(screen.getByRole('link', { name: /Volgende/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Vorige/ })).toBeInTheDocument()
  })

  it('answers an unknown or forbidden article plainly', () => {
    renderAt('/help/back-up')
    expect(screen.getByRole('heading', { name: 'Dit artikel bestaat niet' })).toBeInTheDocument()
  })

  it('shows a link to an article this reader cannot open as plain text', () => {
    auth.permissions = ['customer_portal.view']; auth.customerId = 'c1'
    renderAt('/klantportaal/help/probleemoplossing', <CustomerHelpPage />, '/klantportaal/help')
    expect(screen.queryByRole('link', { name: 'Waarschuwingen en blokkeringen' })).not.toBeInTheDocument()
    expect(screen.getByText('Waarschuwingen en blokkeringen')).toBeInTheDocument()
  })

  it('gives a customer only customer help and the portal PDF', () => {
    auth.permissions = ['customer_portal.view']; auth.customerId = 'c1'
    renderAt('/klantportaal/help', <CustomerHelpPage />, '/klantportaal/help')
    expect(screen.getByRole('link', { name: /Snel starten: Klant/ })).toHaveAttribute('href', '/klantportaal/help/start-klant')
    expect(screen.queryByRole('button', { name: /Planning/ })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Handleiding klantportaal (PDF)' })).toBeInTheDocument()
  })
})
