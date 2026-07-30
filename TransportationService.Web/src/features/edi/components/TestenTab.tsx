import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  listPartners,
  simulate,
  validatePayload,
  SAMPLE_PAYLOAD,
  type EdiPartner,
  type EdiValidationResult,
} from '../api/ediApi'
import { MessageDetailModal } from './MessageDetailModal'

/** "Testen" tab: dry-run validation against the live mapping (no side effects) plus the
 * development simulator that actually ingests a sample order. */
export function TestenTab() {
  const { showSuccess, showError } = useToast()
  const [partners, setPartners] = useState<EdiPartner[]>([])
  const [partnerCode, setPartnerCode] = useState('')
  const [messageType] = useState('order')
  const [payload, setPayload] = useState(SAMPLE_PAYLOAD)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<EdiValidationResult | null>(null)
  const [createdMessageId, setCreatedMessageId] = useState<string | null>(null)

  useEffect(() => {
    listPartners().then((data) => {
      setPartners(data)
      if (!partnerCode && data.length > 0) setPartnerCode(data[0].code)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function runValidate() {
    if (!partnerCode) {
      showError('Kies eerst een handelspartner.')
      return
    }
    setBusy(true)
    setResult(null)
    try {
      const outcome = await validatePayload({ partnerCode, messageType, payload })
      setResult(outcome)
    } catch (err) {
      showError(describeApiError(err, 'Valideren is mislukt.').message)
    } finally {
      setBusy(false)
    }
  }

  async function runSimulate() {
    if (!partnerCode) {
      showError('Kies eerst een handelspartner.')
      return
    }
    setBusy(true)
    try {
      const message = await simulate(partnerCode)
      showSuccess('Testbericht verzonden en verwerkt.')
      setCreatedMessageId(message.id)
    } catch (err) {
      showError(describeApiError(err, 'De simulatie mislukte.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <FormField label="Handelspartner" htmlFor="edi-test-partner">
        <select id="edi-test-partner" value={partnerCode} onChange={(e) => setPartnerCode(e.target.value)}>
          {partners.length === 0 && <option value="">Geen partners</option>}
          {partners.map((p) => (
            <option key={p.id} value={p.code}>
              {p.name} ({p.code})
            </option>
          ))}
        </select>
      </FormField>
      <FormField label="Berichttype" htmlFor="edi-test-type">
        <select id="edi-test-type" value={messageType} disabled>
          <option value="order">order</option>
        </select>
      </FormField>
      <FormField label="Payload (generiek JSON-profiel)" htmlFor="edi-test-payload">
        <textarea
          id="edi-test-payload"
          className="edi-payload-input"
          rows={16}
          value={payload}
          onChange={(e) => setPayload(e.target.value)}
          disabled={busy}
        />
      </FormField>

      <div className="edi-test-actions">
        <Button variant="secondary" onClick={() => void runValidate()} disabled={busy}>
          Valideren zonder te versturen
        </Button>
        <Button onClick={() => void runSimulate()} disabled={busy}>
          Versturen naar test
        </Button>
      </div>

      {result && (
        <section className="edi-test-result">
          {result.valid ? (
            <div>
              <Badge tone="success">Geldig ✓</Badge>
              {result.wouldCreate && (
                <dl className="edi-detail-grid">
                  <dt>Externe order-id</dt>
                  <dd>{result.wouldCreate.externalOrderId}</dd>
                  <dt>Klantreferentie</dt>
                  <dd>{result.wouldCreate.customerReference ?? '—'}</dd>
                  <dt>Omschrijving</dt>
                  <dd>{result.wouldCreate.goodsDescription}</dd>
                  <dt>Aantal stops</dt>
                  <dd>{result.wouldCreate.stopCount}</dd>
                  <dt>Aantal goederenlijnen</dt>
                  <dd>{result.wouldCreate.cargoLineCount}</dd>
                  <dt>Herkende locaties</dt>
                  <dd>{result.wouldCreate.resolvedLocationCodes.join(', ') || '—'}</dd>
                  <dt>Herkende eenheden</dt>
                  <dd>{result.wouldCreate.resolvedUnitCodes.join(', ') || '—'}</dd>
                </dl>
              )}
            </div>
          ) : (
            <div>
              <Badge tone="danger">Ongeldig</Badge>
              <ul className="edi-test-errors">
                {result.errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}
        </section>
      )}

      {createdMessageId && (
        <MessageDetailModal
          id={createdMessageId}
          canRetry
          onClose={() => setCreatedMessageId(null)}
          onReplayed={() => setCreatedMessageId(null)}
        />
      )}
    </div>
  )
}
