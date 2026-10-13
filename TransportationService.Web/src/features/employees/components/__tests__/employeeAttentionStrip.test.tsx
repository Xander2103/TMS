import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { EmployeeAttentionStrip } from '../EmployeeAttentionStrip'
import type { EmployeeAttention } from '../../types/employee'

// The strip renders exactly what the API says (state/counts come from the backend expiry
// policies) and deep-links every row to its tab via ?tab=…&documentId=/qualificationId=.

const ATTENTION: EmployeeAttention = {
  items: [
    { kind: 'document', id: 'doc-expired', label: 'Identiteitskaart', detail: 'Identiteitskaart voorkant', expiryDate: '2026-09-01', daysLeft: -11, state: 'expired' },
    { kind: 'document', id: 'doc-soon', label: 'Medisch attest', detail: 'Medisch document', expiryDate: '2026-09-24', daysLeft: 12, state: 'expiring' },
    { kind: 'qualification', id: 'qual-soon', label: 'Code 95', detail: 'C95-123', expiryDate: '2026-10-05', daysLeft: 23, state: 'expiring' },
  ],
  documentsExpiring: 1,
  documentsExpired: 1,
  qualificationsExpiring: 1,
  qualificationsExpired: 0,
  hasItems: true,
}

const EMPTY: EmployeeAttention = {
  items: [],
  documentsExpiring: 0,
  documentsExpired: 0,
  qualificationsExpiring: 0,
  qualificationsExpired: 0,
  hasItems: false,
}

function renderAt(initialPath: string, attention: EmployeeAttention | null) {
  const router = createMemoryRouter(
    [{ path: '/employees/:id', element: <EmployeeAttentionStrip attention={attention} /> }],
    { initialEntries: [initialPath] },
  )
  render(<RouterProvider router={router} />)
  return router
}

describe('EmployeeAttentionStrip', () => {
  it('renders nothing without items (null payload or hasItems=false)', () => {
    renderAt('/employees/emp-1', null)
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
    renderAt('/employees/emp-1', EMPTY)
    expect(screen.queryByRole('region')).not.toBeInTheDocument()
  })

  it('renders summary lines and one clickable row per item, expired severity when anything is expired', () => {
    renderAt('/employees/emp-1', ATTENTION)

    const region = screen.getByRole('region', { name: 'Aandachtspunten' })
    expect(region).toHaveClass('is-expired')
    expect(screen.getByText('1 document is verlopen')).toBeInTheDocument()
    expect(screen.getByText('1 document verloopt over 12 dagen')).toBeInTheDocument()
    expect(screen.getByText('1 kwalificatie verloopt over 23 dagen')).toBeInTheDocument()

    // Dates go through the tenant date format (default dd/MM/yyyy).
    expect(screen.getByRole('button', { name: 'Identiteitskaart — verlopen op 01/09/2026' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Medisch attest — vervalt op 24/09/2026' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Code 95 — vervalt op 05/10/2026' })).toBeInTheDocument()
    expect(screen.getByText('C95-123')).toBeInTheDocument()
  })

  it('uses the expiring severity and plural copy when nothing is expired', () => {
    renderAt('/employees/emp-1', {
      ...EMPTY,
      hasItems: true,
      documentsExpiring: 2,
      items: [
        { kind: 'document', id: 'a', label: 'A', detail: null, expiryDate: '2026-09-20', daysLeft: 8, state: 'expiring' },
        { kind: 'document', id: 'b', label: 'B', detail: null, expiryDate: '2026-09-28', daysLeft: 16, state: 'expiring' },
      ],
    })
    expect(screen.getByRole('region', { name: 'Aandachtspunten' })).toHaveClass('is-expiring')
    expect(screen.getByText('2 documenten verlopen binnenkort')).toBeInTheDocument()
  })

  it('clicking a document row sets ?tab=documenten&documentId= and keeps other params', async () => {
    const router = renderAt('/employees/emp-1?section=hr', ATTENTION)

    await userEvent.click(screen.getByRole('button', { name: 'Medisch attest — vervalt op 24/09/2026' }))

    const params = new URLSearchParams(router.state.location.search)
    expect(params.get('tab')).toBe('documenten')
    expect(params.get('documentId')).toBe('doc-soon')
    expect(params.get('section')).toBe('hr')
  })

  it('clicking a qualification row sets ?tab=kwalificaties&qualificationId= and drops a stale documentId', async () => {
    const router = renderAt('/employees/emp-1?tab=documenten&documentId=doc-soon', ATTENTION)

    await userEvent.click(screen.getByRole('button', { name: 'Code 95 — vervalt op 05/10/2026' }))

    const params = new URLSearchParams(router.state.location.search)
    expect(params.get('tab')).toBe('kwalificaties')
    expect(params.get('qualificationId')).toBe('qual-soon')
    expect(params.has('documentId')).toBe(false)
  })
})
