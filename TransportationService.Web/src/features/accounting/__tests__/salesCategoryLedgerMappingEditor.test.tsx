import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SalesCategoryLedgerMappingEditor } from '../components/SalesCategoryLedgerMappingEditor'
import type { LedgerAccount, SalesCategoryLedgerMapping } from '../api/accountingApi'

const api = vi.hoisted(() => ({ getLegalEntityOptions: vi.fn() }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: api.getLegalEntityOptions }))

const accounts: LedgerAccount[] = [
  { id: 'acc-700', accountNumber: '700000', name: 'Omzet transport', externalCode: null, description: null, isActive: true },
  { id: 'acc-701', accountNumber: '701000', name: 'Omzet opslag', externalCode: null, description: null, isActive: true },
]

beforeEach(() => {
  api.getLegalEntityOptions.mockReset().mockResolvedValue([
    { id: 'ent-a', displayName: 'Entiteit A', vatNumber: null, isDefault: true, isActive: true },
    { id: 'ent-b', displayName: 'Entiteit B', vatNumber: null, isDefault: false, isActive: true },
    { id: 'ent-old', displayName: 'Oude entiteit', vatNumber: null, isDefault: false, isActive: false },
  ])
})

function Harness({ initial }: { initial: SalesCategoryLedgerMapping[] }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <SalesCategoryLedgerMappingEditor accounts={accounts} value={value} onChange={setValue} />
      <output data-testid="value">{JSON.stringify(value)}</output>
    </>
  )
}

describe('SalesCategoryLedgerMappingEditor', () => {
  it('shows one row per active entity; picking an account adds a mapping, clearing it removes the mapping', async () => {
    render(<Harness initial={[{ legalEntityId: 'ent-b', ledgerAccountId: 'acc-701', costCentre: 'CC-1' }]} />)

    await screen.findByText('Entiteit A')
    expect(screen.queryByText('Oude entiteit')).not.toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Grootboekrekening Entiteit B' })).toHaveValue('acc-701')
    expect(screen.getByRole('textbox', { name: 'Kostenplaats Entiteit B' })).toHaveValue('CC-1')
    // Without an account the cost centre has nothing to attach to.
    expect(screen.getByRole('textbox', { name: 'Kostenplaats Entiteit A' })).toBeDisabled()

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Grootboekrekening Entiteit A' }), 'acc-700')
    await userEvent.type(screen.getByRole('textbox', { name: 'Kostenplaats Entiteit A' }), 'CC-A')
    expect(JSON.parse(screen.getByTestId('value').textContent!)).toEqual(expect.arrayContaining([
      { legalEntityId: 'ent-b', ledgerAccountId: 'acc-701', costCentre: 'CC-1' },
      { legalEntityId: 'ent-a', ledgerAccountId: 'acc-700', costCentre: 'CC-A' },
    ]))

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Grootboekrekening Entiteit B' }), '')
    expect(JSON.parse(screen.getByTestId('value').textContent!)).toEqual([
      { legalEntityId: 'ent-a', ledgerAccountId: 'acc-700', costCentre: 'CC-A' },
    ])
  })
})
