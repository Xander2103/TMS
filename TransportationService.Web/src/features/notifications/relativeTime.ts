import { getActiveLocale } from '../../i18n/activeLocale'
import { translate } from '../../i18n/translations'
import { formatDate } from '../../utils/dates'

/**
 * Compacte relatieve tijd in de actieve taal, bv. "5 min geleden" / "il y a 5 min".
 * Ouder dan een week wordt een datum. Leest de module-level actieve locale (zelfde
 * patroon als utils/dates.ts), zodat niet-React-callers geen hook nodig hebben.
 */
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const locale = getActiveLocale()
  const then = new Date(iso)
  const diffMinutes = Math.floor((now.getTime() - then.getTime()) / 60_000)
  if (diffMinutes < 1) return translate(locale, 'notificationCenter.relative.justNow')
  if (diffMinutes < 60) return translate(locale, 'notificationCenter.relative.minutesAgo', { count: diffMinutes })
  const hours = Math.floor(diffMinutes / 60)
  if (hours < 24) return translate(locale, 'notificationCenter.relative.hoursAgo', { count: hours })
  const days = Math.floor(hours / 24)
  if (days === 1) return translate(locale, 'notificationCenter.relative.yesterday')
  if (days < 7) return translate(locale, 'notificationCenter.relative.daysAgo', { count: days })
  return formatDate(iso)
}
