import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import type { NegativeStockPayload } from '../inventoryApi'
import './NegativeStockConfirmModal.css'

interface NegativeStockConfirmModalProps {
  /** 409-payload van de server (negative_stock_confirmation_required). */
  payload: NegativeStockPayload
  /** Bepaalt de waarschuwingstekst en of een redenveld wordt getoond (correcties dragen hun reden al mee). */
  kind: 'issue' | 'correction'
  /** Naam van de medewerker aan wie wordt uitgereikt, indien van toepassing. */
  employeeName?: string | null
  /** Opslaglocatie van het artikel, indien bekend. */
  storageLocation?: string | null
  /** Zonder bevestigingsrecht toont de modal enkel een melding, geen bevestigknop. */
  canConfirm: boolean
  busy?: boolean
  onConfirm: (reason: string) => void
  onCancel: () => void
}

/**
 * Domme bevestigingsmodal voor voorraadverlagende mutaties die onder nul gaan.
 * Toont de servercijfers uit de 409 en stuurt enkel de (optionele) reden terug.
 */
export function NegativeStockConfirmModal({
  payload,
  kind,
  employeeName,
  storageLocation,
  canConfirm,
  busy = false,
  onConfirm,
  onCancel,
}: NegativeStockConfirmModalProps) {
  const [reason, setReason] = useState('')

  const needsReason = kind === 'issue' && payload.requiresReason
  const confirmDisabled = busy || (needsReason && reason.trim() === '')

  return (
    <Modal
      title="Negatieve voorraad bevestigen"
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Annuleren
          </Button>
          {canConfirm && (
            <Button variant="danger" onClick={() => onConfirm(reason.trim())} disabled={confirmDisabled}>
              Bevestig negatieve voorraad
            </Button>
          )}
        </>
      }
    >
      <div className="negative-stock-body">
        <p className="negative-stock-warning" role="alert">
          <span aria-hidden="true">⚠</span>{' '}
          {kind === 'issue'
            ? 'Deze uitgifte brengt de voorraad onder nul.'
            : 'Deze correctie brengt de voorraad onder nul.'}
        </p>
        {payload.versionMismatch && (
          <p className="negative-stock-mismatch" role="alert">
            De voorraad is intussen gewijzigd; controleer de nieuwe stand.
          </p>
        )}
        <dl className="negative-stock-summary">
          <div>
            <dt>Artikel</dt>
            <dd>
              {payload.itemName}
              {payload.variantLabel ? ` — ${payload.variantLabel}` : ''}
            </dd>
          </div>
          {employeeName && (
            <div>
              <dt>Medewerker</dt>
              <dd>{employeeName}</dd>
            </div>
          )}
          {storageLocation && (
            <div>
              <dt>Locatie</dt>
              <dd>{storageLocation}</dd>
            </div>
          )}
          <div>
            <dt>Huidige voorraad</dt>
            <dd>{payload.currentStock}</dd>
          </div>
          <div>
            <dt>Gevraagde hoeveelheid</dt>
            <dd>{Math.abs(payload.requestedDelta)}</dd>
          </div>
          <div>
            <dt>Nieuwe voorraad</dt>
            <dd>
              <span className="negative-stock-projected">
                <span aria-hidden="true">⚠</span> {payload.projectedStock} — negatieve voorraad
              </span>
            </dd>
          </div>
        </dl>
        {!canConfirm && (
          <p className="negative-stock-no-permission" role="alert">
            Je hebt geen rechten om negatieve voorraad te bevestigen. Vraag een beheerder om deze actie te bevestigen.
          </p>
        )}
        {canConfirm && kind === 'issue' && (
          <FormField
            label="Reden"
            htmlFor="negative-stock-reason"
            required={needsReason}
            hint={needsReason ? 'Een reden is verplicht bij negatieve voorraad.' : undefined}
          >
            <textarea
              id="negative-stock-reason"
              rows={2}
              value={reason}
              maxLength={300}
              onChange={(e) => setReason(e.target.value)}
              disabled={busy}
            />
          </FormField>
        )}
      </div>
    </Modal>
  )
}
