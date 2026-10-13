import { describe, expect, it } from 'vitest'
import { getNavModules } from '../../components/layout/nav/navConfig'
import { APP_COMMANDS } from '../../config/commands'
import { SHORTCUTS } from '../../config/shortcuts'
import { MESSAGES, translate } from '../translations'

/**
 * Missing-key-detectie (i18n-wave §53/§55): elke LETTERLIJKE t('…')-aanroep in de
 * broncode moet in de NL-bundel bestaan — een typfout faalt hier, niet pas runtime.
 * Dynamische sleutels (template literals zoals t(`x.${status}`)) vallen buiten deze
 * scan; hun waardebereik wordt gedekt door de labelmap-conventie + componenttests.
 * Daarnaast: registry-gedreven sleutels (nav, commands, shortcuts) resolven allemaal.
 */

function resolveNl(key: string): boolean {
  // translate echo't de key zelf wanneer die ontbreekt. Pluralsleutels bestaan alleen
  // als `<key>_one`/`<key>_other` — de kale key telt dan ook als aanwezig.
  if (translate('nl', key) !== key) return true
  return translate('nl', `${key}_other`) !== `${key}_other`
}

// Vite's raw glob i.p.v. node:fs — de app-tsconfig heeft bewust geen node-types,
// en translations.ts gebruikt hetzelfde glob-mechanisme voor de bundels zelf.
const SOURCE_FILES = import.meta.glob('../../**/*.{ts,tsx}', {
  eager: true,
  query: '?raw',
  import: 'default',
}) as Record<string, string>

describe('missing translation keys', () => {
  it('every literal t(\'…\') call in src resolves in the NL bundle', () => {
    const pattern = /\bt\(\s*'([\w.-]+)'/g
    const missing: string[] = []

    for (const [file, content] of Object.entries(SOURCE_FILES)) {
      if (/\.test\.(ts|tsx)$/.test(file)) continue
      for (const match of content.matchAll(pattern)) {
        const key = match[1]
        // Alleen sleutels met een namespace-punt zijn vertaalsleutels.
        if (!key.includes('.')) continue
        if (!resolveNl(key)) {
          missing.push(`${file}: ${key}`)
        }
      }
    }

    expect(missing, `Ontbrekende NL-vertaalsleutels:\n${missing.join('\n')}`).toEqual([])
  })

  it('all nav labels resolve', () => {
    const labels: string[] = []
    for (const mod of getNavModules()) {
      labels.push(mod.label)
      for (const item of mod.items ?? []) labels.push(item.label)
      for (const sg of mod.subgroups ?? []) {
        labels.push(sg.label)
        for (const item of sg.items) labels.push(item.label)
      }
    }
    expect(labels.filter((label) => !resolveNl(label))).toEqual([])
  })

  it('all command and shortcut labels resolve', () => {
    expect(APP_COMMANDS.map((c) => c.label).filter((label) => !resolveNl(label))).toEqual([])
    expect(SHORTCUTS.map((s) => s.label).filter((label) => !resolveNl(label))).toEqual([])
  })

  it('placeholders are identical across locales for every key', () => {
    const placeholderSet = (value: string): string =>
      [...value.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',')

    const flatten = (tree: Record<string, unknown>, prefix = ''): Record<string, string> => {
      const out: Record<string, string> = {}
      for (const [key, value] of Object.entries(tree)) {
        const full = prefix ? `${prefix}.${key}` : key
        if (typeof value === 'string') out[full] = value
        else Object.assign(out, flatten(value as Record<string, unknown>, full))
      }
      return out
    }

    const nl = flatten(MESSAGES.nl)
    for (const locale of ['fr', 'en'] as const) {
      const other = flatten(MESSAGES[locale])
      const mismatches = Object.keys(nl)
        .filter((key) => key in other && placeholderSet(nl[key]) !== placeholderSet(other[key]))
        .map((key) => `${locale}: ${key}`)
      expect(mismatches).toEqual([])
    }
  })
})
