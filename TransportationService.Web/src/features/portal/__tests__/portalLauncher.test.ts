import { describe, expect, it } from 'vitest'
import { appRoutePermissions } from '../../../routes/appRoutePermissions'
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

  it('sends warehouse profiles to the Magazijn tile, never to the driver-only /my-trips', () => {
    // Magazijn holds scanning.execute but not driver_workflow.view, which guards /my-trips.
    const magazijn = ['warehouse.view', 'packages.view', 'scanning.view', 'scanning.execute', 'exceptions.view', 'attendance.self']
    const labels = visibleModules(PORTAL_MODULES, withPermissions(magazijn)).map((m) => m.label)
    expect(labels).toContain('portalHome.modules.warehouse.label')
    expect(labels).toContain('portalHome.modules.exceptions.label')
    expect(labels).not.toContain('portalHome.modules.scanning.label')
    expect(labels).not.toContain('portalHome.modules.myTrips.label')
  })

  it('never shows a tile whose route guard would answer "Geen toegang"', () => {
    const guards = appRoutePermissions()
    const profiles = [
      ['driver_workflow.view', 'driver_workflow.execute', 'scanning.view', 'scanning.execute', 'exceptions.create', 'attendance.self'],
      ['warehouse.view', 'packages.view', 'scanning.view', 'scanning.execute', 'exceptions.view', 'attendance.self'],
      ['scanning.execute'],
    ]
    for (const granted of profiles) {
      for (const module of visibleModules(PORTAL_MODULES, withPermissions(granted))) {
        const guard = guards[module.to.split('?')[0]]
        if (guard) expect(withPermissions(granted)(guard), `${module.label} → ${module.to}`).toBe(true)
      }
    }
  })
})
