import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AgreementAdjustmentsPanel } from '../AgreementAdjustmentsPanel'
import type { PriceAdjustmentRulePreview, ScheduledPriceAdjustment, UnitTypeSettings } from '../../api/pricingApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showSuccess: vi.fn(), showError: vi.fn() }),
}))

const state = vi.hoisted(() => ({
  adjustments: [] as ScheduledPriceAdjustment[],
  units: [] as UnitTypeSettings[],
  preview: vi.fn(),
  create: vi.fn(),
  cancel: vi.fn(),
}))

vi.mock('../../api/pricingApi', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../api/pricingApi')>()
  return {
    ...original,
    listAgreementAdjustments: () => Promise.resolve(state.adjustments),
    listUnitTypeSettings: () => Promise.resolve(state.units),
    previewAgreementAdjustment: state.preview,
    createAgreementAdjustment: state.create,
    cancelAgreementAdjustment: state.cancel,
  }
})

function makePreview(): PriceAdjustmentRulePreview[] {
  return [
    {
      priceRuleId: 'rule-1',
      ruleName: 'Europallet Brussel',
      effectiveFrom: '2026-01-01',
      effectiveUntil: null,
      changes: [
        { field: 'Staffel 1-1', oldValue: 45, newValue: 46.8 },
        { field: 'Staffel 2+', oldValue: 70, newValue: 72.8 },
      ],
    },
  ]
}

beforeEach(() => {
  vi.clearAllMocks()
  state.adjustments = []
  state.units = []
  state.preview.mockResolvedValue(makePreview())
  state.create.mockResolvedValue({ id: 'adj-1' })
  state.cancel.mockResolvedValue({ id: 'adj-1', status: 'Geannuleerd', statusCode: 'Cancelled' })
})

describe('AgreementAdjustmentsPanel', () => {
  it('previews old/new values before confirming, then posts the create request', async () => {
    const user = userEvent.setup()
    render(<AgreementAdjustmentsPanel agreementId="agr-1" canManage />)

    await user.click(await screen.findByRole('button', { name: '+ Nieuwe prijsaanpassing' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText(/Ingangsdatum/), '2099-10-01')
    await user.type(within(dialog).getByLabelText(/Waarde/), '4')
    await user.click(within(dialog).getByRole('button', { name: 'Voorbeeld' }))

    expect(await within(dialog).findByText('€ 45,00')).toBeInTheDocument()
    expect(within(dialog).getByText('€ 46,80')).toBeInTheDocument()
    expect(state.create).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Bevestigen' }))
    await waitFor(() =>
      expect(state.create).toHaveBeenCalledWith(
        'agr-1',
        expect.objectContaining({ effectiveDate: '2099-10-01', percent: 4, amountDelta: null, ruleIds: null }),
      ),
    )
  })

  it('sends a basis filter when bases are checked and switches to a fixed amount', async () => {
    const user = userEvent.setup()
    render(<AgreementAdjustmentsPanel agreementId="agr-1" canManage />)

    await user.click(await screen.findByRole('button', { name: '+ Nieuwe prijsaanpassing' }))
    const dialog = await screen.findByRole('dialog')

    await user.selectOptions(within(dialog).getByLabelText('Type'), 'amount')
    await user.type(within(dialog).getByLabelText(/Ingangsdatum/), '2099-10-01')
    await user.type(within(dialog).getByLabelText(/Waarde/), '5')
    await user.click(within(dialog).getByLabelText('Per uur'))
    await user.click(within(dialog).getByRole('button', { name: 'Voorbeeld' }))

    await waitFor(() =>
      expect(state.preview).toHaveBeenCalledWith(
        'agr-1',
        expect.objectContaining({ percent: null, amountDelta: 5, basisFilter: 'Hourly' }),
      ),
    )
  })

  it('lists scheduled adjustments with a cancel action', async () => {
    const user = userEvent.setup()
    state.adjustments = [
      {
        id: 'adj-1',
        customerId: null,
        agreementId: 'agr-1',
        effectiveDate: '2099-10-01',
        percent: 4,
        amountDelta: null,
        roundingStep: null,
        basisFilter: null,
        unitTypeIdFilter: null,
        status: 'Gepland',
        statusCode: 'Planned',
        reason: 'Indexatie',
        ruleCount: 2,
        createdAt: '2026-08-15T10:00:00Z',
      },
    ]

    render(<AgreementAdjustmentsPanel agreementId="agr-1" canManage />)

    expect(await screen.findByText('2099-10-01')).toBeInTheDocument()
    expect(screen.getByText('+4%')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Annuleren' }))
    await user.click(await screen.findByRole('button', { name: 'Annuleren bevestigen' }))
    await waitFor(() => expect(state.cancel).toHaveBeenCalledWith('agr-1', 'adj-1'))
  })
})
