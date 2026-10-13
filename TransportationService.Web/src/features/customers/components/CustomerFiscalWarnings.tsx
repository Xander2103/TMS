import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { getCustomerFiscalWarnings, type CustomerFiscalWarning } from '../api/customersApi'
import '../../transport-orders/components/commercialChange.css'

interface CustomerFiscalWarningsProps {
  customerId: string
  /** Used in the translated message for the "foreign customer on domestic VAT" notice. */
  countryCode: string | null
  /** Re-fetch trigger: bump when the customer was saved. */
  refreshKey?: string | number
}

const KNOWN_CODES = new Set(['vat-number-missing', 'domestic-vat-foreign-customer', 'intra-community-same-country'])

/**
 * Sprint 5: advisory fiscal notices on the Fiscaal & Peppol card. Worded for the operator,
 * translated by code; an unknown code falls back to the backend text. Never changes anything.
 */
export function CustomerFiscalWarnings({ customerId, countryCode, refreshKey }: CustomerFiscalWarningsProps) {
  const { t } = useLocale()
  const [warnings, setWarnings] = useState<CustomerFiscalWarning[] | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let mounted = true
    getCustomerFiscalWarnings(customerId)
      .then((data) => {
        if (!mounted) return
        setFailed(false)
        setWarnings(data)
      })
      .catch(() => {
        if (mounted) setFailed(true)
      })
    return () => {
      mounted = false
    }
  }, [customerId, refreshKey])

  if (failed) return <p className="customer-form-muted">{t('customers.fiscalWarnings.loadFailed')}</p>
  if (warnings === null) return null

  return (
    <div className="customer-fiscal-warnings" data-testid="customer-fiscal-warnings">
      <h4>
        {t('customers.fiscalWarnings.title')}{' '}
        {warnings.length > 0 && <Badge tone="warning">{warnings.length}</Badge>}
      </h4>
      {warnings.length === 0 ? (
        <p className="customer-form-muted">{t('customers.fiscalWarnings.none')}</p>
      ) : (
        <>
          <p className="customer-form-muted">{t('customers.fiscalWarnings.intro')}</p>
          <ul>
            {warnings.map((warning) => (
              <li key={warning.code}>
                {KNOWN_CODES.has(warning.code)
                  ? t(`customers.fiscalWarnings.${warning.code}`, { country: countryCode ?? '—' })
                  : warning.message}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
