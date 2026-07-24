import { apiClient } from '../../../api/apiClient'

/** Safe predefined palette (mirrors the server-side CalendarNotePalette). */
export const NOTE_COLOURS: { value: string; label: string }[] = [
  { value: '#2563eb', label: 'Blauw' },
  { value: '#16a34a', label: 'Groen' },
  { value: '#ea580c', label: 'Oranje' },
  { value: '#9333ea', label: 'Paars' },
  { value: '#0891b2', label: 'Cyaan' },
  { value: '#db2777', label: 'Roze' },
  { value: '#ca8a04', label: 'Oker' },
  { value: '#64748b', label: 'Grijs' },
]

export interface PersonalCalendarNote {
  id: string
  title: string
  description: string | null
  date: string
  startTime: string | null
  endTime: string | null
  allDay: boolean
  colour: string
}

export interface PersonalCalendarNoteInput {
  title: string
  description: string | null
  date: string
  startTime: string | null
  endTime: string | null
  allDay: boolean
  colour: string
}

export const listCalendarNotes = (from: string, to: string): Promise<PersonalCalendarNote[]> =>
  apiClient.getJson(`/api/me/calendar-notes?from=${from}&to=${to}`)
export const createCalendarNote = (input: PersonalCalendarNoteInput): Promise<PersonalCalendarNote> =>
  apiClient.postJson('/api/me/calendar-notes', input)
export const updateCalendarNote = (id: string, input: PersonalCalendarNoteInput): Promise<PersonalCalendarNote> =>
  apiClient.putJson(`/api/me/calendar-notes/${id}`, input)
export const deleteCalendarNote = (id: string): Promise<void> =>
  apiClient.deleteRequest(`/api/me/calendar-notes/${id}`)
