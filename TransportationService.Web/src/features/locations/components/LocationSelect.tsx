import { useEffect, useState } from 'react'
import { getLocationOptions } from '../api/locationsApi'
import type { LocationOption, LocationType } from '../types'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'

interface LocationSelectProps {
  id?: string
  value: string
  onChange: (locationId: string) => void
  type?: LocationType
  /** Restrict to one customer's active locations (order entry: only the customer's sites). */
  customerId?: string
  disabled?: boolean
  allowEmpty?: boolean
  placeholder?: string
  /**
   * Inline-create hook: called with the typed name, resolves with the created location (to
   * auto-select it) or null when the user cancels. The caller owns the creation UI.
   */
  onCreateNew?: (name: string) => Promise<LocationOption | null>
}

/**
 * The options endpoint also returns an address line + postal code (Phase 7) so the picker can
 * render "Magazijn Antwerpen — Noorderlaan 10, 2030 Antwerpen". Typed locally: the shared
 * LocationOption type is owned by another change set; the extra fields are additive on the wire.
 */
type LocationOptionWithAddress = LocationOption & {
  address?: string | null
  postalCode?: string | null
}

function optionLabel(option: LocationOptionWithAddress): string {
  const base = `${option.name} (${option.code})`
  const addressLine = [option.address, [option.postalCode, option.city].filter(Boolean).join(' ')]
    .filter(Boolean)
    .join(', ')
  const withAddress = addressLine ? `${base} — ${addressLine}` : base
  const markers = [
    option.isDefaultLoadingLocation ? 'standaard laden' : null,
    option.isDefaultUnloadingLocation ? 'standaard lossen' : null,
  ].filter(Boolean)
  return markers.length > 0 ? `${withAddress} — ${markers.join(' + ')}` : withAddress
}

function toSelectOption(option: LocationOptionWithAddress): SearchableSelectOption {
  return {
    value: option.id,
    label: optionLabel(option),
    keywords: [option.code, option.city ?? '', option.postalCode ?? '', option.address ?? ''].join(' '),
  }
}

/**
 * Reusable active-location combobox, backed by GET /api/locations/options. Pass `type`
 * to narrow to a single kind and `customerId` to narrow to a customer's own locations
 * (their default loading/unloading sites are marked and sorted first).
 */
export function LocationSelect({
  id,
  value,
  onChange,
  type,
  customerId,
  disabled,
  allowEmpty = true,
  placeholder,
  onCreateNew,
}: LocationSelectProps) {
  const [options, setOptions] = useState<LocationOption[]>([])
  // Loading state is derived from a request key so no setState runs synchronously in the effect.
  const [loadedKey, setLoadedKey] = useState<string | null>(null)
  const requestKey = `${type ?? 'all'}|${customerId ?? 'all'}`
  const isLoading = loadedKey !== requestKey

  useEffect(() => {
    let mounted = true
    getLocationOptions(type, customerId).then((data) => {
      if (mounted) {
        setOptions(data)
        setLoadedKey(requestKey)
      }
    })
    return () => {
      mounted = false
    }
  }, [type, customerId, requestKey])

  return (
    <SearchableSelect
      id={id}
      value={value === '' ? null : value}
      onChange={(v) => onChange(v ?? '')}
      options={options.map(toSelectOption)}
      placeholder={placeholder ?? '— Selecteer een locatie —'}
      disabled={disabled}
      isLoading={isLoading}
      clearable={allowEmpty}
      emptyMessage="Geen locaties gevonden"
      onCreate={
        onCreateNew
          ? {
              label: (query) => (query ? `Nieuwe locatie “${query}” aanmaken…` : 'Nieuwe locatie aanmaken…'),
              create: async (query) => {
                const created = await onCreateNew(query)
                if (!created) return null
                setOptions((current) => [...current, created])
                return toSelectOption(created)
              },
            }
          : undefined
      }
    />
  )
}
