import { beforeEach, describe, expect, it } from 'vitest'
import {
  defaultSelection, effectiveStatuses, hasAnyFilter, hasGroupFilters, periodEnd, readLastUsedFilters,
  selectionFromSearchParams, selectionToSearchParams, urlHasFilters, writeLastUsedFilters,
} from '../planningFilters'

const TODAY = new Date('2026-10-07T10:00:00')
const A = '11111111-1111-1111-1111-111111111111'
const B = '22222222-2222-2222-2222-222222222222'

describe('planningFilters', () => {
  beforeEach(() => window.localStorage.clear())

  it('defaults to the Monday of the current week, one week, no filters, grid view', () => {
    const selection = defaultSelection(TODAY)
    expect(selection.weekStart).toBe('2026-10-05')
    expect(selection.weeks).toBe(1)
    expect(hasAnyFilter(selection)).toBe(false)
    expect(hasGroupFilters(selection)).toBe(false)
    expect(effectiveStatuses(selection.statuses)).toEqual(['Active'])
    expect(periodEnd('2026-10-05', 2)).toBe('2026-10-18')
  })

  it('round-trips the whole selection through the URL with stable ids only', () => {
    const selection = {
      ...defaultSelection(TODAY),
      weekStart: '2026-10-12',
      weeks: 2 as const,
      departmentIds: [A],
      jobFunctionIds: [A, B],
      contractTypeIds: [B],
      statuses: ['Active', 'Inactive'] as const,
      driversOnly: true,
      employeeIds: [B],
      listView: true,
    }
    const params = selectionToSearchParams({ ...selection, statuses: [...selection.statuses] }, TODAY)
    expect(params.toString()).toBe(
      `week=2026-10-12&weeks=2&departments=${A}&functions=${A}%2C${B}&contracts=${B}&status=active%2Cinactive&drivers=1&employees=${B}&view=list`,
    )
    expect(selectionFromSearchParams(params, TODAY)).toEqual({ ...selection, statuses: ['Active', 'Inactive'] })
  })

  it('writes nothing for the default selection and ignores junk in the URL', () => {
    expect(selectionToSearchParams(defaultSelection(TODAY), TODAY).toString()).toBe('')
    const junk = new URLSearchParams('week=not-a-date&weeks=9&departments=foo,bar&status=sepia&employees=' + A + ',' + A)
    const selection = selectionFromSearchParams(junk, TODAY)
    expect(selection.weekStart).toBe('2026-10-05')
    expect(selection.weeks).toBe(1)
    expect(selection.departmentIds).toEqual([])
    expect(selection.statuses).toEqual([])
    expect(selection.employeeIds).toEqual([A]) // de-duplicated
  })

  it('snaps a mid-week date in the URL to its Monday', () => {
    expect(selectionFromSearchParams(new URLSearchParams('week=2026-10-08'), TODAY).weekStart).toBe('2026-10-05')
  })

  it('detects filter parameters so stored filters never override a shared link', () => {
    expect(urlHasFilters(new URLSearchParams('week=2026-10-12'))).toBe(false)
    expect(urlHasFilters(new URLSearchParams(`employees=${A}`))).toBe(true)
    expect(urlHasFilters(new URLSearchParams('status=inactive'))).toBe(true)
  })

  it('remembers group filters per user, never the week or the explicit picks', () => {
    writeLastUsedFilters('user-1', {
      departmentIds: [A], jobFunctionIds: [], contractTypeIds: [B], statuses: ['Inactive'], driversOnly: true,
    })
    expect(readLastUsedFilters('user-1')).toEqual({
      departmentIds: [A], jobFunctionIds: [], contractTypeIds: [B], statuses: ['Inactive'], driversOnly: true,
    })
    expect(readLastUsedFilters('user-2')).toBeNull()
    expect(readLastUsedFilters(null)).toBeNull()
    window.localStorage.setItem('ts.employeePlanning.filters:user-3', '{"departmentIds":["nope"],"statuses":["Weird"]}')
    expect(readLastUsedFilters('user-3')).toEqual({ departmentIds: [], jobFunctionIds: [], contractTypeIds: [], statuses: [], driversOnly: false })
  })
})
