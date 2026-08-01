import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { NegativeStockConfirmModal } from '../components/NegativeStockConfirmModal'
import type { NegativeStockPayload } from '../inventoryApi'

function makePayload(overrides?: Partial<NegativeStockPayload>): NegativeStockPayload {
  return {
    code: 'negative_stock_confirmation_required',
    templateId: 't-1',
    variantId: 'v-1',
    itemName: 'Veiligheidsschoenen',
    variantLabel: 'maat 43',
    currentStock: 2,
    requestedDelta: -5,
    projectedStock: -3,
    version: 'ver-1',
    requiresReason: true,
    versionMismatch: false,
    ...overrides,
  }
}

describe('NegativeStockConfirmModal', () => {
  afterEach(cleanup)

  it('shows the server figures, item, employee and location', () => {
    render(
      <NegativeStockConfirmModal
        payload={makePayload()}
        kind="issue"
        employeeName="Jan Peeters"
        storageLocation="Rek B4"
        canConfirm
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(screen.getByText('Deze uitgifte brengt de voorraad onder nul.')).toBeInTheDocument()
    expect(screen.getByText('Veiligheidsschoenen — maat 43')).toBeInTheDocument()
    expect(screen.getByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByText('Rek B4')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    // Requested quantity is shown as an absolute number.
    expect(screen.getByText('5')).toBeInTheDocument()
    // Projected stock carries an explicit text label, not just a colour.
    expect(screen.getByText(/-3 — negatieve voorraad/)).toBeInTheDocument()
  })

  it('keeps the confirm button disabled until a reason is entered and passes it to onConfirm', async () => {
    const onConfirm = vi.fn()
    render(
      <NegativeStockConfirmModal
        payload={makePayload({ requiresReason: true })}
        kind="issue"
        canConfirm
        onConfirm={onConfirm}
        onCancel={vi.fn()}
      />,
    )

    const confirmButton = screen.getByRole('button', { name: 'Bevestig negatieve voorraad' })
    expect(confirmButton).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Reden/), '  Spoedbestelling  ')
    expect(confirmButton).toBeEnabled()
    await userEvent.click(confirmButton)
    expect(onConfirm).toHaveBeenCalledWith('Spoedbestelling')
  })

  it('allows confirming without a reason when requiresReason is false', async () => {
    const onConfirm = vi.fn()
    render(
      <NegativeStockConfirmModal
        payload={makePayload({ requiresReason: false })}
        kind="issue"
        canConfirm
        onConfirm={onConfirm}
        onCancel={vi.fn()}
      />,
    )

    const confirmButton = screen.getByRole('button', { name: 'Bevestig negatieve voorraad' })
    expect(confirmButton).toBeEnabled()
    await userEvent.click(confirmButton)
    expect(onConfirm).toHaveBeenCalledWith('')
  })

  it('shows the version-mismatch warning when stock changed in the meantime', () => {
    render(
      <NegativeStockConfirmModal
        payload={makePayload({ versionMismatch: true })}
        kind="correction"
        canConfirm
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(screen.getByText('Deze correctie brengt de voorraad onder nul.')).toBeInTheDocument()
    expect(screen.getByText('De voorraad is intussen gewijzigd; controleer de nieuwe stand.')).toBeInTheDocument()
    // A correction already carries its reason, so no reason field is shown.
    expect(screen.queryByLabelText(/Reden/)).not.toBeInTheDocument()
  })

  it('hides the confirm button and explains the required permission when canConfirm is false', () => {
    render(
      <NegativeStockConfirmModal
        payload={makePayload()}
        kind="issue"
        canConfirm={false}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(screen.queryByRole('button', { name: 'Bevestig negatieve voorraad' })).not.toBeInTheDocument()
    expect(screen.getByText(/Vraag een beheerder/)).toBeInTheDocument()
  })
})
