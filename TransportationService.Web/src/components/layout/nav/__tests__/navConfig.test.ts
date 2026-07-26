import { describe, expect, it } from 'vitest'
import { getNavModules } from '../navConfig'

describe('getNavModules', () => {
  const modules = getNavModules()

  it('returns the eleven modules in business order', () => {
    expect(modules.map((m) => m.id)).toEqual([
      'portaal', 'dashboard', 'transport', 'magazijn', 'klanten', 'prijzen',
      'personeel', 'vloot', 'communicatie', 'beheer', 'stamgegevens',
    ])
  })

  it('moves Prijsinstellingen from Klanten to the new Prijzen module, alongside Tarieventabellen', () => {
    const klanten = modules.find((m) => m.id === 'klanten')!
    expect(klanten.items!.map((i) => i.to)).not.toContain('/settings/pricing')

    const prijzen = modules.find((m) => m.id === 'prijzen')!
    const byRoute = new Map(prijzen.items!.map((i) => [i.to, i.label]))
    expect(byRoute.get('/pricing/tables')).toBe('Tarieventabellen')
    expect(byRoute.get('/settings/pricing')).toBe('Prijsinstellingen')
  })

  it('flags the portal module as employee-only', () => {
    expect(modules.find((m) => m.id === 'portaal')?.requiresEmployee).toBe(true)
  })

  it('renames the personnel list to "Medewerkers" and drops the top-level Chauffeurs entry', () => {
    const personeel = modules.find((m) => m.id === 'personeel')!
    const labels = personeel.items!.map((i) => i.label)
    expect(labels).toContain('Medewerkers')
    expect(labels).not.toContain('Chauffeurs')
  })

  it('labels /warehouse as "Magazijn" and keeps /warehouses as "Magazijnen"', () => {
    const magazijn = modules.find((m) => m.id === 'magazijn')!
    const byRoute = new Map(magazijn.items!.map((i) => [i.to, i.label]))
    expect(byRoute.get('/warehouse')).toBe('Magazijn')
    expect(byRoute.get('/warehouses')).toBe('Magazijnen')
  })

  it('marks Meldingen with the notifications badge', () => {
    const comms = modules.find((m) => m.id === 'communicatie')!
    expect(comms.items!.find((i) => i.to === '/notifications')?.badge).toBe('notifications')
  })

  it('builds Stamgegevens from the lookup registry with three subgroups', () => {
    const master = modules.find((m) => m.id === 'stamgegevens')!
    expect(master.subgroups!.map((s) => s.label)).toEqual(['Algemeen', 'Categorieën', 'Templates'])
    const categorieen = master.subgroups!.find((s) => s.label === 'Categorieën')!
    expect(categorieen.items.map((i) => i.to)).toContain('/master-data/customer-categories')
    const algemeen = master.subgroups!.find((s) => s.label === 'Algemeen')!
    expect(algemeen.items[0].to).toBe('/settings/legal-entities')
  })
})
