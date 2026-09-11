import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SelfSavingPanel } from '../SelfSavingPanel'
import { Modal } from '../Modal'

describe('SelfSavingPanel', () => {
  it('keeps change and submit events of an embedded self-saving form away from the page form', async () => {
    const user = userEvent.setup()
    const pageChange = vi.fn()
    const pageSubmit = vi.fn((e: React.FormEvent) => e.preventDefault())
    const innerSubmit = vi.fn((e: React.FormEvent) => e.preventDefault())

    render(
      <form onChange={pageChange} onSubmit={pageSubmit}>
        <input aria-label="pagina-veld" />
        <SelfSavingPanel>
          <Modal title="Contact" onClose={() => undefined}>
            <form id="inner" onSubmit={innerSubmit}>
              <input type="checkbox" aria-label="Facturen" />
              <button type="submit">Opslaan</button>
            </form>
          </Modal>
        </SelfSavingPanel>
      </form>,
    )

    await user.click(screen.getByLabelText('Facturen'))
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(innerSubmit).toHaveBeenCalledTimes(1)
    expect(pageChange).not.toHaveBeenCalled()
    expect(pageSubmit).not.toHaveBeenCalled()

    // The page form still sees its own fields.
    await user.type(screen.getByLabelText('pagina-veld'), 'x')
    expect(pageChange).toHaveBeenCalled()
  })

  it('renders the modal outside the host form so no nested <form> exists in the DOM', () => {
    render(
      <form data-testid="page-form">
        <SelfSavingPanel>
          <Modal title="Adres" onClose={() => undefined}>
            <form data-testid="inner-form" />
          </Modal>
        </SelfSavingPanel>
      </form>,
    )
    const inner = screen.getByTestId('inner-form')
    expect(screen.getByTestId('page-form').contains(inner)).toBe(false)
    expect(inner.closest('form')).toBe(inner)
  })
})
