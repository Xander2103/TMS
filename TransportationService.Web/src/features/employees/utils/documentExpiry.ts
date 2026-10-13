export type ExpiryState = 'expired' | 'expiring' | 'ok'

/** Classifies an expiry date relative to today (within 30 days = expiring soon, past = expired). */
export function classifyExpiry(expiryDate: string | null, today = new Date()): ExpiryState {
  if (!expiryDate) return 'ok'
  const expiry = new Date(`${expiryDate}T00:00:00`)
  if (Number.isNaN(expiry.getTime())) return 'ok'
  const midnight = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const days = Math.floor((expiry.getTime() - midnight.getTime()) / (24 * 60 * 60 * 1000))
  if (days < 0) return 'expired'
  if (days <= 30) return 'expiring'
  return 'ok'
}
