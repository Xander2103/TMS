import type { IssuedItemTemplate } from './issuedItemsApi'

/**
 * "Beheerwijze" — the ONE question the form asks instead of two technical toggles. It is a
 * presentation concept: the backend keeps its `stockTrackingEnabled` + `variantsEnabled`
 * booleans unchanged, and every stored combination maps onto exactly one mode.
 *
 *   individual → no stock tracking      (laptop, badge, tankkaart — each piece is followed on its own)
 *   stock      → stock tracking         (helm, handschoenen — quantities, one shared count)
 *   variants   → stock + variants       (kleding, schoenen — one count per size/version)
 */
export type ManagementMode = 'individual' | 'stock' | 'variants'

export const MANAGEMENT_MODES: readonly ManagementMode[] = ['individual', 'stock', 'variants']

/** Vertaalsleutels — renderen als t(MANAGEMENT_MODE_LABELS[mode]). */
export const MANAGEMENT_MODE_LABELS: Record<ManagementMode, string> = {
  individual: 'issuedItems.mode.individual',
  stock: 'issuedItems.mode.stock',
  variants: 'issuedItems.mode.variants',
}

export const MANAGEMENT_MODE_DESCRIPTIONS: Record<ManagementMode, string> = {
  individual: 'issuedItems.mode.individualDescription',
  stock: 'issuedItems.mode.stockDescription',
  variants: 'issuedItems.mode.variantsDescription',
}

export const MANAGEMENT_MODE_EXAMPLES: Record<ManagementMode, string> = {
  individual: 'issuedItems.mode.individualExamples',
  stock: 'issuedItems.mode.stockExamples',
  variants: 'issuedItems.mode.variantsExamples',
}

/** Derives the mode from the two stored booleans (legacy rows included). */
export function modeFromFlags(flags: { stockTrackingEnabled: boolean; variantsEnabled: boolean }): ManagementMode {
  if (flags.variantsEnabled) return 'variants'
  if (flags.stockTrackingEnabled) return 'stock'
  return 'individual'
}

/** The two backend booleans a mode stands for; mirrors the backend rule "variants require stock". */
export function flagsFromMode(mode: ManagementMode): { stockTrackingEnabled: boolean; variantsEnabled: boolean } {
  return {
    stockTrackingEnabled: mode !== 'individual',
    variantsEnabled: mode === 'variants',
  }
}

// ---- "Voor wie is dit bedoeld?" -----------------------------------------------------------

/**
 * The backend stores applicability as a CSV of job-function CODES (`applicableJobFunctionCodes`,
 * empty = everyone). The UI never shows that string: it is parsed into codes here and serialised
 * back on save. Order and duplicates are normalised; case is preserved as entered because codes
 * are tenant data.
 */
export function parseJobFunctionCodes(csv: string | null | undefined): string[] {
  if (!csv) return []
  const seen = new Set<string>()
  const codes: string[] = []
  for (const raw of csv.split(/[,;]/)) {
    const code = raw.trim()
    if (!code) continue
    const key = code.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    codes.push(code)
  }
  return codes
}

export function serializeJobFunctionCodes(codes: readonly string[]): string | null {
  const cleaned = parseJobFunctionCodes(codes.join(','))
  return cleaned.length === 0 ? null : cleaned.join(',')
}

// ---- Smart defaults ------------------------------------------------------------------------

export interface IssueDefaults {
  mode: ManagementMode
  requiresSerialNumber: boolean
  requiresReceivedDate: boolean
  returnRequired: boolean
}

export interface SiblingSuggestion extends IssueDefaults {
  /** How many existing items in the category the suggestion is based on. */
  basedOn: number
}

function majority(values: boolean[]): boolean {
  const yes = values.filter(Boolean).length
  return yes * 2 > values.length
}

/**
 * Category-aware defaults WITHOUT category names in code: the suggestion is what the tenant
 * already does for the other items in the same category (most common mode, majority of the
 * issue flags). A brand-new category has no siblings and therefore no suggestion.
 */
export function suggestDefaultsFromSiblings(
  items: readonly IssuedItemTemplate[] | undefined,
  categoryId: string | null,
): SiblingSuggestion | null {
  if (!items || !categoryId) return null
  const siblings = items.filter((item) => item.categoryId === categoryId && item.isActive)
  if (siblings.length === 0) return null

  const counts: Record<ManagementMode, number> = { individual: 0, stock: 0, variants: 0 }
  for (const sibling of siblings) counts[modeFromFlags(sibling)] += 1
  const mode = MANAGEMENT_MODES.reduce((best, candidate) => (counts[candidate] > counts[best] ? candidate : best), 'individual' as ManagementMode)

  return {
    mode,
    requiresSerialNumber: majority(siblings.map((s) => s.requiresSerialNumber)),
    requiresReceivedDate: majority(siblings.map((s) => s.requiresReceivedDate)),
    returnRequired: majority(siblings.map((s) => s.returnRequired)),
    basedOn: siblings.length,
  }
}

