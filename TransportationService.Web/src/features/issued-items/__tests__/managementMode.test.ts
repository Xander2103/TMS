import { describe, expect, it } from 'vitest'
import type { IssuedItemTemplate } from '../issuedItemsApi'
import {
  clothingSizeSuggestions,
  flagsFromMode,
  insertClothingSize,
  modeFromFlags,
  newVariantRow,
  parseJobFunctionCodes,
  parseNonNegative,
  presetRows,
  serializeJobFunctionCodes,
  splitQuickVariants,
  suggestDefaultsFromSiblings,
  validateVariantRows,
} from '../managementMode'

function item(overrides: Partial<IssuedItemTemplate>): IssuedItemTemplate {
  return {
    id: 'x',
    name: 'X',
    category: 'Kleding',
    categoryId: 'cat-kleding',
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
    description: null,
    unit: 'piece',
    notes: null,
    stockTrackingEnabled: false,
    variantsEnabled: false,
    allowNegativeStock: false,
    lowStockThreshold: null,
    minimumStock: null,
    targetStockLevel: null,
    reorderQuantity: null,
    negativeStockRequiresReason: false,
    stockStatus: 'Normal',
    storageLocation: null,
    currentStock: 0,
    totalAvailable: 0,
    lowStock: false,
    variantCount: 0,
    ...overrides,
  }
}

describe('Beheerwijze ↔ backend flags', () => {
  it('maps every stored combination onto exactly one mode and back', () => {
    expect(modeFromFlags({ stockTrackingEnabled: false, variantsEnabled: false })).toBe('individual')
    expect(modeFromFlags({ stockTrackingEnabled: true, variantsEnabled: false })).toBe('stock')
    expect(modeFromFlags({ stockTrackingEnabled: true, variantsEnabled: true })).toBe('variants')
    // Legacy rows can never violate "variants require stock", but the mapping stays total anyway.
    expect(modeFromFlags({ stockTrackingEnabled: false, variantsEnabled: true })).toBe('variants')

    expect(flagsFromMode('individual')).toEqual({ stockTrackingEnabled: false, variantsEnabled: false })
    expect(flagsFromMode('stock')).toEqual({ stockTrackingEnabled: true, variantsEnabled: false })
    expect(flagsFromMode('variants')).toEqual({ stockTrackingEnabled: true, variantsEnabled: true })
  })
})

describe('job-function codes (stored CSV ↔ chips)', () => {
  it('parses the legacy CSV leniently and serialises it back normalised', () => {
    expect(parseJobFunctionCodes(null)).toEqual([])
    expect(parseJobFunctionCodes(' CHAUF, plan ;MAGM,,chauf ')).toEqual(['CHAUF', 'plan', 'MAGM'])
    expect(serializeJobFunctionCodes([])).toBeNull()
    expect(serializeJobFunctionCodes(['CHAUF', ' PLAN '])).toBe('CHAUF,PLAN')
  })
})

describe('category-aware suggestions (learned from siblings, no names in code)', () => {
  const items = [
    item({ id: 'a', categoryId: 'cat-kleding', stockTrackingEnabled: true, variantsEnabled: true, returnRequired: true }),
    item({ id: 'b', categoryId: 'cat-kleding', stockTrackingEnabled: true, variantsEnabled: true, returnRequired: true }),
    item({ id: 'c', categoryId: 'cat-kleding', stockTrackingEnabled: true, variantsEnabled: false, returnRequired: false }),
    item({ id: 'd', categoryId: 'cat-it', requiresSerialNumber: true, returnRequired: true }),
    item({ id: 'e', categoryId: 'cat-it', requiresSerialNumber: true, isActive: false }),
  ]

  it('suggests the most common mode and the majority of the issue flags', () => {
    expect(suggestDefaultsFromSiblings(items, 'cat-kleding')).toEqual({
      mode: 'variants',
      requiresSerialNumber: false,
      requiresReceivedDate: true,
      returnRequired: true,
      basedOn: 3,
    })
  })

  it('ignores inactive items and returns nothing for an empty or unknown category', () => {
    expect(suggestDefaultsFromSiblings(items, 'cat-it')).toEqual({
      mode: 'individual',
      requiresSerialNumber: true,
      requiresReceivedDate: true,
      returnRequired: true,
      basedOn: 1,
    })
    expect(suggestDefaultsFromSiblings(items, 'cat-new')).toBeNull()
    expect(suggestDefaultsFromSiblings(items, null)).toBeNull()
    expect(suggestDefaultsFromSiblings(undefined, 'cat-kleding')).toBeNull()
  })
})

