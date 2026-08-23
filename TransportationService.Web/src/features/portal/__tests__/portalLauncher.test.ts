import { describe, expect, it } from 'vitest'
import { PORTAL_MODULES, visibleModules } from '../modules'

function withPermissions(granted: string[]) {
  return (codes: string[]) => codes.some((code) => granted.includes(code))
}

// Module labels are translation keys since the i18n conversion; render sites resolve them via t().
describe('portal module launcher', () => {
  it('shows driver modules only to driver-workflow holders', () => {
    const driver = visibleModules(PORTAL_MODULES, withPermissions(['driver_workflow.view', 'scanning.execute', 'exceptions.create']))
    expect(driver.map((m) => m.label)).toContain('portalHome.modules.myTrips.label')
    expect(driver.map((m) => m.label)).toContain('portalHome.modules.scanning.label')
    expect(driver.map((m) => m.label)).toContain('portalHome.modules.exceptions.label')
  })

  it('hides driver/warehouse modules from a plain employee', () => {
    const employee = visibleModules(PORTAL_MODULES, withPermissions([]))
    const labels = employee.map((m) => m.label)
    expect(labels).not.toContain('portalHome.modules.myTrips.label')
    expect(labels).not.toContain('portalHome.modules.scanning.label')
    expect(labels).not.toContain('portalHome.modules.exceptions.label')
    // The employee core is always there.
    expect(labels).toEqual(
      expect.arrayContaining([
        'portalHome.modules.myPlanning.label',
        'portalHome.modules.leave.label',
        'portalHome.modules.qualifications.label',
        'portalHome.modules.notifications.label',
        'portalHome.modules.myProfile.label',
      ]),
    )
  })

  it('gives warehouse profiles scanning without trips', () => {
    const warehouse = visibleModules(PORTAL_MODULES, withPermissions(['scanning.execute', 'exceptions.view']))
    const labels = warehouse.map((m) => m.label)
    expect(labels).toContain('portalHome.modules.scanning.label')
    expect(labels).toContain('portalHome.modules.exceptions.label')
    expect(labels).not.toContain('portalHome.modules.myTrips.label')
  })
})
