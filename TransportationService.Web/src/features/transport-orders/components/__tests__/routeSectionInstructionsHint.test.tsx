import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { RouteSection } from '../sections/RouteSection'
import { emptyStop } from '../sections/orderFormState'

// The two async pickers on a stop row are irrelevant here and would otherwise hit the network.
vi.mock('../../../locations/components/LocationSelect', () => ({
  LocationSelect: ({ id }: { id?: string }) => <input id={id} aria-label="locatie" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))

/**
 * Wave 1 fix B (B4). Coordinator ruling on the H-14 classification: the general stop
 * "Instructies" field stays portal-visible, because it carries the customer's own delivery
 * instruction submitted through the portal. It is a SHARED write surface though — a planner edits
 * the same column — so the internal form has to say out loud that whatever is typed here goes
 * back to the customer. Internal handling notes belong in the access/loading/unloading
 * instructions, which `PortalStopDto` never projects.
 *
 * The hint is the only thing standing between a dispatcher's private remark and the customer's
 * own order page, so it is pinned by a test rather than left to survive the next refactor by luck.
 */
const noop = () => {}

const baseProps = {
  stops: [emptyStop('Loading')],
  customerId: 'cust-1',
  saving: false,
  locationHours: {},
  errors: {},
  onAddStop: noop,
  setStop: noop,
  moveStop: noop,
  onRemoveStop: noop,
  onRequestRefresh: noop,
}

describe('RouteSection stop instructions', () => {
  it('warns that the general instruction field is visible to the customer in the portal', () => {
    render(<RouteSection {...baseProps} />)

    const hint = screen.getByText(/Zichtbaar voor de klant in het klantportaal/i)
    expect(hint).toBeTruthy()
    // It must sit on the general "Instructies" field, not on some other row.
    const field = hint.closest('.ui-form-field')!
    expect(field.querySelector('label')?.textContent).toContain('Instructies')
    expect(field.querySelector('input')?.id).toMatch(/^st-instr-/)
  })

  it('points internal remarks at the fields that are never exposed', () => {
    render(<RouteSection {...baseProps} />)

    expect(
      screen.getByText(/Interne opmerkingen horen bij toegangs-, laad- of losinstructies\./i),
    ).toBeTruthy()
  })
})
