import { describe, expect, it } from 'vitest'
import { LOCALES, MESSAGES, translate } from '../../../i18n/translations'
import { LOOKUP_RESOURCES } from '../lookupRegistry'

// "New <lookup>" is an explicit translation per lookup and language: an adjective glued onto an
// arbitrary noun guesses at grammar (nl "Nieuwe contracttype", fr "Nouveau une fonction").

type Tree = { [key: string]: string | Tree }
const masterData = (locale: (typeof LOCALES)[number]) => MESSAGES[locale].masterData as Tree

describe('masterData.newLabel', () => {
  it.each(LOCALES.map((l) => [l]))('%s: every registry lookup has an explicit label', (locale) => {
    const labels = masterData(locale).newLabel as Tree
    for (const resource of LOOKUP_RESOURCES) {
      expect(labels[resource.slug], `${locale} masterData.newLabel.${resource.slug}`).toEqual(expect.any(String))
    }
    // …and nothing the registry does not know (a typo would silently never be shown).
    expect(Object.keys(labels).sort()).toEqual(LOOKUP_RESOURCES.map((r) => r.slug).sort())
  })

  it('nl: het-words take "Nieuw", de-words "Nieuwe"', () => {
    expect(translate('nl', 'masterData.newLabel.contract-types')).toBe('Nieuw contracttype')
    expect(translate('nl', 'masterData.newLabel.departments')).toBe('Nieuwe afdeling')
    expect(translate('nl', 'masterData.newLabel.job-functions')).toBe('Nieuwe functie')
    expect(translate('nl', 'masterData.newLabel.languages')).toBe('Nieuwe taal')
  })

  it('fr: the adjective agrees and carries no article', () => {
    expect(translate('fr', 'masterData.newLabel.departments')).toBe('Nouveau département')
    expect(translate('fr', 'masterData.newLabel.job-functions')).toBe('Nouvelle fonction')
    for (const resource of LOOKUP_RESOURCES) {
      expect(translate('fr', `masterData.newLabel.${resource.slug}`)).not.toMatch(/\b(un|une)\b/)
    }
  })

  it.each(LOCALES.map((l) => [l]))('%s: no template glues "new" onto {singular} any more', (locale) => {
    const offenders: string[] = []
    const walk = (tree: Tree, path: string) => {
      for (const [key, value] of Object.entries(tree)) {
        if (typeof value !== 'string') walk(value, `${path}.${key}`)
        else if (/\b(Nieuwe?|Nouveau|Nouvelle|Nouvel)\s+\{singular\}/.test(value)) offenders.push(`${path}.${key}: ${value}`)
      }
    }
    walk(masterData(locale), 'masterData')
    expect(offenders).toEqual([])
  })

  it('the typed-query row is built on the explicit label in every language', () => {
    const row = (locale: (typeof LOCALES)[number]) =>
      translate(locale, 'masterData.select.createOption', {
        newLabel: translate(locale, 'masterData.newLabel.contract-types'),
        query: 'Interim',
      })
    expect(row('nl')).toBe('Nieuw contracttype "Interim" toevoegen')
    expect(row('fr')).toBe('Nouveau type de contrat « Interim »')
    expect(row('en')).toBe('New contract type "Interim"')
  })
})
