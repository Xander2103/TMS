import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Modal } from '../../../components/ui/Modal'
import { SectionDrawer } from '../components/SectionDrawer'

vi.mock('../../../i18n/localeContext', () => ({
  useLocale: () => ({ t: (key: string) => key, locale: 'nl' }),
}))
// The guard needs a data router; navigation blocking is not what this file is about.
vi.mock('../../../components/ui/UnsavedChangesGuard', () => ({ UnsavedChangesGuard: () => null }))

/**
 * Sprint 2026-09-21 — the page could not be scrolled to the bottom after closing a dirty drawer.
 * Drawer and dialog each managed `body.style.overflow` on their own; React cleans a parent up
 * before its children, so the dialog restored the 'hidden' it had captured from the drawer.
 */
function Host({ dirty = false }: { dirty?: boolean }) {
  const [drawerOpen, setDrawerOpen] = useState(true)
  const [modalOpen, setModalOpen] = useState(true)
  const [isDirty, setIsDirty] = useState(dirty)
  return (
    <>
      <button type="button" onClick={() => setDrawerOpen(false)}>
        sluit-drawer
      </button>
      <button type="button" onClick={() => setModalOpen(false)}>
        sluit-modal
      </button>
      <button type="button" onClick={() => setIsDirty((d) => !d)}>
        toggle-dirty
      </button>
      {drawerOpen && (
        <SectionDrawer title="Drawer" dirty={isDirty} onClose={() => setDrawerOpen(false)} onSave={() => {}}>
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

describe('SectionDrawer + Modal body scroll lock', () => {
  afterEach(() => {
    document.body.style.overflow = ''
  })

  it.each([
    ['drawer first', ['sluit-drawer', 'sluit-modal']],
    ['modal first', ['sluit-modal', 'sluit-drawer']],
  ])('restores the original overflow when closed %s', async (_label, order) => {
    const user = userEvent.setup()
    document.body.style.overflow = 'auto'
    render(<Host />)
    expect(document.body.style.overflow).toBe('hidden')

    await user.click(screen.getByRole('button', { name: order[0] }))
    expect(document.body.style.overflow).toBe('hidden')
    await user.click(screen.getByRole('button', { name: order[1] }))
    expect(document.body.style.overflow).toBe('auto')
  })

  it('keeps the lock stable while dirty toggles under an open dialog', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'toggle-dirty' }))
    expect(document.body.style.overflow).toBe('hidden')
    await user.click(screen.getByRole('button', { name: 'sluit-modal' }))
    expect(document.body.style.overflow).toBe('hidden')
    await user.click(screen.getByRole('button', { name: 'sluit-drawer' }))
    expect(document.body.style.overflow).toBe('')
  })

  it('unlocks the page when a dirty drawer is left through its own confirm dialog', async () => {
    // Drawer and its ConfirmDialog unmount in ONE commit, parent cleanup first — the exact
    // sequence that used to leave the body on overflow:hidden.
    const user = userEvent.setup()
    render(<Host dirty />)
    await user.click(screen.getByRole('button', { name: 'sluit-modal' }))
    await user.click(screen.getByRole('button', { name: 'ui.actions.close' }))
    await user.click(screen.getByRole('button', { name: 'dossiers.sectionDrawer.confirmLeave' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.body.style.overflow).toBe('')
  })
})
