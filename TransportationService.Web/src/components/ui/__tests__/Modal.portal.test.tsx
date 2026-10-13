import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Modal } from '../Modal'

vi.mock('../../../i18n/localeContext', () => ({
  useLocale: () => ({ t: (key: string) => key, locale: 'nl' }),
}))

/**
 * Hardening 2026-09-10 — blast-radius regression for the portaled Modal (111 call sites):
 * the dialog renders on document.body, a footer submit button still submits the form inside
 * the dialog via `form=`, Escape closes (not while busy), backdrop click closes, focus moves
 * into the dialog on open and returns to the opener on close, and a dialog opened from inside
 * a page-level <form> never becomes a nested form in the DOM.
 */
function Host({ busy = false }: { busy?: boolean }) {
  const [open, setOpen] = useState(false)
  const [submitted, setSubmitted] = useState(0)
  const [pageSubmits, setPageSubmits] = useState(0)
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        setPageSubmits((n) => n + 1)
      }}
    >
      <input aria-label="pagina-veld" />
      <button type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      <span data-testid="page-submits">{pageSubmits}</span>
      <span data-testid="dialog-submits">{submitted}</span>
      {open && (
        <Modal
          title="Dialoog"
          onClose={() => setOpen(false)}
          busy={busy}
          footer={
            <button type="submit" form="inner-form">
              Bevestig
            </button>
          }
        >
          <form
            id="inner-form"
            onSubmit={(event) => {
              event.preventDefault()
              event.stopPropagation()
              setSubmitted((n) => n + 1)
            }}
          >
            <input aria-label="dialoog-veld" />
          </form>
        </Modal>
      )}
    </form>
  )
}

describe('Modal (portal)', () => {
  it('renders on document.body, never nested in the host form, and submits its own form from the footer', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Open' }))

    const dialog = screen.getByRole('dialog', { name: 'Dialoog' })
    expect(dialog.parentElement?.parentElement).toBe(document.body)
    expect(dialog.closest('form')).toBeNull()
    expect(document.querySelectorAll('form form')).toHaveLength(0)

    await user.click(within(dialog).getByRole('button', { name: 'Bevestig' }))
    expect(screen.getByTestId('dialog-submits')).toHaveTextContent('1')
    expect(screen.getByTestId('page-submits')).toHaveTextContent('0')
  })

  it('moves focus into the dialog on open and returns it to the opener on close', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const opener = screen.getByRole('button', { name: 'Open' })
    await user.click(opener)
    // First focusable in DOM order is the header close button; content may override with autoFocus.
    expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true)

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.activeElement).toBe(opener)
  })

  it('returns focus to the opener even when the content autofocuses its first field (browser-smoke regression)', async () => {
    const user = userEvent.setup()
    function AutoFocusHost() {
      const [open, setOpen] = useState(false)
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Open
          </button>
          {open && (
            <Modal title="Contact" onClose={() => setOpen(false)}>
              <input aria-label="voornaam" autoFocus />
            </Modal>
          )}
        </>
      )
    }
    render(<AutoFocusHost />)
    const opener = screen.getByRole('button', { name: 'Open' })
    await user.click(opener)
    // autoFocus already owns focus before the Modal's effect runs — it must not be mistaken for the opener.
    expect(document.activeElement).toBe(screen.getByLabelText('voornaam'))
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.activeElement).toBe(opener)
  })

  it('closes on backdrop click, but ignores Escape and backdrop while busy', async () => {
    const user = userEvent.setup()
    const { unmount } = render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await user.click(document.querySelector('.ui-modal-backdrop')!)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    unmount()

    render(<Host busy />)
    await user.click(screen.getByRole('button', { name: 'Open' }))
    await user.keyboard('{Escape}')
    await user.click(document.querySelector('.ui-modal-backdrop')!)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('restores the previous body overflow instead of clearing it', async () => {
    const user = userEvent.setup()
    document.body.style.overflow = 'auto'
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Open' }))
    expect(document.body.style.overflow).toBe('hidden')
    await user.keyboard('{Escape}')
    expect(document.body.style.overflow).toBe('auto')
    document.body.style.overflow = ''
  })
})
