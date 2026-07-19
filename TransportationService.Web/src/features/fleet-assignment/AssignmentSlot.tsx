import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/apiClient'
import { Button } from '../../components/ui/Button'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { FormField } from '../../components/ui/FormField'
import { Modal } from '../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../components/ui/SearchableSelect'
import { useToast } from '../../components/ui/toastContext'
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
        if (mounted) toast.showError('Opties konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [pickerOpen, options, loadOptions, toast])

  async function run(id: string | null, replaceExisting: boolean) {
    setBusy(true)
    try {
      await assign(id, replaceExisting)
      toast.showSuccess(id === null ? 'Toewijzing verwijderd.' : 'Toewijzing opgeslagen.')
      setPickerOpen(false)
      setReplacePrompt(null)
      setConfirmUnassign(false)
      setSelection(null)
      onChanged()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && !replaceExisting) {
        setReplacePrompt({ id, message: err.message })
      } else {
        toast.showError(err instanceof ApiError ? err.message : 'Toewijzing kon niet worden opgeslagen.')
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
          <span className="assignment-slot-empty">Niet toegewezen</span>
        )}
      </div>
      {canEdit && (
        <div className="assignment-slot-actions">
          <Button variant="ghost" onClick={() => setPickerOpen(true)} disabled={busy}>
            {assigned ? 'Wijzigen' : 'Toewijzen'}
          </Button>
          {assigned && (
            <Button variant="ghost" onClick={() => setConfirmUnassign(true)} disabled={busy}>
              Ontkoppelen
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
                Annuleren
              </Button>
              <Button onClick={() => void run(selection, false)} disabled={busy || !selection}>
                {busy ? 'Opslaan…' : 'Toewijzen'}
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
              placeholder="— Selecteer —"
            />
          </FormField>
        </Modal>
      )}

      {replacePrompt && (
        <ConfirmDialog
          title="Bestaande toewijzing vervangen?"
          message={`${replacePrompt.message} Wil je die toewijzing vervangen?`}
          confirmLabel="Vervangen"
          cancelLabel="Annuleren"
          busy={busy}
          onConfirm={() => void run(replacePrompt.id, true)}
          onCancel={() => setReplacePrompt(null)}
        />
      )}

      {confirmUnassign && (
        <ConfirmDialog
          title="Toewijzing verwijderen"
          message={`${title} ontkoppelen? Dit heeft geen invloed op geplande ritten.`}
          confirmLabel="Ontkoppelen"
          busy={busy}
          onConfirm={() => void run(null, false)}
          onCancel={() => setConfirmUnassign(false)}
        />
      )}
    </div>
  )
}
