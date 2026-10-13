import type { PlanningEmployeeOption } from './types'

/** Translation keys per weekday (Monday first). */
export const DAY_KEYS = [
  'employeePlanning.days.mon',
  'employeePlanning.days.tue',
  'employeePlanning.days.wed',
  'employeePlanning.days.thu',
  'employeePlanning.days.fri',
  'employeePlanning.days.sat',
  'employeePlanning.days.sun',
]

/** 0 = Monday … 6 = Sunday for an ISO date. */
export function dayIndexOf(date: string): number {
  return (new Date(`${date}T00:00:00`).getDay() + 6) % 7
}

export function employeeDisplayName(option: PlanningEmployeeOption): string {
  return `${option.firstName} ${option.lastName}`
}
