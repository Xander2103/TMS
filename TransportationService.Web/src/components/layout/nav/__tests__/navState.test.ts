import { describe, expect, it } from 'vitest'
import { getNavModules } from '../navConfig'
import { filterModule, findActiveModuleId, moduleHasUnread } from '../navState'

const modules = getNavModules()
const allowAll = () => true

describe('findActiveModuleId', () => {
  it('matches a nested detail route to its module (longest prefix)', () => {
    expect(findActiveModuleId(modules, '/employees/abc-123')).toBe('personeel')
  })
  it('does not confuse /warehouses with /warehouse', () => {
    expect(findActiveModuleId(modules, '/warehouses')).toBe('magazijn')
    expect(findActiveModuleId(modules, '/warehouse')).toBe('magazijn')
  })
  it('returns null for an unknown route', () => {
    expect(findActiveModuleId(modules, '/nowhere')).toBeNull()
  })
})

describe('filterModule', () => {
  const transport = modules.find((m) => m.id === 'transport')!
  const portaal = modules.find((m) => m.id === 'portaal')!

  it('drops items the user lacks permission for and hides an emptied module', () => {
    const none = () => false
    expect(filterModule(transport, { hasAnyPermission: none, hasEmployee: false, query: '' })).toBeNull()
  })

  it('keeps only permitted items', () => {
    const only = (codes: string[]) => codes.includes('planning.view')
    const vm = filterModule(transport, { hasAnyPermission: only, hasEmployee: false, query: '' })!
    expect(vm.items.map((i) => i.to)).toEqual(['/planning', '/planning-center'])
  })

  it('hides an employee-only module when the user has no employee link', () => {
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })).toBeNull()
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: true, query: '' })).not.toBeNull()
  })

  it('narrows items by case-insensitive query and drops non-matching modules', () => {
    const vm = filterModule(transport, { hasAnyPermission: allowAll, hasEmployee: false, query: 'plan' })!
    expect(vm.items.map((i) => i.label)).toEqual(['Planning', 'Planbord'])
    expect(filterModule(transport, { hasAnyPermission: allowAll, hasEmployee: false, query: 'zzz' })).toBeNull()
  })

  it('filters subgroup items and drops emptied subgroups', () => {
    const master = modules.find((m) => m.id === 'stamgegevens')!
    const vm = filterModule(master, { hasAnyPermission: allowAll, hasEmployee: false, query: 'categorie' })!
    expect(vm.subgroups.map((s) => s.label)).toEqual(['Categorieën'])
  })
})

describe('moduleHasUnread', () => {
  it('is true only when a badged item exists and the count is positive', () => {
    const comms = filterModule(modules.find((m) => m.id === 'communicatie')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(comms, 3)).toBe(true)
    expect(moduleHasUnread(comms, 0)).toBe(false)
    const transport = filterModule(modules.find((m) => m.id === 'transport')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(transport, 3)).toBe(false)
  })
})
