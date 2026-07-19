import { describe, expect, it } from 'vitest'
import { PORTAL_MODULES, visibleModules } from '../modules'

function withPermissions(granted: string[]) {
  return (codes: string[]) => codes.some((code) => granted.includes(code))
}

describe('portal module launcher', () => {
  it('shows driver modules only to driver-workflow holders', () => {
    const driver = visibleModules(PORTAL_MODULES, withPermissions(['driver_workflow.view', 'scanning.execute', 'exceptions.create']))
    expect(driver.map((m) => m.label)).toContain('Mijn ritten')
    expect(driver.map((m) => m.label)).toContain('Scannen & laden')
    expect(driver.map((m) => m.label)).toContain('Afwijkingen')
  })

  it('hides driver/warehouse modules from a plain employee', () => {
    const employee = visibleModules(PORTAL_MODULES, withPermissions([]))
    const labels = employee.map((m) => m.label)
    expect(labels).not.toContain('Mijn ritten')
    expect(labels).not.toContain('Scannen & laden')
    expect(labels).not.toContain('Afwijkingen')
    // The employee core is always there.
    expect(labels).toEqual(expect.arrayContaining(['Mijn planning', 'Verlof', 'Kwalificaties', 'Meldingen', 'Mijn profiel']))
  })

  it('gives warehouse profiles scanning without trips', () => {
    const warehouse = visibleModules(PORTAL_MODULES, withPermissions(['scanning.execute', 'exceptions.view']))
    const labels = warehouse.map((m) => m.label)
    expect(labels).toContain('Scannen & laden')
    expect(labels).toContain('Afwijkingen')
    expect(labels).not.toContain('Mijn ritten')
  })
})
