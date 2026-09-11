import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { getLocation } from '../api/locationsApi'
import { pickAddresses, type AddressPickerGroup, type AddressPickerOption } from '../api/customerAddressesApi'
import type { LocationOption, LocationType } from '../types'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'

interface LocationSelectProps {
  id?: string
  value: string
  onChange: (locationId: string) => void
  /**
   * Narrow to one location type. The picker endpoint has no type filter, so this is applied
   * CLIENT-side on the returned page (a very specific type may therefore yield fewer rows than
   * `take`); leave it unset for stop entry.
   */
  type?: LocationType
  /** Rank this customer's addresses first (and their recently used ones right after). */
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

/** Quick-create may hand back an option with address fields (additive on the wire). */
type LocationOptionWithAddress = LocationOption & {
  address?: string | null
  postalCode?: string | null
}

const GROUP_LABEL_KEYS: Record<AddressPickerGroup, string> = {
  CustomerAddress: 'locations.select.group.customerAddress',
  Recent: 'locations.select.group.recent',
  All: 'locations.select.group.all',
}

const PICKER_TAKE = 20

interface AddressParts {
  street?: string | null
  houseNumber?: string | null
  postalCode?: string | null
  city?: string | null
}

/** "Noorderlaan 10, 2030 Antwerpen" — empty parts are skipped. */
function addressLine(parts: AddressParts): string {
  const streetPart = [parts.street, parts.houseNumber].filter(Boolean).join(' ')
  const cityPart = [parts.postalCode, parts.city].filter(Boolean).join(' ')
  return [streetPart, cityPart].filter(Boolean).join(', ')
}

/** Text committed to the input after a selection. */
function committedLabel(name: string, line: string): string {
  return line ? `${name} — ${line}` : name
}

function pickerOptionToSelectOption(t: TranslateFn, option: AddressPickerOption): SearchableSelectOption {
  const line = addressLine(option)
  const groupLabel = t(GROUP_LABEL_KEYS[option.group])
  return {
    value: option.locationId,
    label: committedLabel(option.name, line),
    title: option.code && option.code !== option.name ? `${option.name} (${option.code})` : option.name,
    subtitle: line || undefined,
    meta: option.customerNames ? `${groupLabel} · ${option.customerNames}` : groupLabel,
  }
}

function createdOptionToSelectOption(option: LocationOptionWithAddress): SearchableSelectOption {
  const line = addressLine({ street: option.address, postalCode: option.postalCode, city: option.city })
  return {
    value: option.id,
    label: committedLabel(option.name, line),
  }
}

/**
 * Reusable address combobox, backed by the prioritised picker GET /api/addresses/picker
 * (customer addresses → recently used → whole master; server-side search on name, code,
 * street, postal code and city). Searching is debounced and race-guarded by SearchableSelect;
 * an empty query shows the customer's own and recent addresses.
 *
 * The caller only passes an id, so the label of a pre-set `value` is resolved once through
 * GET /api/locations/{id} and cached; selections and created addresses go straight into that
 * cache so they never trigger a lookup.
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
  // id → committed label, for values that were selected, created or resolved via getLocation.
  const [knownLabels, setKnownLabels] = useState<Record<string, string>>({})
  // Labels of the last search results, so a selection can be cached without a lookup.
  const lastResultsRef = useRef<Map<string, string>>(new Map())

  const selectedLabel = value ? knownLabels[value] : undefined

  useEffect(() => {
    if (!value || selectedLabel !== undefined) return
    let cancelled = false
    getLocation(value).then(
      (detail) => {
        if (cancelled) return
        const label = committedLabel(detail.name, addressLine(detail))
        setKnownLabels((current) => (current[detail.id] === label ? current : { ...current, [detail.id]: label }))
      },
      () => {
        // Unresolvable id (deleted / no access): the input simply shows no label.
      },
    )
    return () => {
      cancelled = true
    }
  }, [value, selectedLabel])

  const search = useCallback(
    async (query: string, signal: AbortSignal): Promise<SearchableSelectOption[]> => {
      const results = await pickAddresses({ customerId, search: query, take: PICKER_TAKE, signal })
      const narrowed = type ? results.filter((r) => r.type === type) : results
      const options = narrowed.map((r) => pickerOptionToSelectOption(t, r))
      lastResultsRef.current = new Map(options.map((o) => [o.value, o.label]))
      return options
    },
    [customerId, type, t],
  )

  function remember(locationId: string, label: string) {
    setKnownLabels((current) => (current[locationId] === label ? current : { ...current, [locationId]: label }))
  }

  return (
    <SearchableSelect
      id={id}
      value={value === '' ? null : value}
      onChange={(v) => {
        if (v) {
          const label = lastResultsRef.current.get(v)
          if (label !== undefined) remember(v, label)
        }
        onChange(v ?? '')
      }}
      options={[]}
      selectedLabel={selectedLabel}
      onSearch={search}
      searchMinChars={0}
      placeholder={placeholder ?? t('locations.select.placeholder')}
      disabled={disabled}
      clearable={allowEmpty}
      emptyMessage={t('locations.select.empty')}
      onCreate={
        onCreateNew
          ? {
              label: (query) => (query ? t('locations.select.createWithName', { query }) : t('locations.select.create')),
              create: async (query) => {
                const created = await onCreateNew(query)
                if (!created) return null
                const option = createdOptionToSelectOption(created)
                remember(option.value, option.label)
                return option
              },
            }
          : undefined
      }
    />
  )
}
