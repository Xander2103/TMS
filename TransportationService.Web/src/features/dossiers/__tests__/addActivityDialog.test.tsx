import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AddActivityDialog } from '../components/AddActivityDialog'
import { dossierDetail } from './fixtures'

vi.mock('../api/activityTypesApi', () => ({
  listActivityTypes: () =>
    Promise.resolve([
      {
        id: 'at-1', code: 'DIRECT_TRANSPORT', name: 'Direct transport', isActive: true, sortOrder: 1,
        icon: 'truck', kpiCategory: null, hasStops: true, supportsGoods: true, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: false, isQuickStart: true, quickStartOrder: 1,
        isSystemDefaultTransport: true,
      },
      {
        id: 'at-2', code: 'KRAANWERK', name: 'Kraanwerk ter plaatse', isActive: true, sortOrder: 2,
        icon: 'crane', kpiCategory: null, hasStops: false, supportsGoods: false, planningRelevant: true,
        warehouseRelevant: false, allowsDuration: true, isQuickStart: false, quickStartOrder: 0,
        isSystemDefaultTransport: false,
      },
    ]),
}))

const addActivity = vi.hoisted(() => vi.fn())
vi.mock('../api/dossiersApi', () => ({
  addDossierActivity: addActivity,
}))

describe('AddActivityDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    addActivity.mockResolvedValue(dossierDetail())
  })

  it('adds a transport activity with createLinkedOrder default ON', async () => {
    const user = userEvent.setup()
    const onAdded = vi.fn()
    render(
      <AddActivityDialog
        dossier={dossierDetail()}
        onClose={vi.fn()}
        onAdded={onAdded}
        onConflict={() => false}
      />,
    )

    await user.click(await screen.findByRole('radio', { name: /Direct transport/ }))
    // hasStops → the checkbox appears, default checked.
    expect(screen.getByLabelText('Meteen transportopdracht aanmaken')).toBeChecked()
    await user.type(screen.getByLabelText('Label'), 'Antwerpen–Luik')
    await user.click(screen.getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() =>
      expect(addActivity).toHaveBeenCalledWith(
        'd-1',
        expect.objectContaining({
          activityTypeId: 'at-1',
          label: 'Antwerpen–Luik',
          createLinkedOrder: true,
          version: 'v-1',
        }),
      ),
    )
    expect(onAdded).toHaveBeenCalled()
  })

  it('sends createLinkedOrder false for standalone types (checkbox hidden)', async () => {
    const user = userEvent.setup()
    render(
      <AddActivityDialog dossier={dossierDetail()} onClose={vi.fn()} onAdded={vi.fn()} onConflict={() => false} />,
    )

    await user.click(await screen.findByRole('radio', { name: /Kraanwerk ter plaatse/ }))
    expect(screen.queryByLabelText('Meteen transportopdracht aanmaken')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Toevoegen' }))

    await waitFor(() =>
      expect(addActivity).toHaveBeenCalledWith(
        'd-1',
        expect.objectContaining({ activityTypeId: 'at-2', createLinkedOrder: false }),
      ),
    )
  })
})
