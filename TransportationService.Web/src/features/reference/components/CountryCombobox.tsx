import { useEffect, useState } from 'react'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { getCountryOptions, type CountryOption } from '../api/countriesApi'

interface CountryComboboxProps {
  id?: string
  /** ISO 3166-1 alpha-2 code, or null when no country is selected. */
  value: string | null
  onChange: (code: string | null) => void
  disabled?: boolean
  placeholder?: string
  ariaLabel?: string
}

/**
 * The one country selector used everywhere (customers, employees, locations, stops,
 * settings, VAT country, qualification issuing country). Type-to-search on the Dutch
 * name, alpha-2 and alpha-3 codes; stores the alpha-2 code.
 */
export function CountryCombobox({ id, value, onChange, disabled, placeholder, ariaLabel }: CountryComboboxProps) {
  const [options, setOptions] = useState<CountryOption[]>([])
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    let mounted = true
    getCountryOptions().then((data) => {
      if (mounted) {
        setOptions(data)
        setLoaded(true)
      }
    })
    return () => {
      mounted = false
    }
  }, [])

  return (
    <SearchableSelect
      id={id}
      value={value}
      onChange={onChange}
      options={options.map((c) => ({
        value: c.code,
        label: c.name,
        description: c.code,
        keywords: `${c.code} ${c.alpha3}`,
      }))}
      placeholder={placeholder ?? '— Selecteer een land —'}
      disabled={disabled}
      isLoading={!loaded}
      emptyMessage="Geen landen gevonden"
      ariaLabel={ariaLabel}
    />
  )
}
