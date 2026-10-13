import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import { confirmDossier, getDossierConfirmation } from '../api/dossiersApi'
import type { DossierConfirmationCheck, DossierConfirmationEvaluation, DossierDetail } from '../types'
import './dossier-confirm-dialog.css'

interface DossierConfirmDialogProps {
  dossierId: string
  dossierNumber: string
  /** Dossier concurrency token when known (detail page); the list may omit it. */
  version?: string
  onConfirmed: (dossier: DossierDetail) => void
  onClose: () => void
}

/**
 * Confirmation sprint 2026-09-23 — "Dossier bevestigen": one dialog for the detail header AND
 * the list row action. It loads the server evaluation, shows a compact readiness summary,
 * refuses on blockers, and lets a person acknowledge warnings deliberately (historic dossiers).
 * Confirmation is operational only — pricing and invoicing are untouched, and the intro says so.
 */
export function DossierConfirmDialog({ dossierId, dossierNumber, version, onConfirmed, onClose }: DossierConfirmDialogProps) {
  const { t } = useLocale()
  const [evaluation, setEvaluation] = useState<DossierConfirmationEvaluation | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [reason, setReason] = useState('')
  const [acknowledged, setAcknowledged] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    getDossierConfirmation(dossierId)
      .then((result) => { if (active) setEvaluation(result) })
      .catch(() => { if (active) setLoadError(true) })
    return () => { active = false }
  }, [dossierId])

  const warnings = evaluation?.warnings.filter((w) => w.severity === 'Warning') ?? []
  const infos = evaluation?.warnings.filter((w) => w.severity === 'Info') ?? []
  const blocked = !!evaluation && !evaluation.canConfirmManually
  const needsAcknowledgement = warnings.length > 0
  const canSubmit = !!evaluation && !blocked && (!needsAcknowledgement || acknowledged) && !busy

  async function submit() {
    if (!canSubmit) return
    setBusy(true)
    setError(null)
    try {
      const trimmed = reason.trim()
      const dossier = await confirmDossier(dossierId, {
        reason: trimmed.length > 0 ? trimmed : null,
        acknowledgeWarnings: needsAcknowledgement && acknowledged,
        version,
      })
      onConfirmed(dossier)
    } catch {
      setError(t('dossiers.lifecycle.confirmFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`${t('dossiers.lifecycle.confirmTitle')} — ${dossierNumber}`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>{t('ui.actions.cancel')}</Button>
          <Button onClick={() => void submit()} disabled={!canSubmit}>
            {busy ? t('ui.actions.busy') : t('dossiers.lifecycle.confirm')}
          </Button>
        </>
      }
    >
      <p className="dossier-confirm-intro">{t('dossiers.lifecycle.confirmIntro')}</p>

      {loadError && <p className="dossier-confirm-error" role="alert">{t('dossiers.lifecycle.loadFailed')}</p>}
      {!evaluation && !loadError && <p className="dossier-confirm-loading">{t('dossiers.lifecycle.loading')}</p>}

      {evaluation && (
        <>
          <ConfirmationSummary evaluation={evaluation} />

          {evaluation.blockers.length > 0 && (
            <CheckList tone="blocking" title={t('dossiers.lifecycle.blockersTitle')} checks={evaluation.blockers} />
          )}
          {warnings.length > 0 && <CheckList tone="warning" title={t('dossiers.lifecycle.warningsTitle')} checks={warnings} />}
          {infos.length > 0 && <CheckList tone="info" title={t('dossiers.lifecycle.infoTitle')} checks={infos} />}
          {evaluation.blockers.length === 0 && warnings.length === 0 && infos.length === 0 && (
            <p className="dossier-confirm-ok">{t('dossiers.lifecycle.noIssues')}</p>
          )}

          {!blocked && needsAcknowledgement && (
            <label className="dossier-confirm-ack">
              <input type="checkbox" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />
              {' '}{t('dossiers.lifecycle.acknowledge')}
            </label>
          )}

          {!blocked && (
            <FormField label={t('dossiers.lifecycle.reasonLabel')} htmlFor="dossier-confirm-reason" hint={t('dossiers.lifecycle.reasonHint')}>
              <textarea id="dossier-confirm-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={1000} />
            </FormField>
          )}
        </>
      )}

      {error && <p className="dossier-confirm-error" role="alert">{error}</p>}
    </Modal>
  )
}

function ConfirmationSummary({ evaluation }: { evaluation: DossierConfirmationEvaluation }) {
  const { t } = useLocale()
  const s = evaluation.summary
  const k = 'dossiers.lifecycle.summary.'
  return (
    <dl className="dossier-confirm-summary">
      <div>
        <dt>{t(`${k}orders`)}</dt>
        <dd>
          {t(`${k}ordersValue`, { completed: s.ordersCompleted, total: s.ordersTotal })}
          {s.ordersCancelled > 0 && ` · ${t(`${k}ordersCancelled`, { count: s.ordersCancelled })}`}
        </dd>
      </div>
      {s.ordersCompleted > 0 && (
        <div>
          <dt>{t(`${k}deliveries`)}</dt>
          <dd>
            {s.deliveriesFailed > 0 ? t(`${k}deliveriesFailed`, { count: s.deliveriesFailed }) : t(`${k}deliveriesOk`)}
            {s.podMissing > 0 && ` · ${t(`${k}podMissing`, { count: s.podMissing })}`}
          </dd>
        </div>
      )}
      {s.activitiesExecutable > 0 && (
        <div>
          <dt>{t(`${k}activities`)}</dt>
          <dd>{t(`${k}activitiesValue`, { executed: s.activitiesExecuted, total: s.activitiesExecutable })}</dd>
        </div>
      )}
      <div>
        <dt>{t(`${k}pricing`)}</dt>
        <dd>{t(`${k}pricingValue`, { priced: s.pricedUnits, total: s.billableUnits })}</dd>
      </div>
      <div>
        <dt>{t(`${k}documents`)}</dt>
        <dd>{t(`${k}documentsValue`, { count: s.documents })} · {s.hasCmr ? t(`${k}cmrPresent`) : t(`${k}cmrMissing`)}</dd>
      </div>
      {s.openIncidents > 0 && (
        <div>
          <dt>{t(`${k}incidents`)}</dt>
          <dd>{s.openIncidents}</dd>
        </div>
      )}
    </dl>
  )
}

function CheckList({ tone, title, checks }: { tone: 'blocking' | 'warning' | 'info'; title: string; checks: DossierConfirmationCheck[] }) {
  return (
    <section className={`dossier-confirm-checks dossier-confirm-checks-${tone}`}>
      <h4>{title}</h4>
      <ul>
        {checks.map((check, index) => (
          <li key={`${check.code}-${check.transportOrderId ?? ''}-${check.activityId ?? ''}-${index}`}>{check.message}</li>
        ))}
      </ul>
    </section>
  )
}
