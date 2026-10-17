import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'
import { buildTrailerOptions, buildVehicleOptions } from '../resourceOptions'

// FL-H1/H2/H4: the pickers show the server's fleet readiness for the trip day before selection.

const t = (key: string, params?: Record<string, string | number>) => translate('nl', key, params)
const vehicle = { id: 'v', internalNumber: 'VRT-0012', licensePlate: '1-TFG-001', brand: 'Volvo', model: 'FM', payloadKg: 10000, operationalStatus: 'Available' }

describe('equipment readiness in the planning pickers', () => {
  it('blocked → ⛔ with the server summary, before brand and payload', () => {
    const [option] = buildVehicleOptions([{ ...vehicle, readiness: 'Blocked', readinessSummary: 'Onderhoud gepland (29/10 – 30/10)' }], t)
    expect(option.subtitle).toBe('⛔ Onderhoud gepland (29/10 – 30/10) · Volvo FM · 10.000 kg laadvermogen')
  })

  it('warning → ⚠; deployable → nothing; "in gebruik" stays as a note', () => {
    expect(buildVehicleOptions([{ ...vehicle, readiness: 'Warning', readinessSummary: 'Tachograafijking vervallen' }], t)[0].subtitle).toMatch(/^⚠ Tachograafijking vervallen · /)
    expect(buildVehicleOptions([{ ...vehicle, readiness: 'Deployable', readinessSummary: null }], t)[0].subtitle).toBe('Volvo FM · 10.000 kg laadvermogen')
    expect(buildVehicleOptions([{ ...vehicle, operationalStatus: 'InUse', readiness: 'Deployable' }], t)[0].subtitle).toMatch(/^In gebruik · /)
  })

  it('without readiness the M1 status note remains (older payloads)', () => {
    expect(buildVehicleOptions([{ ...vehicle, operationalStatus: 'OutOfService' }], t)[0].subtitle).toMatch(/^⚠ Buiten dienst · /)
  })

  it('trailers get the same treatment', () => {
    const [option] = buildTrailerOptions([{ id: 't', internalNumber: 'OPL-0003', licensePlate: 'Q-TFT-001', brand: 'Schmitz', model: null, readiness: 'Blocked', readinessSummary: 'Technische keuring verlopen' }], t)
    expect(option.subtitle).toBe('⛔ Technische keuring verlopen · Schmitz')
  })
})
