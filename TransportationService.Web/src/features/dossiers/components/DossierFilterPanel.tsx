import { useId, type ReactNode } from 'react'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useLocale } from '../../../i18n/localeContext'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import {
  DOSSIER_RANGES,
  DOSSIER_STATUSES,
  monthRangeOf,
  todayRange,
  weekRangeOf,
  type DossierFilterKey,
  type DossierRangeId,
  type DossierSearchFilters,
} from '../pages/dossierSearchParams'
import { DOSSIER_STATUS_LABELS } from '../types'
import type { DossierFilterLookups } from './dossierFilterLookups'

interface DossierFilterPanelProps {
  id: string
  filters: DossierSearchFilters
  lookups: DossierFilterLookups
  /** The signed-in user; "Alleen mijn dossiers" needs it. */
  currentUserId?: string | null
  /** Patches the URL-backed filter state; `replace` for keystrokes so Back skips them. */
  onChange: (patch: Partial<DossierSearchFilters>, options?: { replace?: boolean }) => void
  /** SEC-M1: false hides the invoice-status filter, which the API refuses without a price right. */
  canViewPrices?: boolean
}

/**
 * The extended filter panel of the dossier list: grouped, compact, and purely a view on the
 * URL-backed filter state — it holds no filter state of its own, so closing it never drops a
 * filter. Every field maps 1:1 to a parameter of GET /api/dossiers/search.
 */
