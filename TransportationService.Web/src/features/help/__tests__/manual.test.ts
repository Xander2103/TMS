import { describe, expect, it } from 'vitest'
import { helpPathFor } from '../helpLinks'
import { helpRoleOf, isVisibleTo, orderCategories, primaryArticles } from '../helpRole'
import { ARTICLES } from '../manualContent'
import { parseArticle, parseInline, validateArticles, CATEGORY_ORDER } from '../manualParser'
import { searchManual } from '../manualSearch'

const roleWith = (...codes: string[]) => helpRoleOf((code) => codes.includes(code), false)

describe('manual content', () => {
  it('has no broken help link, related article or image without alt text', () => {
    expect(validateArticles(ARTICLES)).toEqual([])
  })
  it('only uses the known categories', () => {
    for (const a of ARTICLES) expect(CATEGORY_ORDER).toContain(a.category)
  })
  it('gives every workflow article the agreed structure', () => {
    const headings = ['WAT DOE JE HIER?', 'WANNEER GEBRUIK JE DIT?', 'STAPPEN', 'WAT GEBEURT ER DAARNA?', 'WAAR MOET JE OP LETTEN?', 'VEEL VOORKOMENDE PROBLEMEN']
    const free = new Set(['veelgestelde-vragen', 'probleemoplossing'])
    for (const a of ARTICLES.filter((x) => !x.quickstart && !free.has(x.id))) {
      const found = a.blocks.filter((b) => b.type === 'heading' && b.level === 2).map((b) => (b as { text: string }).text)
      expect(found, a.id).toEqual(headings)
    }
  })
  it('has a quickstart for every role', () => {
    const roles = ARTICLES.filter((a) => a.quickstart).flatMap((a) => a.roles).sort()
    expect(roles).toEqual(['accounting', 'admin', 'customer', 'dispatcher', 'driver', 'fleet', 'hr', 'management', 'planner', 'warehouse'])
  })
})

describe('parser', () => {
  it('reads frontmatter and blocks', () => {
    const a = parseArticle('---\nid: x\ntitle: T\ncategory: Dossiers\nroles: planner, hr\nrelated: y\n---\n## Kop\n\nTekst met **vet** en [link](help:y).\n\n3. drie\n\n![Alt](shot:foo "Bij")\n\n| A | B |\n|---|---|\n| 1 | 2 |\n', 7)
    expect(a).toMatchObject({ id: 'x', order: 7, roles: ['planner', 'hr'], related: ['y'] })
    expect(a.blocks.map((b) => b.type)).toEqual(['heading', 'paragraph', 'list', 'image', 'table'])
    expect(a.blocks[2]).toMatchObject({ ordered: true, start: 3 })
    expect(validateArticles([a])).toEqual(['x: broken related y', 'x: broken link to y'])
  })
  it('treats markup as text, never as HTML', () => {
    expect(parseInline('<img src=x> **b**')).toEqual([{ kind: 'text', text: '<img src=x> ' }, { kind: 'bold', text: 'b' }])
  })
})

describe('role-aware help', () => {
  it('follows the same order as the role landings', () => {
    expect(roleWith('roles.manage_permissions', 'planning.edit', 'orders.create')).toBe('admin')
    expect(roleWith('planning.edit', 'orders.create')).toBe('planner')
    expect(roleWith('planning.edit', 'operations.view')).toBe('dispatcher')
    expect(roleWith('invoices.change_status')).toBe('accounting')
    expect(roleWith('vehicles.edit')).toBe('fleet')
    expect(roleWith('employees.edit', 'inventory.issue')).toBe('hr')
    expect(roleWith('inventory.issue')).toBe('warehouse')
    expect(roleWith('operations.view', 'dashboard.view')).toBe('management')
    expect(roleWith('driver_workflow.execute')).toBe('driver')
    expect(helpRoleOf(() => false, true)).toBe('customer')
    expect(roleWith()).toBeNull()
  })
  it('gives a customer only customer help', () => {
    const visible = ARTICLES.filter((a) => isVisibleTo(a, 'customer'))
    expect(visible.some((a) => a.id === 'portaal-aanvraag')).toBe(true)
    for (const a of visible) expect(a.roles.includes('all') || a.roles.includes('customer'), a.id).toBe(true)
    expect(visible.some((a) => a.category === 'Beheer' || a.category === 'Planning')).toBe(false)
  })
  it('keeps the operator manual from other roles', () => {
    expect(ARTICLES.some((a) => a.category === 'Beheer' && isVisibleTo(a, 'admin'))).toBe(true)
    expect(ARTICLES.some((a) => a.id === 'back-up' && isVisibleTo(a, 'planner'))).toBe(false)
    expect(ARTICLES.some((a) => a.id === 'start-hr' && isVisibleTo(a, 'planner'))).toBe(false)
  })
  it('puts the role\'s own work first', () => {
    const all = [...new Set(ARTICLES.map((a) => a.category))]
    expect(orderCategories(all, 'planner')[0]).toBe('Planning')
    expect(orderCategories(all, 'dispatcher')[0]).toBe('Live opvolging')
    expect(orderCategories(all, 'hr')[0]).toBe('Personeel')
    expect(orderCategories(all, 'fleet')[0]).toBe('Wagenpark')
    expect(orderCategories(all, 'accounting')[0]).toBe('Boekhouding')
    expect(orderCategories(all, 'warehouse')[0]).toBe('Magazijn')
    expect(orderCategories(all, 'driver')[0]).toBe('Chauffeur')
    expect(primaryArticles(ARTICLES, 'driver').every((a) => a.category === 'Chauffeur')).toBe(true)
  })
})

describe('search', () => {
  it('finds on title, keyword and content, ignoring accents and case', () => {
    expect(searchManual(ARTICLES, 'FACTUUR').length).toBeGreaterThan(0)
    expect(searchManual(ARTICLES, 'redelivery')[0].article.id).toMatch(/herlevering/)
    expect(searchManual(ARTICLES, 'geeeen-zo-woord')).toEqual([])
    expect(searchManual(ARTICLES, '   ')).toEqual([])
  })
  it('ranks a title match first', () => {
    expect(searchManual(ARTICLES, 'Planbord')[0].article.title).toMatch(/planbord/i)
  })
})

describe('contextual help', () => {
  it('opens the matching article, and every target exists', () => {
    expect(helpPathFor('/dossiers')).toBe('/help/dossier-zoeken')
    expect(helpPathFor('/dossiers/abc')).toBe('/help/dossier-openen')
    expect(helpPathFor('/planning-center')).toBe('/help/planbord')
    expect(helpPathFor('/operations')).toBe('/help/live-opvolging')
    expect(helpPathFor('/invoices/abc')).toBe('/help/factuur-definitief')
    expect(helpPathFor('/inventory/issues')).toBe('/help/magazijn-uitgeven')
    expect(helpPathFor('/employees/abc')).toBe('/help/offboarding')
    expect(helpPathFor('/iets-anders')).toBe('/help')
    for (const p of ['/dossiers', '/dossiers/x', '/transport-orders', '/transport-orders/new', '/planning', '/planning-center', '/operations', '/incidents', '/exceptions', '/invoice-control', '/invoices', '/invoices/new', '/invoices/x', '/inventory', '/inventory/issues', '/employees', '/employees/x', '/fleet', '/vehicles', '/trailers', '/users', '/roles', '/settings/system', '/peppol', '/dashboard']) {
      const id = helpPathFor(p).replace('/help/', '')
      expect(ARTICLES.some((a) => a.id === id), p).toBe(true)
    }
  })
})
