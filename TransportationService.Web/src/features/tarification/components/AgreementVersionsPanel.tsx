import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { duplicateAgreement, type PricingAgreement } from '../api/pricingApi'

interface AgreementVersionsPanelProps {
  agreement: PricingAgreement
  canManage: boolean
  onDuplicated: (newAgreementId: string) => void
}

type RoundingChoice = '' | '0.01' | '0.05' | '0.10'

interface DuplicateDraft {
  name: string
  effectiveFrom: string
  closeSource: boolean
  mode: 'none' | 'percent' | 'fixed'
  value: string
  roundingStep: RoundingChoice
}

const nextYearName = (name: string) => `${name} ${new Date().getFullYear() + 1}`

/**
 * "Versies" tab: duplicates the agreement as a new version (new effective window, optionally
 * adjusted). Assignments are deliberately not copied — the new version needs its customers
 * linked explicitly on the Klanten tab.
 */
export function AgreementVersionsPanel({ agreement, canManage, onDuplicated }: AgreementVersionsPanelProps) {
  const { t } = useLocale()
  const [draft, setDraft] = useState<DuplicateDraft | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function open() {
    setError(null)
    setDraft({ name: nextYearName(agreement.name), effectiveFrom: '', closeSource: false, mode: 'none', value: '', roundingStep: '' })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.name.trim()) {
      setError(t('tarification.common.nameRequired'))
      return
    }
    if (!draft.effectiveFrom) {
      setError(t('tarification.common.chooseDate'))
      return
    }

    setBusy(true)
    setError(null)
    try {
      const duplicated = await duplicateAgreement(agreement.id, {
        name: draft.name.trim(),
        effectiveFrom: draft.effectiveFrom,
        closeSource: draft.closeSource,
        percent: draft.mode === 'percent' ? Number(draft.value) || 0 : null,
        amountDelta: draft.mode === 'fixed' ? Number(draft.value) || 0 : null,
        roundingStep: draft.roundingStep === '' ? null : Number(draft.roundingStep),
      })
      onDuplicated(duplicated.id)
    } catch (err) {
      setError(localizeApiError(t, err, t('tarification.versions.createError')))
    } finally {
      setBusy(false)
    }
  }

  if (!canManage) {
    return <EmptyState message={t('tarification.versions.noPermission')} />
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>{t('tarification.versions.title')}</h3>
      </div>
      <p className="customer-form-muted">{t('tarification.versions.hint')}</p>
      <Button onClick={open}>{t('tarification.versions.duplicate')}</Button>

      {draft && (
        <Modal
          title={t('tarification.versions.modalTitle')}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="duplicate-form" disabled={busy}>
                {busy ? t('ui.actions.busy') : t('tarification.common.create')}
              </Button>
            </>
          }
        >
          <form id="duplicate-form" className="issued-items-form" onSubmit={submit} noValidate>
            {error && (
              <div className="issued-items-form-error" role="alert">
                {error}
              </div>
            )}
            <FormField label={t('tarification.common.name')} htmlFor="dup-name" required>
              <input
                id="dup-name"
                value={draft.name}
                onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))}
                maxLength={200}
              />
            </FormField>
            <FormField label={t('tarification.common.effectiveDate')} htmlFor="dup-from" required>
              <input
                id="dup-from"
                type="date"
                value={draft.effectiveFrom}
                onChange={(e) => setDraft((d) => (d ? { ...d, effectiveFrom: e.target.value } : d))}
              />
            </FormField>
            <label className="tof-checkbox">
              <input
                type="checkbox"
                checked={draft.closeSource}
                onChange={(e) => setDraft((d) => (d ? { ...d, closeSource: e.target.checked } : d))}
              />
              {t('tarification.versions.closeSource')}
            </label>
            <div className="issued-items-form-row">
              <FormField label={t('tarification.common.adjustment')} htmlFor="dup-mode">
                <select
                  id="dup-mode"
                  value={draft.mode}
                  onChange={(e) => setDraft((d) => (d ? { ...d, mode: e.target.value as DuplicateDraft['mode'] } : d))}
                >
                  <option value="none">{t('tarification.common.none')}</option>
                  <option value="percent">{t('tarification.common.percentage')}</option>
                  <option value="fixed">{t('tarification.common.fixedAmount')}</option>
                </select>
              </FormField>
              {draft.mode !== 'none' && (
                <FormField label={t('tarification.common.value')} htmlFor="dup-value">
                  <input
                    id="dup-value"
                    type="number"
                    step="0.01"
                    value={draft.value}
                    onChange={(e) => setDraft((d) => (d ? { ...d, value: e.target.value } : d))}
                  />
                </FormField>
              )}
            </div>
            {draft.mode !== 'none' && (
              <FormField label={t('tarification.common.rounding')} htmlFor="dup-rounding">
                <select
                  id="dup-rounding"
                  value={draft.roundingStep}
                  onChange={(e) => setDraft((d) => (d ? { ...d, roundingStep: e.target.value as RoundingChoice } : d))}
                >
                  <option value="">{t('tarification.common.none')}</option>
                  <option value="0.01">€ 0,01</option>
                  <option value="0.05">€ 0,05</option>
                  <option value="0.10">€ 0,10</option>
                </select>
              </FormField>
            )}
          </form>
        </Modal>
      )}
    </section>
  )
}
