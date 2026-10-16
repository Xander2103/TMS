import { describe, expect, it } from 'vitest'
import { buildDriverOptions } from '../resourceOptions'
import type { DriverListItem } from '../../drivers/types'

const t = (key: string, vars?: Record<string, unknown>) =>
  ({
    'planning.driverPicker.blocked': '⚠ Geblokkeerd',
    'planning.driverPicker.absent': '⚠ Afwezig op deze dag',
    'planning.driverPicker.unavailable': '⚠ Niet inzetbaar',
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
