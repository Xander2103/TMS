import type { TranslateFn } from '../../i18n/localeContext'
import { fromWireDateTime } from '../../utils/dates'
import type { TransportOrderStop } from '../transport-orders/types'

/** Shared read-only stop rendering for the route summary and the Overzicht card. */
export function stopLine(t: TranslateFn, stop: TransportOrderStop): string {
  return stop.locationName || [stop.address, stop.city].filter(Boolean).join(', ') || stop.city || t('dossiers.route.tbd')
}

/**
 * "12-08 · 08:00–10:00" — compact day + window. C-03: the window is a UTC instant on the wire,
 * so it is projected onto the tenant zone with the same helper the order detail table uses;
 * both surfaces must show the identical hour for the identical stop.
 */
export function stopTiming(t: TranslateFn, stop: TransportOrderStop): string | null {
  const from = fromWireDateTime(stop.plannedFrom)
  const to = fromWireDateTime(stop.plannedTo)
  const day = from ?? to
  if (!day) return null
  const [, month, dayOfMonth] = day.date.split('-')
  const time = from && from.time !== '00:00'
    ? (to ? `${from.time}–${to.time}` : from.time)
    : to ? t('dossiers.route.before', { time: to.time }) : null
  return `${dayOfMonth}-${month}${time ? ` · ${time}` : ''}`
}
