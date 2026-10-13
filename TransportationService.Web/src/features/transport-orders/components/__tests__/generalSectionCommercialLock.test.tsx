import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { GeneralSection } from '../sections/GeneralSection'
import type { TransportOrderDetail } from '../../types'

/**
 * Wave 1 blocker C-02 (UI half): on an EXISTING order the customer and the invoicing entity may
 * only be moved through their dedicated flows ("Klant wijzigen" / "Entiteit wijzigen" on the
 * detail page), so both selects are locked and point the user at them. Cosmetic only — the guard
 * itself is server-side (OrderUpdateIntegrityTests).
 */
const noop = () => {}

const baseProps = {
  customers: [
    { id: 'cust-1', name: 'Haven BV', customerNumber: 'KL-1' },
    { id: 'cust-2', name: 'Dok NV', customerNumber: 'KL-2' },
  ] as never,
  legalEntities: [{ id: 'ent-1', displayName: 'Acme Transport BV', isDefault: true, isActive: true }] as never,
  customerRequirements: null,
  requirementHints: [],
  customerId: 'cust-1',
  setCustomerId: noop,
  customerReference: '',
  setCustomerReference: noop,
  orderDate: '2026-08-30',
  setOrderDate: noop,
  legalEntityId: 'ent-1',
  setLegalEntityId: noop,
  dieselSurchargeOverride: false,
  setDieselSurchargeOverride: noop,
  dieselSurchargePercentOverride: '',
  setDieselSurchargePercentOverride: noop,
  dieselSurchargeOverrideReason: '',
  setDieselSurchargeOverrideReason: noop,
  saving: false,
  errors: {},
}

const existingOrder = {
  id: 'order-1',
  customerId: 'cust-1',
  customerName: 'Haven BV',
} as unknown as TransportOrderDetail

function selects(container: HTMLElement) {
  return {
    customer: container.querySelector<HTMLSelectElement>('#to-customer')!,
    entity: container.querySelector<HTMLSelectElement>('#to-legal-entity')!,
  }
}

describe('GeneralSection commercial lock', () => {
  it('leaves customer and entity editable on a blank new order', () => {
    const { container } = render(<GeneralSection {...baseProps} mode="create" />)
    const { customer, entity } = selects(container)

    expect(customer).toBeEnabled()
    expect(entity).toBeEnabled()
    expect(screen.queryByText(/'Klant wijzigen'/)).toBeNull()
  })

  /**
   * Regression (review I-2): the lock must key off the FORM MODE, not off the presence of an
   * `order` prop — NewTransportOrderPage passes an existing order as a TEMPLATE ("nieuwe opdracht
   * op basis van deze"). Locking there would make it impossible to raise the same transport for
   * another customer, and "Klant wijzigen" does not exist for an order that is not created yet.
   */
  it('leaves customer and entity editable when creating from a template', () => {
    const { container } = render(<GeneralSection {...baseProps} mode="create" order={existingOrder} />)
    const { customer, entity } = selects(container)

    expect(customer).toBeEnabled()
    expect(entity).toBeEnabled()
    expect(screen.queryByText(/'Klant wijzigen'/)).toBeNull()
  })

  it('locks customer and entity when editing a persisted order and explains where to change them', () => {
    const { container } = render(<GeneralSection {...baseProps} mode="edit" order={existingOrder} />)
    const { customer, entity } = selects(container)

    expect(customer).toBeDisabled()
    expect(entity).toBeDisabled()
    expect(screen.getByText(/'Klant wijzigen'/)).toBeInTheDocument()
    expect(screen.getByText(/'Entiteit wijzigen'/)).toBeInTheDocument()
  })

  it('keeps the current values selected so nothing is silently reset', () => {
    const { container } = render(<GeneralSection {...baseProps} mode="edit" order={existingOrder} />)
    const { customer, entity } = selects(container)

    expect(customer.value).toBe('cust-1')
    expect(entity.value).toBe('ent-1')
  })
})
