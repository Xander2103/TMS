import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  NOTE_COLOURS,
  createCalendarNote,
  deleteCalendarNote,
  updateCalendarNote,
  type PersonalCalendarNote,
} from '../api/calendarNotesApi'

interface PersonalNoteDialogProps {
  /** Null = create; the note's date is preselected via initialDate. */
  note: PersonalCalendarNote | null
  initialDate: string
  onSaved: () => void
  onClose: () => void
}

/** Create/edit/delete one personal calendar note with a palette-based colour choice. */
export function PersonalNoteDialog({ note, initialDate, onSaved, onClose }: PersonalNoteDialogProps) {
  const { t } = useLocale()
  const [title, setTitle] = useState(note?.title ?? '')
  const [description, setDescription] = useState(note?.description ?? '')
  const [date, setDate] = useState(note?.date ?? initialDate)
  const [allDay, setAllDay] = useState(note?.allDay ?? true)
  const [startTime, setStartTime] = useState(note?.startTime?.slice(0, 5) ?? '')
  const [endTime, setEndTime] = useState(note?.endTime?.slice(0, 5) ?? '')
  const [colour, setColour] = useState(note?.colour ?? NOTE_COLOURS[0].value)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!title.trim()) {
      setError(t('portalHome.note.titleRequired'))
      return
    }
    setBusy(true)
    try {
      const input = {
        title: title.trim(),
        description: description.trim() || null,
        date,
        allDay,
        startTime: allDay || !startTime ? null : `${startTime}:00`,
        endTime: allDay || !endTime ? null : `${endTime}:00`,
        colour,
      }
      if (note) {
        await updateCalendarNote(note.id, input)
      } else {
        await createCalendarNote(input)
      }
      onSaved()
    } catch (err) {
      setError(describeApiError(err, t('portalHome.note.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!note) return
    setBusy(true)
    try {
      await deleteCalendarNote(note.id)
      onSaved()
    } catch (err) {
      setError(describeApiError(err, t('portalHome.note.deleteFailed')).message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={note ? t('portalHome.note.editTitle', { title: note.title }) : t('portalHome.note.createTitle')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          {note && (
            <Button variant="danger" onClick={() => void handleDelete()} disabled={busy}>
              {t('ui.actions.delete')}
            </Button>
          )}
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="personal-note-form" disabled={busy}>
            {t('ui.actions.save')}
          </Button>
        </>
      }
    >
      <form id="personal-note-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="issued-items-form-error" role="alert">
            {error}
          </div>
        )}
        <FormField label={t('portalHome.note.titleField')} htmlFor="pn-title" required hint={t('portalHome.note.titleHint')}>
          <input id="pn-title" value={title} onChange={(e) => setTitle(e.target.value)} disabled={busy} maxLength={120} />
        </FormField>
        <FormField label={t('portalHome.note.descriptionField')} htmlFor="pn-desc">
          <textarea id="pn-desc" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} disabled={busy} maxLength={1000} />
        </FormField>
        <div className="issued-items-form-row">
          <FormField label={t('portalHome.note.dateField')} htmlFor="pn-date" required>
            <input id="pn-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} disabled={busy} />
          </FormField>
          <label className="tof-checkbox">
            <input type="checkbox" checked={allDay} onChange={(e) => setAllDay(e.target.checked)} disabled={busy} />
            {t('portalHome.note.allDay')}
          </label>
        </div>
        {!allDay && (
          <div className="issued-items-form-row">
            <FormField label={t('portalHome.note.fromField')} htmlFor="pn-start">
              <input id="pn-start" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label={t('portalHome.note.toField')} htmlFor="pn-end">
              <input id="pn-end" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} disabled={busy} />
            </FormField>
          </div>
        )}
        <fieldset className="portal-note-colours">
          <legend>{t('portalHome.note.colourLegend')}</legend>
          {NOTE_COLOURS.map((option) => (
            <label key={option.value} className="portal-note-colour">
              <input
                type="radio"
                name="pn-colour"
                value={option.value}
                checked={colour === option.value}
                onChange={() => setColour(option.value)}
                disabled={busy}
              />
              <span className="portal-note-swatch" style={{ background: option.value }} aria-hidden="true" />
              {t(option.label)}
            </label>
          ))}
        </fieldset>
      </form>
    </Modal>
  )
}
