import { describe, expect, it, vi } from 'vitest'
import { translate } from '../../../i18n/translations'
import type { TranslateFn } from '../../../i18n/localeContext'
import { buildCustomerAttention } from '../utils/customerAttention'
import type { CustomerDetail } from '../types'

const t = ((key: string, params?: Record<string, unknown>) => translate('nl', key, params as never)) as TranslateFn

function customer(overrides: Partial<CustomerDetail> = {}): CustomerDetail {
  return {
    id: 'c1', name: 'Haven BV', email: 'info@haven.example', invoiceEmail: null, isBlocked: false, blockReason: null,
    peppolEnabled: false, peppolId: null, peppolScheme: null, peppolValidationStatus: 'Unknown',
    purchaseOrderRequired: false, signedDeliveryNoteRequired: false, customerReferenceRequired: false,
    countryCode: 'BE', vatCountryCode: null,
    ...overrides,
  } as CustomerDetail
}

describe('buildCustomerAttention (B17)', () => {
  it('stays empty for a normally configured customer — no warning noise', () => {
    expect(buildCustomerAttention(customer(), [], t)).toEqual([])
  })

  it('words the backend fiscal notices for the operator, with the country filled in, and falls back to the backend text', () => {
    const items = buildCustomerAttention(
      customer({ vatCountryCode: 'DE' }),
      [
        { code: 'domestic-vat-foreign-customer', message: 'backend text' },
        { code: 'some-future-code', message: 'Onbekende melding uit de backend.' },
      ],
      t,
    )
    expect(items.map((i) => [i.severity, i.text])).toEqual([
      ['warning', 'Deze klant staat in DE maar wordt met binnenlandse btw gefactureerd. Controleer of dat klopt.'],
      ['warning', 'Onbekende melding uit de backend.'],
    ])
  })

  it('puts a block first as critical, flags missing invoicing channels and lists requirements as info', () => {
    const unblock = vi.fn()
    const complete = vi.fn()
    const items = buildCustomerAttention(
      customer({ isBlocked: true, blockReason: 'Openstaande facturen', email: null, purchaseOrderRequired: true, signedDeliveryNoteRequired: true }),
      [],
      t,
      { onUnblock: unblock, onComplete: complete },
    )
    expect(items.map((i) => i.key)).toEqual(['blocked', 'no-invoice-channel', 'po', 'signed-note'])
    expect(items[0]).toMatchObject({ severity: 'critical', detail: 'Openstaande facturen' })
    expect(items[2].severity).toBe('info')
    items[0].action!.onSelect!()
    items[1].action!.onSelect!()
    expect(unblock).toHaveBeenCalled()
    expect(complete).toHaveBeenCalledWith('facturatie')
  })

  it('warns about Peppol only when it is switched on and cannot work', () => {
    expect(buildCustomerAttention(customer({ peppolEnabled: true }), [], t).map((i) => i.key)).toEqual(['peppol-id'])
    expect(
      buildCustomerAttention(customer({ peppolEnabled: true, peppolId: '0403', peppolValidationStatus: 'NotFound' }), [], t).map((i) => i.key),
    ).toEqual(['peppol-not-found'])
    expect(buildCustomerAttention(customer({ peppolEnabled: true, peppolId: '0403', peppolValidationStatus: 'Found' }), [], t)).toEqual([])
  })

  it('offers no actions to a user who may not edit', () => {
    const items = buildCustomerAttention(customer({ isBlocked: true, email: null }), [], t)
    expect(items.every((i) => i.action === undefined)).toBe(true)
  })
})
