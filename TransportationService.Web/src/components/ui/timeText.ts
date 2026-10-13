/** Exactly 'HH:mm' with 00–23 hours and 00–59 minutes. */
export const COMPLETE_TIME = /^([01]\d|2[0-3]):[0-5]\d$/
/** Hours and minutes with a separator: "8:30", "8.30", "8,30", "8h30", "8u30", "8 30". */
const SEPARATED = /^(\d{1,2})\s*[:.,hu\s]\s*(\d{1,2})$/

export const pad2 = (n: number) => String(n).padStart(2, '0')

/**
 * Normalizes free text to a 24-hour 'HH:mm' string, or returns null when impossible.
 *
 * Rules: "8" / "08" → "08:00"; "830" / "0830" → "08:30"; "123" → "01:23" (3 digits = H:MM);
 * "8:5" / "8:3" → "08:05" / "08:03" (a single minute digit is that minute); separators
 * ":" "." "," "h" "u" and space are accepted ("8h30", "8u30", "8 30"). Hours must be 0–23,
 * minutes 0–59 ("24:00", "24", "08:60" → null). Never produces AM/PM.
 */
export function normalizeTimeText(raw: string): string | null {
  const text = raw.trim().toLowerCase()
  if (!text) return null

  let hours: string
  let minutes: string
  const separated = SEPARATED.exec(text)
  if (separated) {
    hours = separated[1]
    minutes = separated[2]
  } else if (/^\d{1,4}$/.test(text)) {
    if (text.length <= 2) {
      hours = text
      minutes = '0'
    } else if (text.length === 3) {
      hours = text.slice(0, 1)
      minutes = text.slice(1)
    } else {
      hours = text.slice(0, 2)
      minutes = text.slice(2)
    }
  } else {
    return null
  }

  const h = Number(hours)
  const m = Number(minutes)
  if (!Number.isInteger(h) || !Number.isInteger(m) || h > 23 || m > 59) return null
  return `${pad2(h)}:${pad2(m)}`
}

