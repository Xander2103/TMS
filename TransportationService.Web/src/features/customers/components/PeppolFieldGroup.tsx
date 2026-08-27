import { useState } from 'react'
import { FormField } from '../../../components/ui/FormField'
import { useLocale } from '../../../i18n/localeContext'
import { combinePeppolValue, parsePeppolValue, peppolFormatError } from '../utils/peppolValue'
import type { PeppolScheme, PeppolStatus } from '../types'
// The .peppol-* styles live in the shared customers stylesheet; import it here so the
// component also carries its styles when reused outside the customer pages (legal entities).
import './customers.css'

/** Vertaalsleutels per herkomst-/validatiestatus van de gecombineerde Peppol-waarde. */
const STATUS_TEXT_KEYS: Record<PeppolStatus, string> = {
  auto: 'customers.peppolStatus.auto',
  manual: 'customers.peppolStatus.manual',
  'not-found': 'customers.peppolStatus.notFound',
  'not-validated': 'customers.peppolStatus.notValidated',
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
 * Peppol-identifiers en schemacodes zijn data en worden nooit vertaald.
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
  const { t } = useLocale()
  const [advancedOpen, setAdvancedOpen] = useState(false)
  const combined = combinePeppolValue(scheme, participantId)
  const formatErrorKey = peppolFormatError(combined)
  const formatError = formatErrorKey ? t(formatErrorKey) : null
  const knownScheme = schemes.find((s) => s.code === scheme)
  const schemeUnknown = scheme !== '' && schemes.length > 0 && !knownScheme

  return (
    <fieldset className="peppol-group" aria-label="Peppol">
      <legend className="peppol-group-legend">
        Peppol
        <span className={`peppol-status peppol-status-${status}`}>{t(STATUS_TEXT_KEYS[status])}</span>
      </legend>
      <div className="peppol-group-fields">
        <FormField
          label={t('customers.peppolGroup.idLabel')}
          htmlFor="peppol-id"
          hint={
            knownScheme
              ? t('customers.peppolGroup.knownSchemeHint', { code: knownScheme.code, label: knownScheme.label })
              : t('customers.peppolGroup.formatHint')
          }
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
                {t('customers.peppolGroup.unknownSchemeWarning', { scheme })}
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
            {advancedOpen ? t('customers.peppolGroup.advancedHide') : t('customers.peppolGroup.advancedShow')}
          </button>
        )}
        {advancedOpen && !disabled && (
          <div className="peppol-advanced-fields">
            <FormField label={t('customers.peppolGroup.schemeLabel')} htmlFor="peppol-scheme">
              <select
                id="peppol-scheme"
                value={scheme}
                onChange={(e) => onChange({ scheme: e.target.value, participantId })}
              >
                <option value="">—</option>
                {/* Keep an out-of-catalog stored scheme selectable instead of blanking it. */}
                {schemeUnknown && (
                  <option value={scheme}>{t('customers.peppolGroup.unknownSchemeOption', { scheme })}</option>
                )}
                {schemes.map((s) => (
                  <option key={s.code} value={s.code}>
                    {s.code} — {s.label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField
              label={t('customers.peppolGroup.participantLabel')}
              htmlFor="peppol-participant"
              hint={t('customers.peppolGroup.participantHint')}
            >
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
