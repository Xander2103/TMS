import { apiClient } from '../../../api/apiClient'

/**
 * Mirrors `HrReminderSettingsDto`
 * (TransportationService.Api/Modules/Hr/Services/HrReminderConfigService.cs).
 * One row per tenant, lazily created with defaults on first GET.
 */
export interface HrReminderSettings {
  birthdayEnabled: boolean
  /** Days before the birthday to notify (0 = on the day itself). Server clamps to 0-60. */
  birthdayDaysBefore: number
  birthdayEmailEnabled: boolean
  /** CSV of role template codes that receive the in-app birthday notification. */
  birthdayRecipientRoleCodes: string

  seniorityEnabled: boolean
  /** CSV of milestone years, e.g. "1,10,15,20,25,30". */
  seniorityMilestoneYears: string
  seniorityWarningDays: number
  seniorityEmployeeEmailEnabled: boolean

  employmentEndEnabled: boolean
  employmentEndDaysBefore: number

  /** Opvolging onvolledige dossiers. */
  dossierRemindersEnabled: boolean
  /** Days after a dossier item becomes due before HR is reminded. Server validates 1-365. */
  dossierReminderDays: number
  /** Days after a dossier item becomes due before it is escalated. Server validates 1-365, must exceed dossierReminderDays. */
  dossierEscalationDays: number
}

export type SaveHrReminderSettingsInput = HrReminderSettings

export function getHrReminderSettings(): Promise<HrReminderSettings> {
  return apiClient.getJson<HrReminderSettings>('/api/hr/reminder-settings')
}

export function updateHrReminderSettings(input: SaveHrReminderSettingsInput): Promise<HrReminderSettings> {
  return apiClient.putJson<HrReminderSettings, SaveHrReminderSettingsInput>('/api/hr/reminder-settings', input)
}
