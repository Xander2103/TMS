import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { GoodsSection } from '../sections/GoodsSection'
import {
  applyUnitToCargoRow,
  computeCargoSummary,
  duplicateCargoRowInList,
  emptyCargoRow,
  type CargoFormRow,
} from '../sections/orderFormState'

/**
 * Master sprint 2026-09-21, D4 — the goods line in the form: five main fields, automatic vs manual
 * total, "Herbereken", the honest total-only hint and the duplicate / add actions.
 */

function Harness({ initial, withDuplicate = true }: { initial: CargoFormRow[]; withDuplicate?: boolean }) {
  const [cargoItems, setCargoItems] = useState(initial)
  const [text, setText] = useState('')
  const noop = () => {}
  return (
    <GoodsSection
      goodsDescription={text}
      setGoodsDescription={setText}
      quantity=""
      setQuantity={noop}
      quantityUnit=""
      quantityUnitCode={null}
      setQuantityUnitCode={noop}
      weightKg=""
      setWeightKg={noop}
      volumeM3=""
      setVolumeM3={noop}
      palletCount=""
      setPalletCount={noop}
      distanceKm=""
      setDistanceKm={noop}
      loadingMeters=""
      setLoadingMeters={noop}
      adrRequired={false}
      setAdrRequired={noop}
      craneRequired={false}
      setCraneRequired={noop}
      plateauRequired={false}
      setPlateauRequired={noop}
      moffettRequired={false}
      setMoffettRequired={noop}
      isReturnMovement={false}
      setIsReturnMovement={noop}
      derivedFromCargo={cargoItems.length > 0}
      cargoSummary={computeCargoSummary(cargoItems, [])}
      cargoItems={cargoItems}
      stops={[]}
      unitOptions={[]}
      preferredUnits={[]}
      setCargo={(key, patch) => setCargoItems((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)))}
      onAddCargoRow={() => setCargoItems((rows) => [...rows, emptyCargoRow()])}
      onAddCargoRowFromHeader={noop}
      onRemoveCargoRow={(key) => setCargoItems((rows) => rows.filter((r) => r.key !== key))}
      onDuplicateCargoRow={withDuplicate ? (key) => setCargoItems((rows) => duplicateCargoRowInList(rows, key)) : undefined}
      applyCargoUnit={(key, code) => setCargoItems((rows) => rows.map((r) => (r.key === key ? applyUnitToCargoRow(r, code, null) : r)))}
      cargoDimensionsFixed={() => false}
      saving={false}
      errors={{}}
    />
  )
}

const totalOf = (line: HTMLElement) => within(line).getByLabelText('Totaal gewicht (kg)') as HTMLInputElement
const line = (number: number) => screen.getByRole('group', { name: `Lijn ${number}` })

describe('GoodsSection goods line (D4)', () => {
  it('renders the five main fields outside "Meer details"', () => {
    render(<Harness initial={[emptyCargoRow()]} />)
    const first = line(1)
    const details = first.querySelector('details.tof-stop-details') as HTMLElement
    expect(details.hasAttribute('open')).toBe(false)
    for (const label of ['Omschrijving', 'Verwacht aantal *', 'Eenheid', 'Gewicht per eenheid (kg)', 'Totaal gewicht (kg)']) {
      const control = within(first).getByLabelText(label)
      expect(control).toBeVisible()
      expect(details.contains(control)).toBe(false)
    }
    expect(
      screen.getByText(/Verschillende gewichten per eenheid\? Voer ze in als aparte goederenlijnen/),
    ).toBeInTheDocument()
  })

  it('computes the total, recomputes on a quantity change, keeps a typed total and recalculates on demand', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[{ ...emptyCargoRow(), expectedQuantity: '3' }]} />)
    const first = line(1)

    await user.type(within(first).getByLabelText('Gewicht per eenheid (kg)'), '2000')
    expect(totalOf(first).value).toBe('6000')
    expect(within(first).getByText('Automatisch: aantal × gewicht per eenheid.')).toBeInTheDocument()
    expect(within(first).queryByRole('button', { name: 'Herbereken' })).not.toBeInTheDocument()

    const quantity = within(first).getByLabelText('Verwacht aantal *')
    await user.clear(quantity)
    await user.type(quantity, '4')
    expect(totalOf(first).value).toBe('8000')

    // Typed by hand → manual: later input changes no longer overwrite it.
    await user.clear(totalOf(first))
    await user.type(totalOf(first), '8150')
    expect(within(first).getByText('Handmatig ingevuld.')).toBeInTheDocument()
    await user.clear(quantity)
    await user.type(quantity, '5')
    expect(totalOf(first).value).toBe('8150')

    await user.click(within(first).getByRole('button', { name: 'Herbereken' }))
    expect(totalOf(first).value).toBe('10000')
    expect(within(first).getByText('Automatisch: aantal × gewicht per eenheid.')).toBeInTheDocument()
  })

  it('never shows a weight per unit for a total-only line — a neutral hint instead', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[{ ...emptyCargoRow(), expectedQuantity: '3' }]} />)
    const first = line(1)

    await user.type(totalOf(first), '6000')
    expect((within(first).getByLabelText('Gewicht per eenheid (kg)') as HTMLInputElement).value).toBe('')
    expect(
      within(first).getByText('Gewicht per eenheid onbekend — capaciteit per eenheid nog te controleren'),
    ).toBeInTheDocument()
    expect(within(first).queryByRole('button', { name: 'Herbereken' })).not.toBeInTheDocument()
    expect(screen.queryByText(/2[.,]?000/)).not.toBeInTheDocument()
  })

  it('offers "Regel dupliceren" and "+ Regel toevoegen" next to the lines', async () => {
    const user = userEvent.setup()
    render(<Harness initial={[{ ...emptyCargoRow(), description: 'Staal', expectedQuantity: '3', weightPerUnitKg: '2000', totalWeightKg: '6000' }]} />)

    await user.click(within(line(1)).getByRole('button', { name: 'Regel dupliceren' }))
    expect((within(line(2)).getByLabelText('Omschrijving') as HTMLInputElement).value).toBe('Staal')
    expect(totalOf(line(2)).value).toBe('6000')

    await user.click(screen.getByRole('button', { name: '+ Regel toevoegen' }))
    expect(screen.getAllByRole('group', { name: /^Lijn \d+$/ })).toHaveLength(3)
  })

  it('hides the duplicate action when the host does not support it', () => {
    render(<Harness initial={[emptyCargoRow()]} withDuplicate={false} />)
    expect(screen.queryByRole('button', { name: 'Regel dupliceren' })).not.toBeInTheDocument()
  })

  it('shows the capacity block under the lines with the unknown state', () => {
    render(<Harness initial={[{ ...emptyCargoRow(), expectedQuantity: '3', weightPerUnitKg: '2000', totalWeightKg: '6000' }]} />)
    const block = screen.getByRole('complementary', { name: 'Operationele capaciteit' })
    expect(within(block).getByText('6.000 kg')).toBeInTheDocument()
    expect(within(block).getByText('2.000 kg')).toBeInTheDocument()
    expect(within(block).getByText('Capaciteit nog te controleren')).toBeInTheDocument()
  })
})
