import { FormField } from '../../../components/ui/FormField'
import type { PeppolScheme, PeppolStatus } from '../types'

const STATUS_TEXT: Record<PeppolStatus, string> = {
  auto: 'Automatisch opgehaald',
  manual: 'Handmatig ingevoerd',
  'not-found': 'Niet gevonden',
  'not-validated': 'Niet gevalideerd',
}

interface PeppolFieldGroupProps {
  scheme: string
  participantId: string
  status: PeppolStatus
  schemes: PeppolScheme[]
  disabled?: boolean
  onChange: (next: { scheme: string; participantId: string }) => void
}

/**
 * Groups the Peppol scheme (EAS) and participant ID into one control with a provenance
 * status chip (auto / manual / not-found / not-validated). Values stay owned by the
 * parent form; this component only renders + reports changes.
 */
export function PeppolFieldGroup({
  scheme,
  participantId,
  status,
  schemes,
  disabled,
  onChange,
}: PeppolFieldGroupProps) {
  return (
    <fieldset className="peppol-group" aria-label="Peppol">
      <legend className="peppol-group-legend">
        Peppol
        <span className={`peppol-status peppol-status-${status}`}>{STATUS_TEXT[status]}</span>
      </legend>
      <div className="peppol-group-fields">
        <FormField label="Schema" htmlFor="peppol-scheme">
          <select
            id="peppol-scheme"
            value={scheme}
            disabled={disabled}
            onChange={(e) => onChange({ scheme: e.target.value, participantId })}
          >
            <option value="">—</option>
            {schemes.map((s) => (
              <option key={s.code} value={s.code}>
                {s.code} — {s.label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Participant-ID" htmlFor="peppol-id" hint="Zonder schema, bv. 0123456789.">
          <input
            id="peppol-id"
            value={participantId}
            maxLength={64}
            disabled={disabled}
            onChange={(e) => onChange({ scheme, participantId: e.target.value })}
          />
        </FormField>
      </div>
    </fieldset>
  )
}