describe('inline variant rows', () => {
  it('splits a quick-add list and skips blanks and duplicates (case-insensitive)', () => {
    expect(splitQuickVariants('S, M,, L; xl\nXL', ['m'])).toEqual(['S', 'L', 'xl'])
  })

  it('validates labels: required and unique', () => {
    const rows = [newVariantRow('S'), newVariantRow(''), newVariantRow(' s ')]
    const issues = validateVariantRows(rows)
    expect(issues).toEqual([
      { key: rows[1].key, message: 'issuedItems.form.variantNameRequired' },
      { key: rows[2].key, message: 'issuedItems.form.variantDuplicate' },
    ])
    expect(validateVariantRows([newVariantRow('S'), newVariantRow('M')])).toEqual([])
  })

  it('parses numeric inputs as non-negative integers, empty as null', () => {
    expect(parseNonNegative('')).toBeNull()
    expect(parseNonNegative('  ')).toBeNull()
    expect(parseNonNegative('12')).toBe(12)
    expect(parseNonNegative('3.9')).toBe(3)
    expect(parseNonNegative('-4')).toBe(0)
    expect(parseNonNegative('abc')).toBe(0)
  })
})

describe('variant presets (starting points, frontend only)', () => {
  it('Kledingmaten starts with S, M, L, XL as ordinary empty rows; Zelf bepalen starts empty', () => {
    const clothing = presetRows('clothing')
    expect(clothing.map((row) => row.label)).toEqual(['S', 'M', 'L', 'XL'])
    expect(clothing.every((row) => row.stock === '' && row.threshold === '')).toBe(true)
    expect(new Set(clothing.map((row) => row.key)).size).toBe(4)
    expect(presetRows('custom')).toEqual([])
  })

  it('suggests the canonical sizes that are missing, case-insensitively, in wearing order', () => {
    expect(clothingSizeSuggestions(presetRows('clothing'))).toEqual(['XS', 'XXL', '3XL'])
    expect(clothingSizeSuggestions([newVariantRow('xs'), newVariantRow(' m '), newVariantRow('zwart')])).toEqual(['S', 'L', 'XL', 'XXL', '3XL'])
    expect(clothingSizeSuggestions([])).toEqual(['XS', 'S', 'M', 'L', 'XL', 'XXL', '3XL'])
  })

  it('inserts a canonical size at its wearing position and appends anything else', () => {
    const labels = (rows: { label: string }[]) => rows.map((row) => row.label)
    const base = presetRows('clothing')
    expect(labels(insertClothingSize(base, newVariantRow('XS')))).toEqual(['XS', 'S', 'M', 'L', 'XL'])
    expect(labels(insertClothingSize(base, newVariantRow('XXL')))).toEqual(['S', 'M', 'L', 'XL', 'XXL'])
    // A removed middle size comes back where it belongs; free labels keep their place.
    const withoutM = [newVariantRow('S'), newVariantRow('zwart'), newVariantRow('L'), newVariantRow('XL')]
    expect(labels(insertClothingSize(withoutM, newVariantRow('M')))).toEqual(['S', 'zwart', 'M', 'L', 'XL'])
    expect(labels(insertClothingSize(base, newVariantRow('maat 43')))).toEqual(['S', 'M', 'L', 'XL', 'maat 43'])
    expect(labels(insertClothingSize([], newVariantRow('XL')))).toEqual(['XL'])
  })
})