export function DossierFilterPanel({ id, filters, lookups, currentUserId = null, onChange, canViewPrices = true }: DossierFilterPanelProps) {
  const { t } = useLocale()
  const idPrefix = useId()
  const fieldId = (key: DossierFilterKey) => `${idPrefix}-${key}`
  const label = (key: DossierFilterKey) => t(`dossiers.filters.fields.${key}`)

  const textField = (key: DossierFilterKey) => (
    <FormField key={key} label={label(key)} htmlFor={fieldId(key)}>
      <input
        id={fieldId(key)}
        type="text"
        value={filters[key]}
        onChange={(e) => onChange({ [key]: e.target.value }, { replace: true })}
        autoComplete="off"
      />
    </FormField>
  )

  const selectField = (key: DossierFilterKey, options: { value: string; label: string }[]) => (
    <FormField key={key} label={label(key)} htmlFor={fieldId(key)}>
      <select id={fieldId(key)} value={filters[key]} onChange={(e) => onChange({ [key]: e.target.value })}>
        <option value="">{t('dossiers.filters.any')}</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FormField>
  )

  const lookupField = (key: DossierFilterKey, options: SearchableSelectOption[]) => (
    <FormField key={key} label={label(key)} htmlFor={fieldId(key)}>
      <SearchableSelect
        id={fieldId(key)}
        value={filters[key] || null}
        onChange={(value) => onChange({ [key]: value ?? '' })}
        options={options}
        placeholder={t('dossiers.filters.any')}
      />
    </FormField>
  )

  /** From/to pair kept on one line; each bound limits the other so an inverted range cannot be picked. */
  const rangeField = (rangeId: DossierRangeId) => {
    const range = DOSSIER_RANGES.find((r) => r.id === rangeId)!
    return (
      <div key={rangeId} className="dossier-filter-range">
        <FormField label={label(range.from)} htmlFor={fieldId(range.from)}>
          <input
            id={fieldId(range.from)}
            type="date"
            value={filters[range.from]}
            max={filters[range.to] || undefined}
            onChange={(e) => onChange({ [range.from]: e.target.value }, { replace: true })}
          />
        </FormField>
        <FormField label={label(range.to)} htmlFor={fieldId(range.to)}>
          <input
            id={fieldId(range.to)}
            type="date"
            value={filters[range.to]}
            min={filters[range.from] || undefined}
            onChange={(e) => onChange({ [range.to]: e.target.value }, { replace: true })}
          />
        </FormField>
      </div>
    )
  }

  const quickRanges = [
    { key: 'today', range: todayRange() },
    { key: 'thisWeek', range: weekRangeOf() },
    { key: 'thisMonth', range: monthRangeOf() },
  ]

  const group = (name: string, children: ReactNode) => (
    <fieldset className="dossier-advanced-group">
      <legend>{t(`dossiers.filters.groups.${name}`)}</legend>
      <div className="dossier-advanced-fields">{children}</div>
    </fieldset>
  )

  return (
    <section id={id} className="dossier-advanced" aria-label={t('dossiers.filters.panelLabel')}>
      {group('identification', [textField('dossierNumber'), textField('orderNumber'), textField('customerReference')])}
      {group('customer', [lookupField('customerId', lookups.customers), textField('customerNumber')])}
      {group('lifecycle', [
        selectField(
          'status',
          DOSSIER_STATUSES.map((status) => ({ value: status, label: t(DOSSIER_STATUS_LABELS[status]) })),
        ),
        selectField('confirmationSource', [
          { value: 'Manual', label: t('dossiers.filters.options.sourceManual') },
          { value: 'Automatic', label: t('dossiers.filters.options.sourceAutomatic') },
        ]),
        rangeField('confirmed'),
        selectField('hasOpenIncident', [
          { value: 'true', label: t('dossiers.filters.options.incidentOpen') },
          { value: 'false', label: t('dossiers.filters.options.incidentNone') },
        ]),
        currentUserId ? (
          <label key="mine" className="dossier-filter-mine">
            <input
              type="checkbox"
              checked={filters.responsibleUserId === currentUserId}
              onChange={(e) => onChange({ responsibleUserId: e.target.checked ? currentUserId : '' })}
            />
            {t('dossiers.filters.onlyMine')}
          </label>
        ) : null,
      ])}
      {group('date', [
        rangeField('date'),
        <div key="quick" className="dossier-filter-quick">
          {quickRanges.map(({ key, range }) => (
            <button
              key={key}
              type="button"
              className="dossier-filter-chip"
              aria-pressed={filters.dateFrom === range.dateFrom && filters.dateTo === range.dateTo}
              onClick={() => onChange(range)}
            >
              {t(`dossiers.filters.${key}`)}
            </button>
          ))}
        </div>,
        rangeField('planning'),
        rangeField('created'),
      ])}
      {group('transport', [
        lookupField('transportTypeId', lookups.transportTypes),
        lookupField('activityTypeId', lookups.activityTypes),
      ])}
      {group('planning', [
        selectField('planning', [
          { value: 'planned', label: t('dossiers.filters.options.planned') },
          { value: 'unplanned', label: t('dossiers.filters.options.unplanned') },
        ]),
        lookupField('driverId', lookups.drivers),
        lookupField('vehicleId', lookups.vehicles),
        textField('licensePlate'),
        lookupField('trailerId', lookups.trailers),
      ])}
      {group('location', [
        textField('loadingCity'),
        textField('unloadingCity'),
        textField('postalCode'),
        <FormField key="countryCode" label={label('countryCode')} htmlFor={fieldId('countryCode')}>
          <CountryCombobox
            id={fieldId('countryCode')}
            value={filters.countryCode ? filters.countryCode.toUpperCase() : null}
            onChange={(code) => onChange({ countryCode: code ?? '' })}
            placeholder={t('dossiers.filters.any')}
          />
        </FormField>,
      ])}
      {group('commercial', [
        selectField('priceStatus', [
          { value: 'Priced', label: t('dossiers.filters.options.priced') },
          { value: 'Partial', label: t('dossiers.filters.options.partial') },
          { value: 'Unpriced', label: t('dossiers.filters.options.unpriced') },
        ]),
        ...(canViewPrices
          ? [
              selectField('invoiceStatus', [
                { value: 'ReadyToInvoice', label: t('dossiers.filters.options.readyToInvoice') },
                { value: 'NotInvoiced', label: t('dossiers.filters.options.notInvoiced') },
                { value: 'Draft', label: t('dossiers.filters.options.invoiceDraft') },
                { value: 'Sent', label: t('dossiers.filters.options.invoiceSent') },
                { value: 'Paid', label: t('dossiers.filters.options.invoicePaid') },
              ]),
            ]
          : []),
      ])}
      {group('documents', [
        selectField('hasCmr', [
          { value: 'true', label: t('dossiers.filters.options.cmrYes') },
          { value: 'false', label: t('dossiers.filters.options.cmrNo') },
        ]),
      ])}
    </section>
  )
}
