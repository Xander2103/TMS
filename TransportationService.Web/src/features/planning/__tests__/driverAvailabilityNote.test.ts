import { describe, expect, it } from 'vitest'
import { buildDriverOptions } from '../resourceOptions'
import type { DriverListItem } from '../../drivers/types'

const t = (key: string, vars?: Record<string, unknown>) =>
  ({
    'planning.driverPicker.blocked': '⚠ Geblokkeerd',
    'planning.driverPicker.absent': '⚠ Afwezig op deze dag',
    'planning.driverPicker.unavailable': '⚠ Niet inzetbaar',
    'planning.driverPicker.qualification': '⚠ Kwalificaties nakijken',
    'planning.detail.employeeNumber': `Personeelsnr. ${String(vars?.number)}`,
  })[key] ?? key

function driver(overrides: Partial<DriverListItem>): DriverListItem {
  return {
    id: 'd1', driverNumber: 'CH-1', fullName: 'Jan Jansen', employeeNumber: 'MED-1', categoryName: null,
    availabilityStatus: 'Available', isActive: true, isBlocked: false, ...overrides,
  }
}

// HR acceptance P2-7: the picker says why a driver is not plainly available BEFORE selection.
describe('buildDriverOptions availability', () => {
  it('flags absent, non-deployable and blocked drivers; leaves available ones plain', () => {
    const [absent, suspended, blocked, free] = buildDriverOptions(
      [
        driver({ availabilityStatus: 'OnLeave' }),
        driver({ availabilityStatus: 'Unavailable' }),
        driver({ isBlocked: true }),
        driver({}),
      ],
      t as never,
    )
    expect(absent.subtitle).toContain('⚠ Afwezig op deze dag')
    expect(suspended.subtitle).toContain('⚠ Niet inzetbaar')
    expect(blocked.subtitle).toContain('⚠ Geblokkeerd')
    expect(free.subtitle).toBe('Personeelsnr. MED-1')
  })
})

describe('driver picker qualification indicator (M1)', () => {
  it('flags a major qualification issue, but availability problems take precedence', () => {
    const [issue, absentAndIssue] = buildDriverOptions(
      [driver({ hasQualificationIssue: true }), driver({ availabilityStatus: 'OnLeave', hasQualificationIssue: true })],
      t as never,
    )
    expect(issue.subtitle).toContain('⚠ Kwalificaties nakijken')
    expect(absentAndIssue.subtitle).toContain('⚠ Afwezig op deze dag')
    expect(absentAndIssue.subtitle).not.toContain('Kwalificaties')
  })
})

describe('vehicle and trailer picker context (M1)', () => {
  it('shows status, payload and equipment concisely; available vehicles carry no warning', async () => {
    const { buildVehicleOptions, buildTrailerOptions } = await import('../resourceOptions')
    const t = (key: string, vars?: Record<string, unknown>) => (vars ? `${key}:${JSON.stringify(vars)}` : key)
    const [crane, broken] = buildVehicleOptions([
      { id: 'v1', internalNumber: 'VRT-0003', licensePlate: '1-KRN-201', brand: 'Volvo', model: 'FH', operationalStatus: 'Available', payloadKg: 18000, hasCrane: true, adrSuitable: false },
      { id: 'v2', internalNumber: 'VRT-0009', licensePlate: '1-XYZ-999', brand: null, model: null, operationalStatus: 'InMaintenance', payloadKg: null, hasCrane: false, adrSuitable: true },
    ], t)
    expect(crane.subtitle).toContain('planning.vehiclePicker.crane')
    expect(crane.subtitle).toContain('planning.vehiclePicker.payload')
    expect(crane.subtitle).not.toContain('⚠')
    expect(broken.subtitle?.startsWith('planning.vehiclePicker.inMaintenance')).toBe(true)
    const [trailer] = buildTrailerOptions([{ id: 't1', internalNumber: 'OPL-1', licensePlate: 'Q-1', brand: null, model: null, operationalStatus: 'OutOfService' }], t)
    expect(trailer.subtitle).toBe('planning.vehiclePicker.outOfService')
  })
})
