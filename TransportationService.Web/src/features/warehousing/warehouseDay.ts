/** `YYYY-MM-DD` for today + `offsetDays` on the device's own calendar (not UTC). */
export function localIsoDate(offsetDays = 0, from: Date = new Date()): string {
  const date = new Date(from.getFullYear(), from.getMonth(), from.getDate() + offsetDays)
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}
