import { describe, expect, it, beforeEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PeppolPage } from '../PeppolPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../../components/OverviewTab', () => ({ OverviewTab: () => <div>OverviewTabStub</div> }))
vi.mock('../../components/OutgoingTab', () => ({ OutgoingTab: () => <div>OutgoingTabStub</div> }))
vi.mock('../../components/IncomingTab', () => ({ IncomingTab: () => <div>IncomingTabStub</div> }))
vi.mock('../../components/SettingsTab', () => ({ SettingsTab: () => <div>SettingsTabStub</div> }))
vi.mock('../../components/ValidationTab', () => ({ ValidationTab: () => <div>ValidationTabStub</div> }))

function renderPage(initialEntry = '/peppol') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <PeppolPage />
    </MemoryRouter>,
  )
}

describe('PeppolPage', () => {
  beforeEach(() => {
    auth.permissions = new Set()
  })

  it('shows a permission placeholder without peppol.view', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om Peppol te bekijken.')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('hides the Inkomend tab without peppol.view_incoming', () => {
    auth.permissions = new Set(['peppol.view'])
    renderPage()

    expect(screen.getByRole('tab', { name: 'Overzicht' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Uitgaand' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Configuratie' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Validatieproblemen' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Inkomend' })).not.toBeInTheDocument()
  })

  it('shows the Inkomend tab with peppol.view_incoming and honours the ?tab= deep link', () => {
    auth.permissions = new Set(['peppol.view', 'peppol.view_incoming'])
    renderPage('/peppol?tab=inkomend')

    expect(screen.getByRole('tab', { name: 'Inkomend', selected: true })).toBeInTheDocument()
    expect(screen.getByText('IncomingTabStub')).toBeInTheDocument()
  })

  it('falls back to Overzicht when ?tab= names a hidden tab', () => {
    auth.permissions = new Set(['peppol.view'])
    renderPage('/peppol?tab=inkomend')

    expect(screen.getByRole('tab', { name: 'Overzicht', selected: true })).toBeInTheDocument()
    expect(screen.getByText('OverviewTabStub')).toBeInTheDocument()
  })
})
