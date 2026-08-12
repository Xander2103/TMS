import { useEffect, useState } from 'react'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import {
  getOrderDocumentStrategy,
  setOrderDocumentPreference,
  type OrderDocumentPreference,
  type OrderDocumentStrategy,
} from '../api/transportDocumentsApi'

interface OrderDocumentStrategyPanelProps {
  orderId: string
}

const PREFERENCE_OPTIONS: { value: '' | OrderDocumentPreference; label: string }[] = [
  { value: '', label: 'Volgens klantinstelling' },
  { value: 'Own', label: 'Eigen document' },
  { value: 'CustomerDocument', label: 'Document van de klant' },
  { value: 'NoneRequired', label: 'Geen document nodig' },
]

/**
 * Documentstrategie naast de leveringsbon/CMR-knoppen (Wave 9): toont wélk document deze
 * opdracht nodig heeft en waarom (klantinstelling, tenantregel of orderkeuze). De
 * downloadknoppen blijven altijd beschikbaar — ook als de klant het document aanlevert —
 * maar de melding maakt duidelijk dat een eigen document dan niet nodig is.
 */
export function OrderDocumentStrategyPanel({ orderId }: OrderDocumentStrategyPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('orders.manage')

  const [strategy, setStrategy] = useState<OrderDocumentStrategy | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getOrderDocumentStrategy(orderId)
      .then((data) => {
        if (mounted) setStrategy(data)
      })
      .catch(() => {
        /* strategie is informatief; de downloadknoppen blijven gewoon werken */
      })
    return () => {
      mounted = false
    }
  }, [orderId])

  async function handlePreferenceChange(value: string) {
    setBusy(true)
    try {
      const updated = await setOrderDocumentPreference(orderId, value === '' ? null : (value as OrderDocumentPreference))
      setStrategy(updated)
      showSuccess('Documentkeuze bijgewerkt.')
    } catch (err) {
      showError(describeApiError(err, 'De documentkeuze kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  if (!strategy) return null

  const noOwnDocument = strategy.usesCustomerDocument || strategy.noneRequired

  return (
    <div className="to-doc-strategy">
      {noOwnDocument ? (
        <p className="to-doc-strategy-notice" role="note">
          Geen eigen document nodig: {strategy.reason}
        </p>
      ) : (
        <p className="customer-form-muted">{strategy.reason}</p>
      )}
      {canManage && (
        <label className="to-doc-strategy-choice">
          Documentkeuze voor deze opdracht
          <select
            aria-label="Documentkeuze voor deze opdracht"
            value={strategy.orderPreference ?? ''}
            onChange={(e) => void handlePreferenceChange(e.target.value)}
            disabled={busy}
          >
            {PREFERENCE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
      )}
    </div>
  )
}
