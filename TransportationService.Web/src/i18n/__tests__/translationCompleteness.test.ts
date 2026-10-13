import { describe, expect, it } from 'vitest'
import { LOCALES, MESSAGES, type MessageTree } from '../translations'

function flattenKeys(tree: MessageTree, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === 'string' ? [path] : flattenKeys(value, path)
  })
}

function flattenEntries(tree: MessageTree, prefix = ''): Array<[string, string]> {
  return Object.entries(tree).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === 'string' ? ([[path, value]] as Array<[string, string]>) : flattenEntries(value, path)
  })
}

const DOMAINS = Object.keys(MESSAGES.nl)

describe('translation completeness', () => {
  it('covers the same domains in every locale', () => {
    for (const locale of LOCALES) {
      expect(Object.keys(MESSAGES[locale]).sort()).toEqual([...DOMAINS].sort())
    }
  })

  for (const domain of DOMAINS) {
    it(`fr and en define exactly the Dutch key set for "${domain}"`, () => {
      const nlKeys = flattenKeys(MESSAGES.nl[domain] as MessageTree).sort()
      expect(flattenKeys(MESSAGES.fr[domain] as MessageTree).sort()).toEqual(nlKeys)
      expect(flattenKeys(MESSAGES.en[domain] as MessageTree).sort()).toEqual(nlKeys)
    })
  }

  it('contains no empty strings in any locale', () => {
    for (const locale of LOCALES) {
      for (const domain of DOMAINS) {
        for (const [path, value] of flattenEntries(MESSAGES[locale][domain] as MessageTree)) {
          expect(value.trim(), `${locale}/${domain}: ${path}`).not.toHaveLength(0)
        }
      }
    }
  })
})
