import { apiClient } from '../../../api/apiClient'

/**
 * Safe predefined palette (mirrors the server-side CalendarNotePalette).
 * `label` is a translation key (portalHome.noteColours.*); render sites resolve it via t().
 */
export const NOTE_COLOURS: { value: string; label: string }[] = [
  { value: '#2563eb', label: 'portalHome.noteColours.blue' },
  { value: '#16a34a', label: 'portalHome.noteColours.green' },
  { value: '#ea580c', label: 'portalHome.noteColours.orange' },
  { value: '#9333ea', label: 'portalHome.noteColours.purple' },
  { value: '#0891b2', label: 'portalHome.noteColours.cyan' },
  { value: '#db2777', label: 'portalHome.noteColours.pink' },
  { value: '#ca8a04', label: 'portalHome.noteColours.ochre' },
  { value: '#64748b', label: 'portalHome.noteColours.grey' },
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