// ---- Variant rows (inline table) -----------------------------------------------------------

export interface VariantRow {
  /** Stable client key for React lists and focus management. */
  key: string
  label: string
  /** Raw input text; empty = 0 on save. */
  stock: string
  /** Raw input text; empty = the item's default minimum applies. */
  threshold: string
}

let rowSequence = 0

export function newVariantRow(label = ''): VariantRow {
  rowSequence += 1
  return { key: `row-${Date.now().toString(36)}-${rowSequence}`, label, stock: '', threshold: '' }
}

/** "S, M, L, XL" → rows; splits on comma/semicolon/newline, trims, skips blanks and duplicates. */
export function splitQuickVariants(input: string, existingLabels: readonly string[]): string[] {
  const taken = new Set(existingLabels.map((label) => label.trim().toLowerCase()))
  const labels: string[] = []
  for (const raw of input.split(/[,;\n]/)) {
    const label = raw.trim()
    if (!label) continue
    const key = label.toLowerCase()
    if (taken.has(key)) continue
    taken.add(key)
    labels.push(label)
  }
  return labels
}

/** Non-negative integer from a raw text input; empty → null, garbage → 0 (never negative). */
export function parseNonNegative(raw: string): number | null {
  if (raw.trim() === '') return null
  const value = Math.trunc(Number(raw))
  return Number.isFinite(value) ? Math.max(0, value) : 0
}

export interface VariantRowIssue {
  key: string
  message: 'issuedItems.form.variantNameRequired' | 'issuedItems.form.variantDuplicate'
}

/** Rows must have a label and labels must be unique (the backend rejects duplicates too). */
export function validateVariantRows(rows: readonly VariantRow[]): VariantRowIssue[] {
  const issues: VariantRowIssue[] = []
  const seen = new Set<string>()
  for (const row of rows) {
    const label = row.label.trim()
    if (!label) {
      issues.push({ key: row.key, message: 'issuedItems.form.variantNameRequired' })
      continue
    }
    const key = label.toLowerCase()
    if (seen.has(key)) {
      issues.push({ key: row.key, message: 'issuedItems.form.variantDuplicate' })
      continue
    }
    seen.add(key)
  }
  return issues
}

// ---- Variant presets (starting points for the pending rows) --------------------------------

/**
 * A preset is purely a convenience of this form: it decides which rows the pending list STARTS
 * with. The rows are ordinary rows (ordinary variants once saved) and can be edited, removed or
 * extended freely; the backend never learns that a preset was used.
 *
 *   clothing → S, M, L, XL prefilled (the common case: kleding, schoenen)
 *   custom   → empty list, the user adds every variant by hand
 */
export type VariantPreset = 'clothing' | 'custom'

export const VARIANT_PRESETS: readonly VariantPreset[] = ['clothing', 'custom']

export const DEFAULT_VARIANT_PRESET: VariantPreset = 'clothing'

export const VARIANT_PRESET_LABELS: Record<VariantPreset, string> = {
  clothing: 'issuedItems.form.presetClothing',
  custom: 'issuedItems.form.presetCustom',
}

export const VARIANT_PRESET_HINTS: Record<VariantPreset, string> = {
  clothing: 'issuedItems.form.presetClothingHint',
  custom: 'issuedItems.form.presetCustomHint',
}

/** Clothing sizes in wearing order; the one-click suggestions follow this order too. */
export const CLOTHING_SIZES: readonly string[] = ['XS', 'S', 'M', 'L', 'XL', 'XXL', '3XL']

/** The rows the clothing preset starts with. */
export const CLOTHING_PRESET_SIZES: readonly string[] = ['S', 'M', 'L', 'XL']

export function presetRows(preset: VariantPreset): VariantRow[] {
  return preset === 'clothing' ? CLOTHING_PRESET_SIZES.map((label) => newVariantRow(label)) : []
}

function clothingRank(label: string): number {
  return CLOTHING_SIZES.indexOf(label.trim().toUpperCase())
}

/** Canonical sizes that are not in the list yet (case-insensitive), e.g. XS and XXL next to S–XL. */
export function clothingSizeSuggestions(rows: readonly VariantRow[]): string[] {
  const taken = new Set(rows.map((row) => row.label.trim().toUpperCase()))
  return CLOTHING_SIZES.filter((size) => !taken.has(size))
}

/**
 * Inserts a canonical size at its wearing position relative to the other canonical sizes in the
 * list (XS lands before S, XXL after XL); rows with any other label keep their place. A label
 * that is not a canonical size is simply appended.
 */
export function insertClothingSize(rows: readonly VariantRow[], row: VariantRow): VariantRow[] {
  const rank = clothingRank(row.label)
  const index = rank < 0 ? -1 : rows.findIndex((other) => clothingRank(other.label) > rank)
  if (index < 0) return [...rows, row]
  return [...rows.slice(0, index), row, ...rows.slice(index)]
}
