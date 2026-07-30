import { describe, expect, it, beforeEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { EdiPage } from '../EdiPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))

vi.mock('../../components/DashboardTab', () => ({ DashboardTab: () => <div>DashboardTabStub</div> }))
vi.mock('../../components/MessagesTab', () => ({ MessagesTab: () => <div>MessagesTabStub</div> }))
vi.mock('../../components/PartnersTab', () => ({ PartnersTab: () => <div>PartnersTabStub</div> }))
vi.mock('../../components/MappingsTab', () => ({ MappingsTab: () => <div>MappingsTabStub</div> }))
vi.mock('../../components/TestenTab', () => ({ TestenTab: () => <div>TestenTabStub</div> }))

function renderPage(initialEntry = '/edi') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <EdiPage />
    </MemoryRouter>,
  )
}

describe('EdiPage', () => {
  beforeEach(() => {
    auth.permissions = new Set()
  })

  it('shows a permission placeholder without edi.view or edi.manage', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om EDI te bekijken.')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('with only edi.view: shows Dashboard/Berichten but not Handelspartners/Mappings/Testen', () => {
    auth.permissions = new Set(['edi.view'])
    renderPage()

    expect(screen.getByRole('tab', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Berichten' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Handelspartners' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Mappings' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Testen' })).not.toBeInTheDocument()
  })

  it('adds the Testen tab with edi.test', () => {
    auth.permissions = new Set(['edi.view', 'edi.test'])
    renderPage()
    expect(screen.getByRole('tab', { name: 'Testen' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Handelspartners' })).not.toBeInTheDocument()
  })

  it('adds Handelspartners, Mappings and Testen with edi.manage alone', () => {
    auth.permissions = new Set(['edi.manage'])
    renderPage()
    expect(screen.getByRole('tab', { name: 'Handelspartners' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Mappings' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Testen' })).toBeInTheDocument()
  })

  it('falls back to Dashboard when ?tab= names a tab the user cannot see', () => {
    auth.permissions = new Set(['edi.view'])
    renderPage('/edi?tab=handelspartners')

    expect(screen.getByRole('tab', { name: 'Dashboard', selected: true })).toBeInTheDocument()
    expect(screen.getByText('DashboardTabStub')).toBeInTheDocument()
  })

  it('honours a valid ?tab= deep link', () => {
    auth.permissions = new Set(['edi.manage'])
    renderPage('/edi?tab=mappings')

    expect(screen.getByRole('tab', { name: 'Mappings', selected: true })).toBeInTheDocument()
    expect(screen.getByText('MappingsTabStub')).toBeInTheDocument()
  })
})
