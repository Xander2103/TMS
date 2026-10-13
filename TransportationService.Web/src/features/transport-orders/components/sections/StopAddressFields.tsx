import { useEffect, useState } from 'react'
import { Badge } from '../../../../components/ui/Badge'
import { FormField } from '../../../../components/ui/FormField'
import { useLocale } from '../../../../i18n/localeContext'
import { fullAddressLine, splitStreetLine, type PickedAddress } from '../../../locations/addressFields'
import { checkAddressDuplicates, type AddressDuplicateCandidate } from '../../../locations/api/customerAddressesApi'
import { getLocation } from '../../../locations/api/locationsApi'
import { AddressAutocompleteInput } from '../../../locations/components/AddressAutocompleteInput'
import { LocationSelect } from '../../../locations/components/LocationSelect'
import type { LocationOption } from '../../../locations/types'
import { CountryCombobox } from '../../../reference/components/CountryCombobox'
import { stopPatchFromAddress, type StopFormRow } from './orderFormState'

interface StopAddressFieldsProps {
  stop: StopFormRow
  index: number
  customerId: string
  saving: boolean
  errors: Record<string, string>
  setStop: (key: string, patch: Partial<StopFormRow>) => void
  /** Opens the host's "Adres opnieuw overnemen" confirmation (persisted stops). */
  onRequestRefresh: (key: string) => void
  onQuickCreate?: (name: string) => Promise<LocationOption | null>
  /** locations.create AND a known customer: the "also store in the address book" option is offered. */
  canSaveToAddressBook: boolean
}

/**
 * Address block of one stop (master sprint 2026-09-21, D3). The address fields are ALWAYS shown
 * — search, location name, street + number, postal code, place, country — with or without a
 * linked address-book record, because the stop snapshot IS this dossier's own copy:
 *
 *  - choosing an existing address (search field or a street suggestion) fills EVERY field from
 *    the real record; the name field gets the location's name, never the whole address line;
 *  - editing a field of a linked stop keeps the link but marks the stop `addressOverridden`
 *    ("Afwijkend van adresboek — alleen voor dit dossier"); "Adres opnieuw overnemen" resets it;
 *  - a new address (free, or a deviating linked one) may ALSO be stored in the customer's address
 *    book — a flag in the save payload only: nothing is created before the dossier is saved.
 */
