import { useId, useMemo, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { LookupSelect } from '../../master-data/components/LookupSelect'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'

type Scope = 'everyone' | 'specific'

interface EligibilitySelectProps {
  /** Selected job-function CODES (what the backend stores); empty = everyone. */
  codes: string[]
  onChange: (codes: string[]) => void
  disabled?: boolean
}

/**
 * "Voor wie is dit bedoeld?" — Iedereen, or a searchable pick of job functions shown as chips.
 * Users choose functions by name; the component maps to/from the stored codes. Codes that no
 * longer match a function (renamed or deleted lookup, or a list the user may not read) stay
 * visible as code chips so saving never silently drops them.
 */
export function EligibilitySelect({ codes, onChange, disabled = false }: EligibilitySelectProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const groupName = useId()
  const canViewFunctions = hasPermission('job_functions.view')
  const { options, isLoading } = useLookupOptions('/api/job-functions', { enabled: canViewFunctions })
  // The user may switch to "specific" before picking anything; derive the initial scope once.
  const [scope, setScope] = useState<Scope>(codes.length > 0 ? 'specific' : 'everyone')

  const byCode = useMemo(() => new Map(options.map((option) => [option.code.toLowerCase(), option])), [options])
  const effectiveScope: Scope = codes.length > 0 ? 'specific' : scope

  function switchScope(next: Scope) {
    setScope(next)
    if (next === 'everyone' && codes.length > 0) onChange([])
  }

  return (
    <div className="issued-items-eligibility">
      <div className="issued-items-radio-row" role="radiogroup" aria-label={t('issuedItems.form.sectionEligibility')}>
        <label className="issued-items-radio">
          <input
            type="radio"
            name={groupName}
            value="everyone"
            checked={effectiveScope === 'everyone'}
            disabled={disabled}
            onChange={() => switchScope('everyone')}
          />
          <span>{t('issuedItems.form.eligibilityEveryone')}</span>
        </label>
        <label className="issued-items-radio">
          <input
            type="radio"
            name={groupName}
            value="specific"
            checked={effectiveScope === 'specific'}
            disabled={disabled}
            onChange={() => switchScope('specific')}
          />
          <span>{t('issuedItems.form.eligibilitySpecific')}</span>
        </label>
      </div>

      {effectiveScope === 'specific' && (
        <div className="issued-items-eligibility-picker">
          <div className="issued-items-chips">
            {codes.map((code) => {
              const option = byCode.get(code.toLowerCase())
              const label = option ? option.name : isLoading ? code : t('issuedItems.form.eligibilityUnknownCode', { code })
              return (
                <Badge key={code} tone={option ? 'info' : 'neutral'}>
                  {label}
                  <button
                    type="button"
                    className="issued-items-chip-remove"
                    aria-label={t('issuedItems.form.eligibilityRemove', { name: option?.name ?? code })}
                    disabled={disabled}
                    onClick={() => onChange(codes.filter((other) => other !== code))}
                  >
                    ×
                  </button>
                </Badge>
              )
            })}
            {codes.length === 0 && <span className="issued-items-chips-empty">{t('issuedItems.form.eligibilityNone')}</span>}
          </div>
          {canViewFunctions ? (
            <LookupSelect
              id="tpl-eligibility-add"
              basePath="/api/job-functions"
              viewPermission="job_functions.view"
              managePermission="job_functions.manage"
              singular="masterData.singular.job-functions"
              valueKey="code"
              excludeValues={codes}
              value={null}
              onChange={(code) => {
                if (code && !codes.some((existing) => existing.toLowerCase() === code.toLowerCase())) onChange([...codes, code])
              }}
              placeholder={t('issuedItems.form.eligibilityAddPlaceholder')}
              disabled={disabled}
            />
          ) : (
            <p className="issued-items-section-hint">{t('issuedItems.form.eligibilityNoAccess')}</p>
          )}
        </div>
      )}
    </div>
  )
}
