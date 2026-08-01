/** nl-BE date+time rendering for task timestamps ("—" for null). */
export function formatTaskDateTime(value: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}
