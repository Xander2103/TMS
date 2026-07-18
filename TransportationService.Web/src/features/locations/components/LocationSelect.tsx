import { useEffect, useState } from 'react'
import { getLocationOptions } from '../api/locationsApi'
import type { LocationOption, LocationType } from '../types'

interface LocationSelectProps {
  id?: string
  value: string
  onChange: (locationId: string) => void
  type?: LocationType
  disabled?: boolean
  allowEmpty?: boolean
  placeholder?: string
}

/**
 * Reusable active-location dropdown, backed by GET /api/locations/options. Intended for reuse
 * by future order/stop/planning forms that need to reference a master location — pass `type`
 * to narrow to a single kind (e.g. only loading locations).
 */
export function LocationSelect({ id, value, onChange, type, disabled, allowEmpty = true, placeholder }: LocationSelectProps) {
  const [options, setOptions] = useState<LocationOption[]>([])

  useEffect(() => {
    let mounted = true
    getLocationOptions(type).then((data) => {
      if (mounted) setOptions(data)
    })
    return () => {
      mounted = false
    }
  }, [type])

  return (
    <select id={id} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled}>
      {allowEmpty && <option value="">{placeholder ?? '— Selecteer een locatie —'}</option>}
      {options.map((o) => (
        <option key={o.id} value={o.id}>
          {o.name} ({o.code})
        </option>
      ))}
    </select>
  )
}
