import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../api/customersApi'
import type { CustomerListItem } from '../types'
import '../../transport-orders/components/commercialChange.css'

interface CustomerSearchPickerProps {
  id: string
  /** The currently chosen customer; null = nothing chosen yet. */
  value: CustomerListItem | null
  onChange: (customer: CustomerListItem | null) => void
  /** Shown as "current" and not selectable (the customer we are moving away from). */
  currentCustomerId?: string | null
  disabled?: boolean
}

/**
 * Server-side customer search for the customer-change flows (sprint 6): a text box that
 * queries the customer list (active only) and a short result list to pick from. Unlike
 * SearchableSelect this never preloads the whole customer base.
 */
export function CustomerSearchPicker({ id, value, onChange, currentCustomerId, disabled }: CustomerSearchPickerProps) {
  const { t } = useLocale()
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<CustomerListItem[]>([])
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const term = query.trim()
    if (term.length < 2) return
    let cancelled = false
    const handle = window.setTimeout(() => {
      setSearching(true)
      searchCustomers({ search: term, isActive: true, page: 1, pageSize: 10 })
        .then((page) => {
          if (cancelled) return
          setResults(page.items)
          setError(null)
        })
        .catch(() => {
          if (!cancelled) setError(t('customers.picker.loadFailed'))
        })
        .finally(() => {
          if (!cancelled) setSearching(false)
        })
    }, 250)
    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [query, t])

  return (
    <div className="customer-picker">
      <input
        id={id}
        type="search"
        value={query}
        placeholder={t('customers.picker.placeholder')}
        onChange={(e) => setQuery(e.target.value)}
        disabled={disabled}
        autoComplete="off"
      />
      {value && (
        <p className="customer-picker-selected">
          <strong>{value.name}</strong> <span className="customer-form-muted">{value.customerNumber}</span>
          {!disabled && (
            <button type="button" className="link-button" onClick={() => onChange(null)}>
              ×
            </button>
          )}
        </p>
      )}
      {error && <p className="customer-form-muted">{error}</p>}
      {query.trim().length >= 2 && (
        <ul className="customer-picker-results" aria-label={t('customers.picker.placeholder')}>
          {searching && <li className="customer-form-muted">{t('customers.picker.searching')}</li>}
          {!searching && results.length === 0 && !error && <li className="customer-form-muted">{t('customers.picker.noResults')}</li>}
          {results.map((customer) => {
            const isCurrent = customer.id === currentCustomerId
            return (
              <li key={customer.id}>
                <button
                  type="button"
                  className={`customer-picker-row${value?.id === customer.id ? ' is-selected' : ''}`}
                  onClick={() => onChange(customer)}
                  disabled={disabled || isCurrent || customer.isBlocked}
                >
                  <span>
                    <strong>{customer.name}</strong>{' '}
                    <span className="customer-form-muted">
                      {customer.customerNumber}
                      {customer.city ? ` · ${customer.city}` : ''}
                    </span>
                  </span>
                  {isCurrent && <Badge tone="neutral">{t('customers.picker.current')}</Badge>}
                  {customer.isBlocked && <Badge tone="danger">{t('customers.picker.blocked')}</Badge>}
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {query.trim().length < 2 && !value && <p className="customer-form-muted">{t('customers.picker.typeToSearch')}</p>}
    </div>
  )
}
