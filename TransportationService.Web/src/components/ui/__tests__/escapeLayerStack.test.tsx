import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { ConfirmDialog } from '../ConfirmDialog'
import { Modal } from '../Modal'
import { SectionDrawer } from '../../../features/dossiers/components/SectionDrawer'

vi.mock('../../../i18n/localeContext', () => ({
  useLocale: () => ({ t: (key: string) => key, locale: 'nl' }),
}))
// The guard needs a data router; navigation blocking is not what this file is about.
vi.mock('../UnsavedChangesGuard', () => ({ UnsavedChangesGuard: () => null }))

/**
 * 2026-09-23 — Escape inside a confirmation dialog also closed the drawer hosting it. Drawer
 * and dialog each listened for Escape on `document`, so one key press reached both. The shared
 * escape layer stack hands Escape to the topmost open layer only; the next press reaches the
 * layer below, exactly as if the user had closed the top one with its own button.
 */
function Host({ dirty = false, confirmBusy = false }: { dirty?: boolean; confirmBusy?: boolean }) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [drawerCloses, setDrawerCloses] = useState(0)
  return (
    <>
      <button type="button" onClick={() => setDrawerOpen(true)}>
        open-drawer
      </button>
      <span data-testid="drawer-closes">{drawerCloses}</span>
      {drawerOpen && (
        <SectionDrawer
          title="Drawer"
          dirty={dirty}
          onClose={() => {
            setDrawerCloses((n) => n + 1)
            setDrawerOpen(false)
          }}
          onSave={() => {}}
        >
          <input aria-label="drawer-veld" />
          <button type="button" onClick={() => setConfirmOpen(true)}>
            open-confirm
          </button>
        </SectionDrawer>
      )}
      {confirmOpen && (
        <ConfirmDialog
          title="Bevestig"
          message="Zeker?"
          busy={confirmBusy}
          onConfirm={() => setConfirmOpen(false)}
          onCancel={() => setConfirmOpen(false)}
        />
      )}
    </>
  )
}

describe('escape layer stack (SectionDrawer + Modal)', () => {
  it('closes only the confirm dialog on the first Escape and the drawer on the second', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'open-drawer' }))
    await user.click(screen.getByRole('button', { name: 'open-confirm' }))
    expect(screen.getByRole('dialog', { name: 'Bevestig' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'Bevestig' })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Drawer' })).toBeInTheDocument()
    expect(screen.getByTestId('drawer-closes')).toHaveTextContent('0')

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByTestId('drawer-closes')).toHaveTextContent('1')
  })

  it('lets Escape cancel only the unsaved-changes confirm of a dirty drawer, which then re-asks', async () => {
    const user = userEvent.setup()
    render(<Host dirty />)
    await user.click(screen.getByRole('button', { name: 'open-drawer' }))

    await user.keyboard('{Escape}')
    expect(screen.getByRole('dialog', { name: 'dossiers.sectionDrawer.confirmTitle' })).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Drawer' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'dossiers.sectionDrawer.confirmTitle' })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Drawer' })).toBeInTheDocument()
    expect(screen.getByTestId('drawer-closes')).toHaveTextContent('0')

    await user.keyboard('{Escape}')
    expect(screen.getByRole('dialog', { name: 'dossiers.sectionDrawer.confirmTitle' })).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Drawer' })).toBeInTheDocument()
  })

  it('returns focus to the opener button after the drawer closes via Escape', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const opener = screen.getByRole('button', { name: 'open-drawer' })
    await user.click(opener)
    // Focus lives inside the drawer by the time it closes — the realistic case.
    await user.click(screen.getByLabelText('drawer-veld'))
    expect(document.activeElement).toBe(screen.getByLabelText('drawer-veld'))

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.activeElement).toBe(opener)
  })

  it('ignores Escape while the top dialog is busy, without letting it fall through to the drawer', async () => {
    const user = userEvent.setup()
    render(<Host confirmBusy />)
    await user.click(screen.getByRole('button', { name: 'open-drawer' }))
    await user.click(screen.getByRole('button', { name: 'open-confirm' }))

    await user.keyboard('{Escape}')
    expect(screen.getByRole('dialog', { name: 'Bevestig' })).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Drawer' })).toBeInTheDocument()
    expect(screen.getByTestId('drawer-closes')).toHaveTextContent('0')
  })

  it('hands Escape to whichever layer is on top after a lower layer closed out of order', async () => {
    // Layers are removed by identity, not popped: closing the drawer under an open dialog must
    // leave the dialog as the top layer, and Escape must still reach it.
    const user = userEvent.setup()
    function OutOfOrderHost() {
      const [drawerOpen, setDrawerOpen] = useState(true)
      const [modalOpen, setModalOpen] = useState(true)
      return (
        <>
          <button type="button" onClick={() => setDrawerOpen(false)}>
            sluit-drawer
          </button>
          {drawerOpen && (
            <SectionDrawer title="Drawer" dirty={false} onClose={() => setDrawerOpen(false)}>
              <p>inhoud</p>
            </SectionDrawer>
          )}
          {modalOpen && (
            <Modal title="Dialoog" onClose={() => setModalOpen(false)}>
              <p>dialoog</p>
            </Modal>
          )}
        </>
      )
    }
    render(<OutOfOrderHost />)
    await user.click(screen.getByRole('button', { name: 'sluit-drawer' }))
    expect(screen.queryByRole('dialog', { name: 'Drawer' })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Dialoog' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
