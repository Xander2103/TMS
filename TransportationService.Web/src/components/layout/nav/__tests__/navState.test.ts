import { describe, expect, it } from 'vitest'
import type { NavModule } from '../navConfig'
import { getNavModules } from '../navConfig'
import { filterModule, findActiveModuleId, moduleHasUnread } from '../navState'
import { Truck } from 'lucide-react'

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
  const planning = modules.find((m) => m.id === 'planning')!
  const portaal = modules.find((m) => m.id === 'portaal')!

  it('drops items the user lacks permission for and hides an emptied module', () => {
    const none = () => false
    expect(filterModule(planning, { hasAnyPermission: none, hasEmployee: false, query: '' })).toBeNull()
  })

  it('keeps only permitted items', () => {
    const only = (codes: string[]) => codes.includes('planning.view')
    const vm = filterModule(planning, { hasAnyPermission: only, hasEmployee: false, query: '' })!
    expect(vm.items.map((i) => i.to)).toEqual(['/planning-center', '/planning'])
  })

  it('hides an employee-only module when the user has no employee link', () => {
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })).toBeNull()
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: true, query: '' })).not.toBeNull()
  })

  it('narrows items by case-insensitive query and drops non-matching modules', () => {
    const vm = filterModule(planning, { hasAnyPermission: allowAll, hasEmployee: false, query: 'planbord' })!
    expect(vm.items.map((i) => i.label)).toEqual(['Planbord'])
    expect(filterModule(planning, { hasAnyPermission: allowAll, hasEmployee: false, query: 'zzz' })).toBeNull()
  })

  it('filters subgroup items and drops emptied subgroups', () => {
    const parameters = modules.find((m) => m.id === 'parameters')!
    const vm = filterModule(parameters, { hasAnyPermission: allowAll, hasEmployee: false, query: 'categorie' })!
    expect(vm.subgroups.map((s) => s.label)).toEqual(['Stamgegevens'])
  })
})

describe('moduleHasUnread', () => {
  it('is true only when a badged item exists and the count is positive', () => {
    const vandaag = filterModule(modules.find((m) => m.id === 'vandaag')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(vandaag, 3)).toBe(true)
    expect(moduleHasUnread(vandaag, 0)).toBe(false)
    const planning = filterModule(modules.find((m) => m.id === 'planning')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(planning, 3)).toBe(false)
  })
})

describe('findActiveModuleId — cross-module prefix boundary', () => {
  // Two DIFFERENT modules whose routes share a prefix: this is the collision the
  // trailing-slash guard must prevent. If the guard regressed to a bare startsWith,
  // '/warehouses' would wrongly resolve to the '/warehouse' module.
  const synthetic: NavModule[] = [
    { id: 'alpha', label: 'A', icon: Truck, items: [{ label: 'Ware', to: '/warehouse' }] },
    { id: 'beta', label: 'B', icon: Truck, items: [{ label: 'Wares', to: '/warehouses' }] },
  ]

  it('does not prefix-match /warehouses onto the /warehouse module', () => {
    expect(findActiveModuleId(synthetic, '/warehouses')).toBe('beta')
    expect(findActiveModuleId(synthetic, '/warehouse')).toBe('alpha')
    expect(findActiveModuleId(synthetic, '/warehouse/123')).toBe('alpha')
  })
})

describe('filterModule / moduleHasUnread — nested children recursion', () => {
  const nested: NavModule = {
    id: 'nest',
    label: 'Nest',
    icon: Truck,
    items: [
      {
        label: 'Parent',
        to: '/parent',
        permissions: ['p.parent'],
        children: [
          { label: 'Alpha child', to: '/parent/alpha', permissions: ['p.alpha'] },
          { label: 'Beta child', to: '/parent/beta', badge: 'notifications' },
        ],
      },
    ],
  }

  it('keeps a parent when only a descendant matches the query', () => {
    const vm = filterModule(nested, { hasAnyPermission: () => true, hasEmployee: false, query: 'beta child' })!
    expect(vm.items).toHaveLength(1)
    expect(vm.items[0].children!.map((c) => c.label)).toEqual(['Beta child'])
  })

  it('drops descendants the user lacks permission for, keeps permitted and ungated ones', () => {
    const only = (codes: string[]) => codes.includes('p.parent') || codes.includes('p.alpha')
    const vm = filterModule(nested, { hasAnyPermission: only, hasEmployee: false, query: '' })!
    expect(vm.items[0].children!.map((c) => c.to)).toEqual(['/parent/alpha', '/parent/beta'])
  })

  it('drops the parent when its only query-matching descendant is filtered out by permission', () => {
    const only = (codes: string[]) => codes.includes('p.parent')
    const vm = filterModule(nested, { hasAnyPermission: only, hasEmployee: false, query: 'alpha child' })
    expect(vm).toBeNull()
  })

  it('detects an unread badge nested inside children', () => {
    const vm = filterModule(nested, { hasAnyPermission: () => true, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(vm, 4)).toBe(true)
    expect(moduleHasUnread(vm, 0)).toBe(false)
  })
})
