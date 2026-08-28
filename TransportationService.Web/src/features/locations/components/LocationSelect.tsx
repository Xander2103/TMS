import { useEffect, useState } from 'react'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
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

function optionLabel(t: TranslateFn, option: LocationOptionWithAddress): string {
  const base = `${option.name} (${option.code})`
  const addressLine = [option.address, [option.postalCode, option.city].filter(Boolean).join(' ')]
    .filter(Boolean)
    .join(', ')
  const withAddress = addressLine ? `${base} — ${addressLine}` : base
  const markers = [
    option.isDefaultLoadingLocation ? t('locations.select.defaultLoading') : null,
    option.isDefaultUnloadingLocation ? t('locations.select.defaultUnloading') : null,
  ].filter(Boolean)
  const withMarkers = markers.length > 0 ? `${withAddress} — ${markers.join(' + ')}` : withAddress
  const provenance = provenanceSuffix(t, option)
  return provenance ? `${withMarkers} — ${provenance}` : withMarkers
}

/**
 * Central address master: the options endpoint offers every address of the tenant, sorted
 * customer-first. Addresses of this customer need no marker (they are on top); a company-wide
 * address and an address shared by other customers say so in plain words.
 */
function provenanceSuffix(t: TranslateFn, option: LocationOptionWithAddress): string | null {
  if (option.linkedCustomerCount === undefined) return null // older payload without provenance
  if (option.isLinkedToCustomer) return null
  if (option.linkedCustomerCount === 0) return t('locations.select.companyAddress')
  return option.linkedCustomerNames
    ? t('locations.select.sharedAddressWith', { names: option.linkedCustomerNames })
    : t('locations.select.sharedAddress')
}

function toSelectOption(t: TranslateFn, option: LocationOptionWithAddress): SearchableSelectOption {
  return {
    value: option.id,
    label: optionLabel(t, option),
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
  const { t } = useLocale()
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
      options={options.map((option) => toSelectOption(t, option))}
      placeholder={placeholder ?? t('locations.select.placeholder')}
      disabled={disabled}
      isLoading={isLoading}
      clearable={allowEmpty}
      emptyMessage={t('locations.select.empty')}
      onCreate={
        onCreateNew
          ? {
              label: (query) => (query ? t('locations.select.createWithName', { query }) : t('locations.select.create')),
              create: async (query) => {
                const created = await onCreateNew(query)
                if (!created) return null
                setOptions((current) => [...current, created])
                return toSelectOption(t, created)
              },
            }
          : undefined
      }
    />
  )
}
