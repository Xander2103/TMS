import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../components/ui/SearchableSelect'
import { useToast } from '../../components/ui/toastContext'
import { useLocale } from '../../i18n/localeContext'
import './assignment-slot.css'

interface AssignmentSlotProps {
  /** e.g. "Vaste chauffeur" */
  title: string
  /** One-line explanation of what this slot means. */
  description: string
  assigned: { label: string; linkTo?: string } | null
  canEdit: boolean
  /** Options for the picker (loaded lazily when the dialog opens). */
  loadOptions: () => Promise<SearchableSelectOption[]>
  /**
   * Performs the assignment; throw an ApiError with status 409 to trigger the
   * confirm-replace flow (the error message is shown to the user).
   */
  assign: (id: string | null, replaceExisting: boolean) => Promise<void>
  /** Called after any successful change so the parent can reload. */
  onChanged: () => void
  pickerLabel: string
}

/**
 * One assignment slot (fixed/current driver or vehicle), shared by the vehicle and driver
 * detail pages. Handles the pick dialog, unassign confirmation and the 409 replace flow —
 * "this asset already has a holder, replace it?" — retrying with replaceExisting.
 */
export function AssignmentSlot({ title, description, assigned, canEdit, loadOptions, assign, onChanged, pickerLabel }: AssignmentSlotProps) {
  const toast = useToast()
  const { t } = useLocale()
  const [pickerOpen, setPickerOpen] = useState(false)
  const [options, setOptions] = useState<SearchableSelectOption[] | null>(null)
  const [selection, setSelection] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [replacePrompt, setReplacePrompt] = useState<{ id: string | null; message: string } | null>(null)
  const [confirmUnassign, setConfirmUnassign] = useState(false)

  useEffect(() => {
    if (!pickerOpen || options !== null) return
    let mounted = true
    loadOptions()
      .then((data) => {
        if (mounted) setOptions(data)
      })
      .catch(() => {
        if (mounted) toast.showError(t('fleet.assignmentSlot.optionsLoadFailed'))
      })
    return () => {
      mounted = false
    }
  }, [pickerOpen, options, loadOptions, toast, t])

  async function run(id: string | null, replaceExisting: boolean) {
    setBusy(true)
    try {
      await assign(id, replaceExisting)
      toast.showSuccess(id === null ? t('fleet.assignmentSlot.removed') : t('fleet.assignmentSlot.saved'))
      setPickerOpen(false)
      setReplacePrompt(null)
      setConfirmUnassign(false)
      setSelection(null)
      onChanged()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && !replaceExisting) {
        setReplacePrompt({ id, message: err.message })
      } else {
        toast.showError(err instanceof ApiError ? err.message : t('fleet.assignmentSlot.saveFailed'))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="assignment-slot">
      <div className="assignment-slot-info">
        <span className="assignment-slot-title">{title}</span>
        <span className="assignment-slot-description">{description}</span>
      </div>
      <div className="assignment-slot-value">
        {assigned ? (
          assigned.linkTo ? (
            <Link to={assigned.linkTo} className="assignment-slot-link">
              {assigned.label}
            </Link>
          ) : (
            <span>{assigned.label}</span>
          )
        ) : (
          <span className="assignment-slot-empty">{t('fleet.assignmentSlot.notAssigned')}</span>
        )}
      </div>
      {canEdit && (
        <div className="assignment-slot-actions">
          <Button variant="ghost" onClick={() => setPickerOpen(true)} disabled={busy}>
            {assigned ? t('fleet.assignmentSlot.change') : t('fleet.assignmentSlot.assign')}
          </Button>
          {assigned && (
            <Button variant="ghost" onClick={() => setConfirmUnassign(true)} disabled={busy}>
              {t('fleet.assignmentSlot.unassign')}
            </Button>
          )}
        </div>
      )}

      {pickerOpen && (
        <Modal
          title={title}
          onClose={() => setPickerOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setPickerOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={() => void run(selection, false)} disabled={busy || !selection}>
                {busy ? t('fleet.common.saving') : t('fleet.assignmentSlot.assign')}
              </Button>
            </>
          }
        >
          <FormField label={pickerLabel} htmlFor="assignment-picker">
            <SearchableSelect
              id="assignment-picker"
              value={selection}
              onChange={setSelection}
              options={options ?? []}
              isLoading={options === null}
              placeholder={t('ui.select.placeholder')}
            />
          </FormField>
        </Modal>
      )}

      {replacePrompt && (
        <ConfirmDialog
          title={t('fleet.assignmentSlot.replaceTitle')}
          message={t('fleet.assignmentSlot.replaceMessage', { message: replacePrompt.message })}
          confirmLabel={t('fleet.assignmentSlot.replaceConfirm')}
          cancelLabel={t('ui.actions.cancel')}
          busy={busy}
          onConfirm={() => void run(replacePrompt.id, true)}
          onCancel={() => setReplacePrompt(null)}
        />
      )}

      {confirmUnassign && (
        <ConfirmDialog
          title={t('fleet.assignmentSlot.unassignTitle')}
          message={t('fleet.assignmentSlot.unassignMessage', { title })}
          confirmLabel={t('fleet.assignmentSlot.unassign')}
          busy={busy}
          onConfirm={() => void run(null, false)}
          onCancel={() => setConfirmUnassign(false)}
        />
      )}
    </div>
  )
}
