import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DossierConfirmDialog } from '../components/DossierConfirmDialog'
import type { DossierConfirmationEvaluation } from '../types'
import { dossierDetail } from './fixtures'

const api = vi.hoisted(() => ({ getDossierConfirmation: vi.fn(), confirmDossier: vi.fn() }))
vi.mock('../api/dossiersApi', async (orig) => ({
  ...(await orig<typeof import('../api/dossiersApi')>()),
  getDossierConfirmation: api.getDossierConfirmation,
  confirmDossier: api.confirmDossier,
}))

function evaluation(overrides: Partial<DossierConfirmationEvaluation> = {}): DossierConfirmationEvaluation {
  return {
    dossierId: 'd-1', status: 'Open', canConfirmManually: true, canAutoConfirm: true,
    blockers: [], warnings: [], autoBlockers: [], relevantOrderIds: ['o-1'], relevantActivityIds: ['a-1'],
    summary: {
      ordersTotal: 1, ordersCompleted: 1, ordersCancelled: 0, ordersOpen: 0, deliveriesFailed: 0, podMissing: 0,
      activitiesExecutable: 0, activitiesExecuted: 0, billableUnits: 1, pricedUnits: 1, documents: 2, hasCmr: true, openIncidents: 0,
    },
    ...overrides,
  }
}

/**
 * Confirmation sprint 2026-09-23 — the dialog shows the readiness summary, distinguishes blockers
 * (refuse) from warnings (acknowledge deliberately), and confirms through the ONE lifecycle
 * endpoint. Used from the detail header and from the list row action alike.
 */
describe('DossierConfirmDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  function renderDialog(onConfirmed = vi.fn(), onClose = vi.fn()) {
    render(
      <MemoryRouter>
        <DossierConfirmDialog dossierId="d-1" dossierNumber="DOS-0001" version="v1" onConfirmed={onConfirmed} onClose={onClose} />
      </MemoryRouter>,
    )
    return { onConfirmed, onClose }
  }

  it('confirms a ready dossier with the summary and an optional note', async () => {
    api.getDossierConfirmation.mockResolvedValue(evaluation())
    api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
    const user = userEvent.setup()
    const { onConfirmed } = renderDialog()

    expect(await screen.findByText('1 van 1 voltooid')).toBeInTheDocument()
    expect(screen.getByText('Alle operationele onderdelen zijn afgerond.')).toBeInTheDocument()
    expect(screen.queryByLabelText(/sluit bewust af/i)).toBeNull()

    await user.type(screen.getByLabelText(/Opmerking/), 'Alles afgehandeld')
    await user.click(screen.getByRole('button', { name: 'Dossier afsluiten' }))

    await waitFor(() => expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: 'Alles afgehandeld', acknowledgeWarnings: false, version: 'v1' }))
    expect(onConfirmed).toHaveBeenCalled()
  })

  it('lists warnings and only confirms after a deliberate acknowledgement', async () => {
    api.getDossierConfirmation.mockResolvedValue(evaluation({
      canAutoConfirm: false,
      warnings: [{ code: 'order.not_completed', severity: 'Warning', message: 'Opdracht ORD-1 is nog niet uitgevoerd (status Draft).', transportOrderId: 'o-1', activityId: null }],
      summary: { ...evaluation().summary, ordersCompleted: 0, ordersOpen: 1 },
    }))
    api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
    const user = userEvent.setup()
    renderDialog()

    expect(await screen.findByText('Aandachtspunten')).toBeInTheDocument()
    expect(screen.getByText(/ORD-1 is nog niet uitgevoerd/)).toBeInTheDocument()
    const confirm = screen.getByRole('button', { name: 'Dossier afsluiten' })
    expect(confirm).toBeDisabled()

    await user.click(screen.getByLabelText(/sluit bewust af/i))
    expect(confirm).toBeEnabled()
    await user.click(confirm)

    await waitFor(() => expect(api.confirmDossier).toHaveBeenCalledWith('d-1', { reason: null, acknowledgeWarnings: true, version: 'v1' }))
  })

  it('refuses when a blocker exists — no acknowledgement can bypass it', async () => {
    api.getDossierConfirmation.mockResolvedValue(evaluation({
      canConfirmManually: false, canAutoConfirm: false,
      blockers: [{ code: 'incident.open', severity: 'Blocking', message: 'Dit dossier heeft nog 1 open incident(en). Handel die eerst af.', transportOrderId: null, activityId: null }],
    }))
    renderDialog()

    expect(await screen.findByText('Afsluiten is niet mogelijk')).toBeInTheDocument()
    expect(screen.getByText(/1 open incident/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Dossier afsluiten' })).toBeDisabled()
    expect(screen.queryByLabelText(/sluit bewust af/i)).toBeNull()
    expect(api.confirmDossier).not.toHaveBeenCalled()
  })

  it('reports a failed confirmation and stays open', async () => {
    api.getDossierConfirmation.mockResolvedValue(evaluation())
    api.confirmDossier.mockRejectedValue(new Error('Request to /api/dossiers/d1/confirm failed with status 500'))
    const user = userEvent.setup()
    const { onConfirmed, onClose } = renderDialog()

    await screen.findByText('1 van 1 voltooid')
    await user.click(screen.getByRole('button', { name: 'Dossier afsluiten' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Het dossier kon niet worden afgesloten.')
    expect(onConfirmed).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('EX-05: a blocking afwijking is listed as Blokkerend and needs an acknowledgement AND a reason', async () => {
    api.getDossierConfirmation.mockResolvedValue(evaluation({
      canAutoConfirm: false,
      warnings: [
        { code: 'exception.open.hold', severity: 'Hold', message: 'Open afwijking (beschadigd collo, ernst hoog) op rit RIT-1: houdt automatische bevestiging tegen.', transportOrderId: 'o-1', activityId: null, problemId: 'x-1' },
        { code: 'exception.open.info', severity: 'Info', message: 'Open afwijking (vertraging, ernst laag): ter info.', transportOrderId: 'o-1', activityId: null, problemId: 'x-2' },
      ],
      summary: { ...evaluation().summary, openExceptions: 2, openExceptionsHolding: 1 },
    }))
    api.confirmDossier.mockResolvedValue(dossierDetail({ status: 'Closed' }))
    const user = userEvent.setup()
    renderDialog()

    expect(await screen.findByText('Blokkerend')).toBeInTheDocument()
    expect(screen.getByText('Ter info')).toBeInTheDocument()
    expect(screen.getByText('2 · 1 blokkerend')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Afwijking openen' })[0]).toHaveAttribute('href', '/exceptions/x-1')
    const confirm = screen.getByRole('button', { name: 'Dossier afsluiten' })
    await user.click(screen.getByLabelText(/sluit bewust af/i))
    expect(confirm).toBeDisabled() // the reason is still missing
    await user.type(screen.getByLabelText(/Reden/), 'Schade apart opgevolgd met de klant')
    expect(confirm).toBeEnabled()
    await user.click(confirm)
    await waitFor(() => expect(api.confirmDossier).toHaveBeenCalledWith('d-1', {
      reason: 'Schade apart opgevolgd met de klant', acknowledgeWarnings: true, version: 'v1',
    }))
  })
})
