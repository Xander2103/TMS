import type { ReactNode } from 'react'
import { FormField } from '../../../../components/ui/FormField'
import { useLocale } from '../../../../i18n/localeContext'
import type { CraneJobKind } from '../../types'
import type { CraneJobFormValues } from './orderFormState'
import './route-section.css'

interface CraneJobKindFieldProps {
  /** Unique per page: the radio group name and the input ids derive from it. */
  idPrefix: string
  kind: CraneJobKind
  onChange: (kind: CraneJobKind) => void
  disabled?: boolean
}

/**
 * "Soort kraanopdracht" (master sprint 2026-09-21, D2). Only rendered for an activity type with
 * the `supportsOnSiteWork` capability — the caller decides that from the FLAG, never from a code.
 * Transport met kraan = the classic loading → unloading order; Hijswerk op locatie = one work
 * site, a description of the work and no goods.
 */
export function CraneJobKindField({ idPrefix, kind, onChange, disabled }: CraneJobKindFieldProps) {
  const { t } = useLocale()
  const options: Array<{ value: CraneJobKind; label: string }> = [
    { value: 'TransportWithCrane', label: t('stopEditor.crane.kindTransport') },
    { value: 'OnSiteLifting', label: t('stopEditor.crane.kindOnSite') },
  ]
  return (
    <fieldset className="tof-crane-kind">
      <legend>{t('stopEditor.crane.kindLegend')}</legend>
      <div className="tof-crane-kind-options">
        {options.map((option) => (
          <label key={option.value}>
            <input
              type="radio"
              name={`${idPrefix}-crane-kind`}
              value={option.value}
              // An order that predates the choice (None) is a classic transport with crane.
              checked={option.value === 'OnSiteLifting' ? kind === 'OnSiteLifting' : kind !== 'OnSiteLifting'}
              onChange={() => onChange(option.value)}
              disabled={disabled}
            />
            {option.label}
          </label>
        ))}
      </div>
    </fieldset>
  )
}

interface OnSiteWorkFieldsProps {
  idPrefix: string
  job: CraneJobFormValues
  onChange: (patch: Partial<CraneJobFormValues>) => void
  disabled?: boolean
  /** Inline validation message of the (required) work description. */
  descriptionError?: string
  /** Rendered next to the description (the intake's planned-duration input, or a read-only duration). */
  duration?: ReactNode
}

/**
 * The work of an on-site lifting job: a REQUIRED description and an optional, collapsible block
 * of technical lift data. That block describes the load to LIFT — it is never goods to transport,
 * which is why none of it lives in the goods section.
 */
export function OnSiteWorkFields({ idPrefix, job, onChange, disabled, descriptionError, duration }: OnSiteWorkFieldsProps) {
  const { t } = useLocale()
  const hasLiftData = [
    job.liftLoadWeightKg, job.liftLoadDimensions, job.liftRadiusMeters, job.liftHeightMeters, job.liftConditions, job.liftEquipment,
  ].some((value) => value.trim() !== '')
  const textInput = (field: keyof CraneJobFormValues, label: string, maxLength: number) => (
    <FormField label={label} htmlFor={`${idPrefix}-${field}`}>
      <input
        id={`${idPrefix}-${field}`}
        value={job[field]}
        onChange={(event) => onChange({ [field]: event.target.value })}
        disabled={disabled}
        maxLength={maxLength}
      />
    </FormField>
  )
  const numberInput = (field: keyof CraneJobFormValues, label: string) => (
    <FormField label={label} htmlFor={`${idPrefix}-${field}`}>
      <input
        id={`${idPrefix}-${field}`}
        type="number"
        min={0}
        step="any"
        value={job[field]}
        onChange={(event) => onChange({ [field]: event.target.value })}
        disabled={disabled}
      />
    </FormField>
  )

  return (
    <div className="tof-onsite">
      <FormField
        label={t('stopEditor.crane.workDescription')}
        htmlFor={`${idPrefix}-workDescription`}
        hint={t('stopEditor.crane.workDescriptionHint')}
        error={descriptionError}
        required
      >
        <textarea
          id={`${idPrefix}-workDescription`}
          rows={3}
          value={job.workDescription}
          onChange={(event) => onChange({ workDescription: event.target.value })}
          disabled={disabled}
          maxLength={2000}
          aria-invalid={descriptionError ? true : undefined}
        />
      </FormField>
      {duration}
      <details className="tof-onsite-lift" open={hasLiftData}>
        <summary>{t('stopEditor.crane.liftTitle')}</summary>
        <p className="tof-onsite-lift-note">{t('stopEditor.crane.liftNote')}</p>
        <div className="tof-onsite-grid">
          {numberInput('liftLoadWeightKg', t('stopEditor.crane.liftWeight'))}
          {textInput('liftLoadDimensions', t('stopEditor.crane.liftDimensions'), 200)}
          {numberInput('liftRadiusMeters', t('stopEditor.crane.liftRadius'))}
          {numberInput('liftHeightMeters', t('stopEditor.crane.liftHeight'))}
          {textInput('liftConditions', t('stopEditor.crane.liftConditions'), 1000)}
          {textInput('liftEquipment', t('stopEditor.crane.liftEquipment'), 1000)}
        </div>
      </details>
    </div>
  )
}
