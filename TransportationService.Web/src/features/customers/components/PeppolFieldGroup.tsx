import { useState } from 'react'
import { FormField } from '../../../components/ui/FormField'
import type { PeppolScheme, PeppolStatus } from '../types'

const STATUS_TEXT: Record<PeppolStatus, string> = {
  auto: 'Gevalideerd',
  manual: 'Manueel ingevoerd',
  'not-found': 'Niet gevonden',
  'not-validated': 'Niet gevalideerd',
}

/** Renders scheme + participant as the single combined UI value ("0208:0123456789"). */
export function combinePeppolValue(scheme: string, participantId: string): string {
  return scheme ? `${scheme}:${participantId}` : participantId
}

/**
 * Maps the single UI value onto the separately stored backend columns. A "NNNN:" prefix is
 * the scheme; anything else stays in the participant id verbatim (never invent values) so
 * validation can flag it instead of data being silently rewritten.
 */
export function parsePeppolValue(raw: string): { scheme: string; participantId: string } {
  const idx = raw.indexOf(':')
  if (idx < 0) return { scheme: '', participantId: raw }
  const prefix = raw.slice(0, idx)
  const rest = raw.slice(idx + 1)
  if (/^\d{4}$/.test(prefix) && !rest.includes(':')) return { scheme: prefix, participantId: rest }
  return { scheme: '', participantId: raw }
}

/** Client-side check of the combined format; null when the value is acceptable. */
export function peppolFormatError(raw: string): string | null {
  if (!raw.includes(':')) return null
  const idx = raw.indexOf(':')
  const prefix = raw.slice(0, idx)
  const rest = raw.slice(idx + 1)
  if (!/^\d{4}$/.test(prefix) || rest.includes(':')) {
    return 'Ongeldig Peppol-ID. Gebruik "schema:nummer", bv. 0208:0123456789.'
  }
  return null
}

interface PeppolFieldGroupProps {
  scheme: string
  participantId: string
  status: PeppolStatus
  schemes: PeppolScheme[]
  disabled?: boolean
  /** Extra (server) validation message shown on the combined field. */
  error?: string
  onChange: (next: { scheme: string; participantId: string }) => void
}

/**
 * One "Peppol-ID" input with a provenance/validation chip (Gevalideerd / Manueel ingevoerd /
 * Niet gevonden / Niet gevalideerd). The backend keeps scheme and participant id as separate
 * columns; this control maps the single value both ways. An optional "Geavanceerd" toggle
 * exposes the raw scheme + participant fields for users who may edit them separately.
 */
export function PeppolFieldGroup({
  scheme,
  participantId,
  status,
  schemes,
  disabled,
  error,
  onChange,
}: PeppolFieldGroupProps) {
  const [advancedOpen, setAdvancedOpen] = useState(false)
  const combined = combinePeppolValue(scheme, participantId)
  const formatError = peppolFormatError(combined)
  const knownScheme = schemes.find((s) => s.code === scheme)
  const schemeUnknown = scheme !== '' && schemes.length > 0 && !knownScheme

  return (
    <fieldset className="peppol-group" aria-label="Peppol">
      <legend className="peppol-group-legend">
        Peppol
        <span className={`peppol-status peppol-status-${status}`}>{STATUS_TEXT[status]}</span>
      </legend>
      <div className="peppol-group-fields">
        <FormField
          label="Peppol-ID"
          htmlFor="peppol-id"
          hint={knownScheme ? `Schema ${knownScheme.code} — ${knownScheme.label}.` : 'Bv. 0208:0123456789 (schema:nummer), of enkel het nummer.'}
          error={formatError ?? error}
        >
          <>
            <input
              id="peppol-id"
              value={combined}
              maxLength={70}
              disabled={disabled}
              aria-invalid={formatError || error ? 'true' : undefined}
              onChange={(e) => onChange(parsePeppolValue(e.target.value))}
            />
            {schemeUnknown && (
              <p className="customer-form-warning" role="status">
                Schema {scheme} staat niet in de schemalijst — controleer het Peppol-ID.
              </p>
            )}
          </>
        </FormField>
        {!disabled && (
          <button
            type="button"
            className="peppol-advanced-toggle"
            aria-expanded={advancedOpen}
            onClick={() => setAdvancedOpen((open) => !open)}
          >
            {advancedOpen ? 'Geavanceerd verbergen' : 'Geavanceerd'}
          </button>
        )}
        {advancedOpen && !disabled && (
          <div className="peppol-advanced-fields">
            <FormField label="Schema" htmlFor="peppol-scheme">
              <select
                id="peppol-scheme"
                value={scheme}
                onChange={(e) => onChange({ scheme: e.target.value, participantId })}
              >
                <option value="">—</option>
                {/* Keep an out-of-catalog stored scheme selectable instead of blanking it. */}
                {schemeUnknown && <option value={scheme}>{scheme} — onbekend schema</option>}
                {schemes.map((s) => (
                  <option key={s.code} value={s.code}>
                    {s.code} — {s.label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Participant-ID" htmlFor="peppol-participant" hint="Zonder schema, bv. 0123456789.">
              <input
                id="peppol-participant"
                value={participantId}
                maxLength={64}
                onChange={(e) => onChange({ scheme, participantId: e.target.value })}
              />
            </FormField>
          </div>
        )}
      </div>
    </fieldset>
  )
}
