import { describe, expect, it } from 'vitest'
import { filterCommands, matchesCommand, APP_COMMANDS } from '../commands'
import { matchShortcut, SHORTCUTS } from '../shortcuts'
import { translate } from '../../i18n/translations'

describe('matchShortcut', () => {
  it('matches mod+k directly', () => {
    const match = matchShortcut({ key: 'k', mod: true }, null)
    expect(match).not.toBeNull()
    expect(match).not.toBe('prefix')
    expect((match as { keys: string }).keys).toBe('mod+k')
  })

  it('treats g as a sequence prefix and completes g p', () => {
    expect(matchShortcut({ key: 'g', mod: false }, null)).toBe('prefix')
    const match = matchShortcut({ key: 'p', mod: false }, 'g')
    expect((match as { keys: string }).keys).toBe('g p')
  })

  it('rejects unknown sequences and plain keys', () => {
    expect(matchShortcut({ key: 'z', mod: false }, 'g')).toBeNull()
    expect(matchShortcut({ key: 'p', mod: false }, null)).toBeNull()
  })

  it('only matches within the permission-filtered registry it is given', () => {
    const withoutPlanning = SHORTCUTS.filter((s) => !s.permissions.includes('planning.view'))
    expect(matchShortcut({ key: 'p', mod: false }, 'g', withoutPlanning)).toBeNull()
  })
})

describe('command registry', () => {
  const allow = () => true
  const denyAll = () => false
  const nl = (key: string) => translate('nl', key)

  it('filters commands by permission', () => {
    expect(filterCommands('', allow, nl).length).toBeGreaterThan(0)
    expect(filterCommands('', denyAll, nl)).toHaveLength(0)
  })

  it('matches substrings and subsequences', () => {
    const planbord = APP_COMMANDS.find((c) => c.id === 'go-planning-center')!
    expect(matchesCommand(planbord, 'planbord', nl)).toBe(true)
    expect(matchesCommand(planbord, 'plnb', nl)).toBe(true)
    expect(matchesCommand(planbord, 'xyz123', nl)).toBe(false)
  })

  it('caps the visible command list', () => {
    expect(filterCommands('', allow, nl).length).toBeLessThanOrEqual(8)
  })
})
