import { useEffect, useState } from 'react'
import { getLocationOptions } from '../api/locationsApi'
import type { LocationOption, LocationType } from '../types'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'

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
 * Reusable active-location combobox, backed by GET /api/locations/options. Pass `type`
 * to narrow to a single kind (e.g. only loading locations). Type-to-search on name and code.
 */
export function LocationSelect({ id, value, onChange, type, disabled, allowEmpty = true, placeholder }: LocationSelectProps) {
  const [options, setOptions] = useState<LocationOption[]>([])
  // Loading state is derived from a request key so no setState runs synchronously in the effect.
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  const requestKey = type ?? 'all'
  const isLoading = loadedKey !== requestKey

  useEffect(() => {
    let mounted = true
    getLocationOptions(type).then((data) => {
      if (mounted) {
        setOptions(data)
        setLoadedKey(type ?? 'all')
      }
    })
    return () => {
      mounted = false
    }
  }, [type])

  return (
    <SearchableSelect
      id={id}
      value={value === '' ? null : value}
      onChange={(v) => onChange(v ?? '')}
      options={options.map((o) => ({ value: o.id, label: `${o.name} (${o.code})`, keywords: o.code }))}
      placeholder={placeholder ?? '— Selecteer een locatie —'}
      disabled={disabled}
      isLoading={isLoading}
      clearable={allowEmpty}
      emptyMessage="Geen locaties gevonden"
    />
  )
}