export function StopAddressFields({
  stop,
  index,
  customerId,
  saving,
  errors,
  setStop,
  onRequestRefresh,
  onQuickCreate,
  canSaveToAddressBook,
}: StopAddressFieldsProps) {
  const { t } = useLocale()
  const linked = stop.locationId !== ''
  const overridden = linked && stop.addressOverridden
  const hasAddress = Boolean(stop.address.trim() || stop.city.trim() || stop.postalCode.trim())
  const offerSave = canSaveToAddressBook && hasAddress && (!linked || overridden)
  const cityError = errors[`stops[${index}].city`]
  const postalError = errors[`stops[${index}].postalCode`]
  // A server-side refusal to store THIS stop's address (permission, validation) is shown here.
  const saveError = errors[`stops[${index}].saveToAddressBook`]

  /** An edit of a linked stop is a deliberate deviation — for this dossier only. */
  const edit = (patch: Partial<StopFormRow>) => setStop(stop.key, linked ? { ...patch, addressOverridden: true } : patch)
  const fill = (address: PickedAddress) => setStop(stop.key, { ...stopPatchFromAddress(address), refreshSnapshot: false })

  // "Adres opnieuw overnemen": the server re-copies the record on save (refreshSnapshot); the
  // fields on screen are refilled from the same record right away, so what the planner sees is
  // what will be stored. A failed lookup only means the fields refresh after the save.
  const refreshing = linked && stop.refreshSnapshot
  useEffect(() => {
    if (!refreshing) return
    let cancelled = false
    getLocation(stop.locationId).then(
      (detail) => {
        if (cancelled || typeof detail?.name !== 'string') return
        // An unsaved stop has nothing to re-copy on the server: the refill IS the reset.
        setStop(stop.key, {
          ...stopPatchFromAddress({ ...detail, locationId: detail.id }),
          ...(stop.id ? {} : { refreshSnapshot: false }),
        })
      },
      () => {},
    )
    return () => {
      cancelled = true
    }
    // Keyed on the pending refresh of this stop only — not on every edit of the row.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshing, stop.locationId, stop.key])

  /** Reset a deviation. A persisted stop goes through the host's confirmation (audited re-copy). */
  function resetToAddressBook() {
    if (stop.id) onRequestRefresh(stop.key)
    else setStop(stop.key, { refreshSnapshot: true, addressOverridden: false })
  }

  // Detach from the address book: the fields stay exactly as shown and become a free address.
  const unlinkButton = (
    <button
      type="button"
      className="tof-link"
      onClick={() =>
        setStop(stop.key, { locationId: '', addressOverridden: false, refreshSnapshot: false, snapshotName: '', snapshotAddress: '' })
      }
      disabled={saving}
    >
      {t('stopEditor.address.unlink')}
    </button>
  )

  return (
    <div className="tof-address">
      <div className="tof-row tof-row-single">
        <FormField label={t('stopEditor.address.search')} htmlFor={`st-loc-${stop.key}`}>
          <LocationSelect
            id={`st-loc-${stop.key}`}
            value={stop.locationId}
            onChange={(locationId) =>
              // A different address invalidates the shown snapshot and any pending deviation; the
              // record itself arrives through `onSelectAddress` and fills the fields below.
              setStop(stop.key, {
                locationId, refreshSnapshot: false, snapshotName: '', snapshotAddress: '',
                addressOverridden: false, saveToAddressBook: false,
              })
            }
            onSelectAddress={fill}
            customerId={customerId || undefined}
            disabled={saving}
            onCreateNew={onQuickCreate}
          />
        </FormField>
      </div>
      {linked && !overridden && (
        <div className="tof-snapshot-row">
          <Badge tone="info">{t('transportOrders.route.snapshotBadge')}</Badge>
          {stop.snapshotName && (
            <span className="tof-snapshot-line">
              {stop.snapshotName}
              {stop.snapshotAddress ? ` — ${stop.snapshotAddress}` : ''}
            </span>
          )}
          {stop.refreshSnapshot && <Badge tone="warning">{t('transportOrders.route.snapshotRefreshBadge')}</Badge>}
          {unlinkButton}
        </div>
      )}
      {overridden && (
        <div className="tof-snapshot-row" role="note">
          <Badge tone="warning">{t('stopEditor.address.overridden')}</Badge>
          <button type="button" className="tof-link" onClick={resetToAddressBook} disabled={saving}>
            {t('transportOrders.route.refreshFromLocation')}
          </button>
          {unlinkButton}
        </div>
      )}
      <div className="tof-row">
        <FormField label={t('stopEditor.address.name')} htmlFor={`st-name-${stop.key}`}>
          <input
            id={`st-name-${stop.key}`}
            value={stop.locationName}
            onChange={(e) => edit({ locationName: e.target.value })}
            disabled={saving}
            maxLength={200}
          />
        </FormField>
        <FormField label={t('stopEditor.address.street')} htmlFor={`st-addr-${stop.key}`}>
          <AddressAutocompleteInput
            id={`st-addr-${stop.key}`}
            value={stop.address}
            onChange={(text) => edit({ address: text })}
            onSelect={fill}
            customerId={customerId || undefined}
            disabled={saving}
            maxLength={300}
          />
        </FormField>
      </div>
      <div className="tof-row">
        <FormField label={t('transportOrders.route.postalCode')} htmlFor={`st-pc-${stop.key}`} error={postalError}>
          <input
            id={`st-pc-${stop.key}`}
            value={stop.postalCode}
            onChange={(e) => edit({ postalCode: e.target.value, postalCodeTouched: true })}
            disabled={saving}
            maxLength={20}
            aria-invalid={postalError ? true : undefined}
          />
        </FormField>
        <FormField label={t('transportOrders.route.city')} htmlFor={`st-city-${stop.key}`} required={!linked} error={cityError}>
          <input
            id={`st-city-${stop.key}`}
            value={stop.city}
            onChange={(e) => edit({ city: e.target.value })}
            disabled={saving}
            maxLength={100}
            aria-invalid={cityError ? true : undefined}
          />
        </FormField>
      </div>
      <div className="tof-row">
        <FormField label={t('transportOrders.route.country')} htmlFor={`st-cc-${stop.key}`}>
          <CountryCombobox
            id={`st-cc-${stop.key}`}
            value={stop.countryCode || null}
            onChange={(code) => edit({ countryCode: code ?? '' })}
            disabled={saving}
          />
        </FormField>
      </div>
      {offerSave && (
        <div className="tof-address-save">
          <label className="tof-address-save-check">
            <input
              type="checkbox"
              checked={stop.saveToAddressBook}
              onChange={(e) => setStop(stop.key, { saveToAddressBook: e.target.checked })}
              disabled={saving}
              aria-describedby={`st-save-help-${stop.key}`}
            />
            {t('stopEditor.address.save')}
          </label>
          <p id={`st-save-help-${stop.key}`} className="tof-address-save-help">
            {t('stopEditor.address.saveHelp')}
          </p>
          {saveError && (
            <p className="ui-form-field-error" role="alert">
              {saveError}
            </p>
          )}
          {stop.saveToAddressBook && (
            <StopAddressDuplicates stop={stop} saving={saving} onUse={fill} />
          )}
        </div>
      )}
    </div>
  )
}

/**
 * Possible existing addresses for an address about to be stored (POST /api/addresses/duplicate-check,
 * the same check the quick-create dialog runs). Purely advisory: the server links an exact
 * duplicate instead of creating a second record anyway; this only lets the planner pick it now.
 */
function StopAddressDuplicates({
  stop,
  saving,
  onUse,
}: {
  stop: StopFormRow
  saving: boolean
  onUse: (address: PickedAddress) => void
}) {
  const { t } = useLocale()
  const [found, setFound] = useState<{ key: string; candidates: AddressDuplicateCandidate[] } | null>(null)
  const { address, postalCode, city, countryCode, locationId } = stop
  const checkKey = [address.trim(), postalCode.trim(), city.trim(), countryCode].join('|')
  const checkable = Boolean(address.trim() && (city.trim() || postalCode.trim()))

  useEffect(() => {
    if (!checkable) return
    let cancelled = false
    const timer = setTimeout(() => {
      checkAddressDuplicates({
        ...splitStreetLine(address),
        postalCode: postalCode.trim() || null,
        city: city.trim() || null,
        countryCode: countryCode || null,
        excludeLocationId: locationId || null,
      }).then(
        (result) => {
          if (!cancelled) setFound({ key: checkKey, candidates: result.candidates })
        },
        () => {
          // Advisory only — the server still de-duplicates on save.
        },
      )
    }, 400)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [checkable, checkKey, address, postalCode, city, countryCode, locationId])

  // Candidates of an OLDER version of the address are never shown under the current one.
  const candidates = checkable && found?.key === checkKey ? found.candidates : []
  if (candidates.length === 0) return null
  return (
    <div className="tof-address-duplicates" role="note">
      <p>{t('stopEditor.address.duplicatesTitle')}</p>
      <ul>
        {candidates.slice(0, 3).map((candidate) => (
          <li key={candidate.locationId}>
            <span>
              <strong>{candidate.name}</strong>
              {fullAddressLine(candidate) ? ` — ${fullAddressLine(candidate)}` : ''}
              {candidate.linkedCustomers.length > 0 ? ` · ${candidate.linkedCustomers.join(', ')}` : ''}
            </span>
            <button type="button" className="tof-link" onClick={() => onUse(candidate)} disabled={saving}>
              {t('stopEditor.address.duplicatesUse')}
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
