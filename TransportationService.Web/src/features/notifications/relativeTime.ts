import { formatDate } from '../../utils/dates'

/** Compacte relatieve tijd in het Nederlands, bv. "5 min geleden". Ouder dan een week wordt een datum. */
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso)
  const diffMinutes = Math.floor((now.getTime() - then.getTime()) / 60_000)
  if (diffMinutes < 1) return 'zojuist'
  if (diffMinutes < 60) return `${diffMinutes} min geleden`
  const hours = Math.floor(diffMinutes / 60)
  if (hours < 24) return `${hours} uur geleden`
  const days = Math.floor(hours / 24)
  if (days === 1) return 'gisteren'
  if (days < 7) return `${days} dagen geleden`
  return formatDate(iso)
}
