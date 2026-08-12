import { formatDateTime } from '../../../utils/dates'

/** Tenant-format date+time rendering for task timestamps ("—" for null). */
export function formatTaskDateTime(value: string | null): string {
  if (!value) return '—'
  return formatDateTime(value)
}
