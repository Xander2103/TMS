import { useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getLocation } from '../api/locationsApi'
import type { PickedAddress } from '../addressFields'
import type { LocationOption } from '../types'
import { AddressAutocompleteInput } from './AddressAutocompleteInput'
import './LocationSelect.css'

interface LocationSelectProps {
  id?: string
  /** The stop's linked address id ('' = free address); only used to word the placeholder. */
  value: string
  /** The chosen address id. Fired on every explicit choice, right before `onSelectAddress`. */
  onChange: (locationId: string) => void
  /**
   * The chosen address RECORD — name, street + number, postal code, city, country — so the
   * caller can fill every address field from the real record instead of a label.
   */
  onSelectAddress?: (address: PickedAddress) => void
  /** Rank this customer's addresses first (and their recently used ones right after). */
  customerId?: string
  disabled?: boolean
  placeholder?: string
  /**
   * Inline-create hook: called with the typed name ('' from the permanent action), resolves with
   * the created location (to link it) or null when the user cancels. The caller owns the
   * creation UI (the customer-address quick-create dialog) AND the persistence: the address is
   * stored on the customer the moment that dialog saves; linking it to the stop only happens
   * when the route itself is saved.
   */
  onCreateNew?: (name: string) => Promise<LocationOption | null>
}

/** Quick-create may hand back an option with address fields (additive on the wire). */
type LocationOptionWithAddress = LocationOption & {
  address?: string | null
  postalCode?: string | null
  countryCode?: string | null
}

/**
 * The stop's address-SEARCH field (master sprint 2026-09-21, D3), on the one shared address
 * search (`AddressAutocompleteInput` → GET /api/addresses/picker, customer's own addresses
 * first). It is a search box, not a select: the text is a transient query that is cleared after
 * a choice, and what was chosen is shown — and stays editable — in the address fields below it.
 * Nothing is ever chosen on the planner's behalf; leaving the field alone keeps a free address.
 *
 * Route UX sprint 2026-10-08: when the host offers quick-create, "+ Nieuw klantadres" is ALWAYS
 * one click away — as a permanent action next to the field (whatever the customer already has
 * and whatever the search returns) and as the last row of the suggestion list in every state:
 * the empty-field suggestions, a result list and the "no results" note.
 */
export function LocationSelect({
  id,
  value,
  onChange,
  onSelectAddress,
  customerId,
  disabled,
  placeholder,
  onCreateNew,
}: LocationSelectProps) {
  const { t } = useLocale()
  const [query, setQuery] = useState('')

  function choose(address: PickedAddress) {
    setQuery('')
    onChange(address.locationId)
    onSelectAddress?.(address)
  }

  async function createNew(name: string) {
    if (!onCreateNew) return
    const created: LocationOptionWithAddress | null = await onCreateNew(name)
    if (!created) return
    // The option is a summary; the address fields are filled from the real record.
    const fallback: PickedAddress = {
      locationId: created.id,
      name: created.name,
      street: created.address ?? null,
      houseNumber: null,
      postalCode: created.postalCode ?? null,
      city: created.city,
      countryCode: created.countryCode ?? null,
    }
    try {
      const detail = await getLocation(created.id)
      choose({ ...detail, locationId: detail.id })
    } catch {
      choose(fallback)
    }
  }

  const createLabel = t('locations.select.createCustomerAddress')

  return (
    <div className="location-select">
      <AddressAutocompleteInput
        id={id}
        value={query}
        onChange={setQuery}
        onSelect={choose}
        customerId={customerId}
        disabled={disabled}
        suggestWhenEmpty
        placeholder={placeholder ?? (value ? t('stopEditor.search.placeholderLinked') : t('stopEditor.search.placeholder'))}
        footerAction={
          onCreateNew
            ? {
                // A typed name travels into the dialog; the empty-field row is the plain shortcut.
                label: (typed) => (typed ? t('locations.select.createWithName', { query: typed }) : createLabel),
                run: (typed) => void createNew(typed),
              }
            : undefined
        }
        footerWhenEmpty={Boolean(onCreateNew)}
      />
      {onCreateNew && (
        <button
          type="button"
          className="location-select-create"
          onClick={() => void createNew(query.trim())}
          disabled={disabled}
          title={t('locations.select.createCustomerAddressHint')}
        >
          + {createLabel}
        </button>
      )}
    </div>
  )
}
