import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
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
      setError('De titel is verplicht.')
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
      setError(describeApiError(err, 'De notitie kon niet worden opgeslagen.').message)
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
      setError(describeApiError(err, 'De notitie kon niet worden verwijderd.').message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={note ? `Notitie bewerken — ${note.title}` : 'Persoonlijke notitie'}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          {note && (
            <Button variant="danger" onClick={() => void handleDelete()} disabled={busy}>
              Verwijderen
            </Button>
          )}
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" form="personal-note-form" disabled={busy}>
            Opslaan
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
        <FormField label="Titel" htmlFor="pn-title" required hint="bv. Tandarts, Auto garage, Privé afspraak">
          <input id="pn-title" value={title} onChange={(e) => setTitle(e.target.value)} disabled={busy} maxLength={120} />
        </FormField>
        <FormField label="Omschrijving" htmlFor="pn-desc">
          <textarea id="pn-desc" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} disabled={busy} maxLength={1000} />
        </FormField>
        <div className="issued-items-form-row">
          <FormField label="Datum" htmlFor="pn-date" required>
            <input id="pn-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} disabled={busy} />
          </FormField>
          <label className="tof-checkbox">
            <input type="checkbox" checked={allDay} onChange={(e) => setAllDay(e.target.checked)} disabled={busy} />
            Hele dag
          </label>
        </div>
        {!allDay && (
          <div className="issued-items-form-row">
            <FormField label="Van" htmlFor="pn-start">
              <input id="pn-start" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label="Tot" htmlFor="pn-end">
              <input id="pn-end" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} disabled={busy} />
            </FormField>
          </div>
        )}
        <fieldset className="portal-note-colours">
          <legend>Kleur</legend>
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
              {option.label}
            </label>
          ))}
        </fieldset>
      </form>
    </Modal>
  )
}
