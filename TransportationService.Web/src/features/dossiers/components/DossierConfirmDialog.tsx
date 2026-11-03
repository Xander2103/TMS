import { useEffect, useState, type ReactNode } from 'react'
import { AlertTriangle, ChevronUp, ClipboardList, Coins, FileText, FolderOpen, Info, ListChecks, OctagonAlert, PackageCheck, ShieldAlert, TriangleAlert, type LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import { localizeApiError } from '../../../api/problemDetails'
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

  // EX-05: every open afwijking is listed — Blokkerend (holds auto-confirmation), Waarschuwing, Ter info.
  const holds = evaluation?.warnings.filter((w) => w.severity === 'Hold') ?? []
  const warnings = evaluation?.warnings.filter((w) => w.severity === 'Warning') ?? []
  const infos = evaluation?.warnings.filter((w) => w.severity === 'Info') ?? []
  const blocked = !!evaluation && !evaluation.canConfirmManually
  const needsAcknowledgement = warnings.length > 0 || holds.length > 0
  const needsReason = holds.length > 0
  const canSubmit = !!evaluation && !blocked && (!needsAcknowledgement || acknowledged)
    && (!needsReason || reason.trim().length > 0) && !busy

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
    } catch (err) {
      setError(localizeApiError(t, err, t('dossiers.lifecycle.confirmFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`${t('dossiers.lifecycle.confirmTitle')} — ${dossierNumber}`}
      onClose={onClose}
      busy={busy}
      size="wide"
      titleIcon={<FolderOpen size={20} />}
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
          {holds.length > 0 && (
            <CheckList tone="blocking" title={t('dossiers.lifecycle.holdTitle')} hint={t('dossiers.lifecycle.holdHint')} checks={holds} />
          )}
          {warnings.length > 0 && <CheckList tone="warning" title={t('dossiers.lifecycle.warningsTitle')} checks={warnings} />}
          {infos.length > 0 && <CheckList tone="info" title={t('dossiers.lifecycle.infoTitle')} checks={infos} />}
          {evaluation.blockers.length === 0 && holds.length === 0 && warnings.length === 0 && infos.length === 0 && (
            <p className="dossier-confirm-ok">{t('dossiers.lifecycle.noIssues')}</p>
          )}

          {!blocked && needsAcknowledgement && (
            <label className="dossier-confirm-ack">
              <input type="checkbox" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />
              {t('dossiers.lifecycle.acknowledge')}
            </label>
          )}

          {!blocked && (
            <FormField
              label={needsReason ? t('dossiers.lifecycle.reasonRequiredLabel') : t('dossiers.lifecycle.reasonLabel')}
              htmlFor="dossier-confirm-reason"
              required={needsReason}
              hint={needsReason ? t('dossiers.lifecycle.reasonRequiredHint') : t('dossiers.lifecycle.reasonHint')}
            >
              <textarea id="dossier-confirm-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={1000} />
            </FormField>
          )}
        </>
      )}

      {error && <p className="dossier-confirm-error" role="alert">{error}</p>}
    </Modal>
  )
}

function SummaryTile({ icon: Icon, label, children }: { icon: LucideIcon; label: string; children: ReactNode }) {
  return (
    <div className="dossier-confirm-tile">
      <dt>
        <Icon size={18} aria-hidden="true" />
        {label}
      </dt>
      <dd>{children}</dd>
    </div>
  )
}

function ConfirmationSummary({ evaluation }: { evaluation: DossierConfirmationEvaluation }) {
  const { t } = useLocale()
  const s = evaluation.summary
  const k = 'dossiers.lifecycle.summary.'
  return (
    <dl className="dossier-confirm-summary">
      <SummaryTile icon={ClipboardList} label={t(`${k}orders`)}>
        {t(`${k}ordersValue`, { completed: s.ordersCompleted, total: s.ordersTotal })}
        {s.ordersCancelled > 0 && ` · ${t(`${k}ordersCancelled`, { count: s.ordersCancelled })}`}
      </SummaryTile>
      {s.ordersCompleted > 0 && (
        <SummaryTile icon={PackageCheck} label={t(`${k}deliveries`)}>
          {s.deliveriesFailed > 0 ? t(`${k}deliveriesFailed`, { count: s.deliveriesFailed }) : t(`${k}deliveriesOk`)}
          {s.podMissing > 0 && ` · ${t(`${k}podMissing`, { count: s.podMissing })}`}
        </SummaryTile>
      )}
      {s.activitiesExecutable > 0 && (
        <SummaryTile icon={ListChecks} label={t(`${k}activities`)}>
          {t(`${k}activitiesValue`, { executed: s.activitiesExecuted, total: s.activitiesExecutable })}
        </SummaryTile>
      )}
      <SummaryTile icon={Coins} label={t(`${k}pricing`)}>
        {t(`${k}pricingValue`, { priced: s.pricedUnits, total: s.billableUnits })}
      </SummaryTile>
      <SummaryTile icon={FileText} label={t(`${k}documents`)}>
        {t(`${k}documentsValue`, { count: s.documents })} · {s.hasCmr ? t(`${k}cmrPresent`) : t(`${k}cmrMissing`)}
      </SummaryTile>
      {s.openIncidents > 0 && (
        <SummaryTile icon={ShieldAlert} label={t(`${k}incidents`)}>{s.openIncidents}</SummaryTile>
      )}
      {(s.openExceptions ?? 0) > 0 && (
        <SummaryTile icon={TriangleAlert} label={t(`${k}exceptions`)}>
          {s.openExceptions}
          {(s.openExceptionsHolding ?? 0) > 0 && ` · ${t(`${k}exceptionsHolding`, { count: s.openExceptionsHolding ?? 0 })}`}
        </SummaryTile>
      )}
    </dl>
  )
}

const TONE_ICONS: Record<'blocking' | 'warning' | 'info', LucideIcon> = { blocking: OctagonAlert, warning: AlertTriangle, info: Info }

function CheckList({ tone, title, hint, checks }: {
  tone: 'blocking' | 'warning' | 'info'; title: string; hint?: string; checks: DossierConfirmationCheck[]
}) {
  const { t } = useLocale()
  const Icon = TONE_ICONS[tone]
  return (
    <details className={`dossier-confirm-checks dossier-confirm-checks-${tone}`} open>
      <summary>
        <Icon className="dossier-confirm-checks-icon" size={20} aria-hidden="true" />
        <h4>{title}</h4>
        <span className="dossier-confirm-count">{checks.length}</span>
        <ChevronUp className="dossier-confirm-chevron" size={18} aria-hidden="true" />
      </summary>
      <div className="dossier-confirm-checks-body">
        {hint && <p className="dossier-confirm-hint">{hint}</p>}
        <ul>
          {checks.map((check, index) => (
            <li key={`${check.code}-${check.transportOrderId ?? ''}-${check.activityId ?? ''}-${index}`}>
              {check.message}
              {check.problemId && (
                <>
                  {' '}
                  <Link to={`/exceptions/${check.problemId}`}>{t('dossiers.lifecycle.openException')}</Link>
                </>
              )}
            </li>
          ))}
        </ul>
      </div>
    </details>
  )
}
