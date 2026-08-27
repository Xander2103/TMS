import { useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  listPartners,
  simulate,
  validatePayload,
  SAMPLE_PAYLOAD,
  type EdiPartner,
  type EdiValidationResult,
} from '../api/ediApi'
import { MessageDetailModal } from './MessageDetailModal'

interface TestenTabProps {
  /** Whether the detail modal opened after sending to test may show its replay button (edi.retry or edi.manage). */
  canRetry: boolean
}

/** "Testen" tab: dry-run validation against the live mapping (no side effects) plus the
 * development simulator that actually ingests a sample order. */
export function TestenTab({ canRetry }: TestenTabProps) {
  const { t } = useLocale()
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
      showError(t('edi.test.choosePartner'))
      return
    }
    setBusy(true)
    setResult(null)
    try {
      const outcome = await validatePayload({ partnerCode, messageType, payload })
      setResult(outcome)
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.test.validateFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function runSimulate() {
    if (!partnerCode) {
      showError(t('edi.test.choosePartner'))
      return
    }
    setBusy(true)
    try {
      const message = await simulate(partnerCode)
      showSuccess(t('edi.test.sent'))
      setCreatedMessageId(message.id)
    } catch (err) {
      showError(localizeApiError(t, err, t('edi.test.simulateFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <FormField label={t('edi.test.partnerLabel')} htmlFor="edi-test-partner">
        <select id="edi-test-partner" value={partnerCode} onChange={(e) => setPartnerCode(e.target.value)}>
          {partners.length === 0 && <option value="">{t('edi.test.noPartners')}</option>}
          {partners.map((p) => (
            <option key={p.id} value={p.code}>
              {p.name} ({p.code})
            </option>
          ))}
        </select>
      </FormField>
      <FormField label={t('edi.test.typeLabel')} htmlFor="edi-test-type">
        <select id="edi-test-type" value={messageType} disabled>
          <option value="order">order</option>
        </select>
      </FormField>
      <FormField label={t('edi.test.payloadLabel')} htmlFor="edi-test-payload">
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
          {t('edi.test.validate')}
        </Button>
        <Button onClick={() => void runSimulate()} disabled={busy}>
          {t('edi.test.send')}
        </Button>
      </div>

      {result && (
        <section className="edi-test-result">
          {result.valid ? (
            <div>
              <Badge tone="success">{t('edi.test.valid')}</Badge>
              {result.wouldCreate && (
                <dl className="edi-detail-grid">
                  <dt>{t('edi.test.externalOrderId')}</dt>
                  <dd>{result.wouldCreate.externalOrderId}</dd>
                  <dt>{t('edi.test.customerReference')}</dt>
                  <dd>{result.wouldCreate.customerReference ?? '—'}</dd>
                  <dt>{t('edi.test.description')}</dt>
                  <dd>{result.wouldCreate.goodsDescription}</dd>
                  <dt>{t('edi.test.stopCount')}</dt>
                  <dd>{result.wouldCreate.stopCount}</dd>
                  <dt>{t('edi.test.cargoLineCount')}</dt>
                  <dd>{result.wouldCreate.cargoLineCount}</dd>
                  <dt>{t('edi.test.resolvedLocations')}</dt>
                  <dd>{result.wouldCreate.resolvedLocationCodes.join(', ') || '—'}</dd>
                  <dt>{t('edi.test.resolvedUnits')}</dt>
                  <dd>{result.wouldCreate.resolvedUnitCodes.join(', ') || '—'}</dd>
                </dl>
              )}
            </div>
          ) : (
            <div>
              <Badge tone="danger">{t('edi.test.invalid')}</Badge>
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
          canRetry={canRetry}
          onClose={() => setCreatedMessageId(null)}
          onReplayed={() => setCreatedMessageId(null)}
        />
      )}
    </div>
  )
}
