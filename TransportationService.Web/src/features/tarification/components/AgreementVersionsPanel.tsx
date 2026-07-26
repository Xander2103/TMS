import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
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
      setError('De naam is verplicht.')
      return
    }
    if (!draft.effectiveFrom) {
      setError('Kies een ingangsdatum.')
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
      setError(describeApiError(err, 'De nieuwe versie kon niet worden aangemaakt.').message)
    } finally {
      setBusy(false)
    }
  }

  if (!canManage) {
    return <EmptyState message="Je hebt geen rechten om een nieuwe versie van deze tabel te maken." />
  }

  return (
    <section className="customer-panel">
      <div className="customer-panel-header">
        <h3>Nieuwe versie</h3>
      </div>
      <p className="customer-form-muted">
        Maak een nieuwe versie van deze tabel met een eigen ingangsdatum. Klantkoppelingen worden niet
        overgenomen — koppel klanten aan de nieuwe versie via het tabblad Klanten.
      </p>
      <Button onClick={open}>+ Dupliceren als nieuwe versie</Button>

      {draft && (
        <Modal
          title="Nieuwe versie aanmaken"
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDraft(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="duplicate-form" disabled={busy}>
                {busy ? 'Bezig...' : 'Aanmaken'}
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
            <FormField label="Naam" htmlFor="dup-name" required>
              <input
                id="dup-name"
                value={draft.name}
                onChange={(e) => setDraft((d) => (d ? { ...d, name: e.target.value } : d))}
                maxLength={200}
              />
            </FormField>
            <FormField label="Ingangsdatum" htmlFor="dup-from" required>
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
              Sluit huidige versie af (geldig tot ingangsdatum nieuwe versie min 1 dag)
            </label>
            <div className="issued-items-form-row">
              <FormField label="Aanpassing" htmlFor="dup-mode">
                <select
                  id="dup-mode"
                  value={draft.mode}
                  onChange={(e) => setDraft((d) => (d ? { ...d, mode: e.target.value as DuplicateDraft['mode'] } : d))}
                >
                  <option value="none">Geen</option>
                  <option value="percent">Percentage</option>
                  <option value="fixed">Vast bedrag</option>
                </select>
              </FormField>
              {draft.mode !== 'none' && (
                <FormField label="Waarde" htmlFor="dup-value">
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
              <FormField label="Afronding" htmlFor="dup-rounding">
                <select
                  id="dup-rounding"
                  value={draft.roundingStep}
                  onChange={(e) => setDraft((d) => (d ? { ...d, roundingStep: e.target.value as RoundingChoice } : d))}
                >
                  <option value="">Geen</option>
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
