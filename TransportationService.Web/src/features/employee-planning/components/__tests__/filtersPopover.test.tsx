import { describe, expect, it } from 'vitest'
import { useState } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FiltersPopover } from '../FiltersPopover'
import { EMPTY_GROUP_FILTERS, type PlanningGroupFilters } from '../../planningFilters'
import type { PlanningFilterOptions } from '../../types'

/**
 * Regression: opening "Filters" used to expand the Afdelingen combobox at once. The popover
 * moved focus to its first enabled control and the shared MultiSelect opens its list on focus,
 * so with no quick selections and no active filters the departments field popped open by
 * itself. The panel now takes the initial focus; the combobox opens only on an explicit click.
 */
const OPTIONS: PlanningFilterOptions = {
  departments: [
    { id: 'd-1', name: 'Transport' },
    { id: 'd-2', name: 'Dispatch' },
  ],
  jobFunctions: [{ id: 'f-1', name: 'Chauffeur' }],
  contractTypes: [],
  // No quick selections: Afdelingen is the first control of the panel — the layout in which
  // the dropdown used to open by itself.
  myDepartmentId: null,
  hasDrivers: false,
}

function Host({ initial = EMPTY_GROUP_FILTERS }: { initial?: PlanningGroupFilters }) {
  const [filters, setFilters] = useState<PlanningGroupFilters>(initial)
  return <FiltersPopover filters={filters} options={OPTIONS} optionsError={false} onChange={(patch) => setFilters((current) => ({ ...current, ...patch }))} />
}

async function openFilters(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /^Filters/ }))
  return screen.getByRole('dialog', { name: 'Filters' })
}

function departmentsField(dialog: HTMLElement) {
  return within(dialog).getByRole('combobox', { name: 'Afdelingen' })
}

describe('FiltersPopover — Afdelingen stays closed until the user opens it', () => {
  it('opening Filters shows the panel with the Afdelingen dropdown collapsed', async () => {
    const user = userEvent.setup()
    render(<Host />)

    const dialog = await openFilters(user)

    expect(departmentsField(dialog)).toHaveAttribute('aria-expanded', 'false')
    expect(within(dialog).queryByRole('option', { name: 'Transport' })).not.toBeInTheDocument()
    // Focus is inside the dialog (Escape and Tab keep working) but on no control.
    expect(dialog).toHaveFocus()
  })

  it('clicking the Afdelingen field opens it', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const dialog = await openFilters(user)

    await user.click(departmentsField(dialog))

    expect(departmentsField(dialog)).toHaveAttribute('aria-expanded', 'true')
    expect(within(dialog).getByRole('option', { name: 'Transport' })).toBeInTheDocument()
    expect(within(dialog).getByRole('option', { name: 'Dispatch' })).toBeInTheDocument()
  })

  it('selected departments survive closing and reopening Filters, while the dropdown stays closed', async () => {
    const user = userEvent.setup()
    render(<Host />)
    let dialog = await openFilters(user)

    await user.click(departmentsField(dialog))
    await user.click(within(dialog).getByRole('option', { name: 'Transport' }))
    expect(within(dialog).getByRole('button', { name: 'Transport verwijderen' })).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Sluiten' }))
    expect(screen.queryByRole('dialog', { name: 'Filters' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Filters (1)' })).toBeInTheDocument()

    dialog = await openFilters(user)
    expect(within(dialog).getByRole('button', { name: 'Transport verwijderen' })).toBeInTheDocument()
    expect(departmentsField(dialog)).toHaveAttribute('aria-expanded', 'false')
    expect(within(dialog).queryByRole('option', { name: 'Transport' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('option', { name: 'Dispatch' })).not.toBeInTheDocument()
  })
})
