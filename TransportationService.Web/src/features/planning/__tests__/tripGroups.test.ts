import { describe, expect, it } from 'vitest'
import { addDaysIso, groupTrips } from '../tripGroups'
import type { TripListItem } from '../types'

function trip(id: string, tripDate: string, status: TripListItem['status']): TripListItem {
  return {
    id, tripNumber: `RIT-${id}`, tripDate, status, driverId: null, driverName: null, vehicleId: null,
    vehicleNumber: null, vehicleLicensePlate: null, trailerId: null, trailerNumber: null, orderCount: 1, blockingConflictCount: 0,
  }
}

const TODAY = '2026-10-14'

describe('groupTrips (B20)', () => {
  it('keeps a trip that started days ago and is still underway in sight, marked as started before today', () => {
    const old = trip('1', '2026-10-11', 'InProgress')
    const groups = groupTrips([], [old], [old], TODAY)
    expect(groups.running).toEqual([{ trip: old, startedBefore: true }])
    expect(groups.overdue).toEqual([])
  })

  it('lists past trips that never left as overdue', () => {
    const planned = trip('2', '2026-10-12', 'Planned')
    const draft = trip('3', '2026-10-13', 'Draft')
    const done = trip('4', '2026-10-12', 'Completed')
    expect(groupTrips([], [], [planned, draft, done], TODAY).overdue.map((t) => t.id)).toEqual(['2', '3'])
  })

  it('shows every trip once: the selected day drops trips already in Lopend and counts them', () => {
    const runningToday = trip('5', TODAY, 'InProgress')
    const plannedToday = trip('6', TODAY, 'Planned')
    const groups = groupTrips([runningToday, plannedToday], [runningToday], [], TODAY)
    expect(groups.running.map((e) => e.trip.id)).toEqual(['5'])
    expect(groups.running[0].startedBefore).toBe(false)
    expect(groups.day.map((t) => t.id)).toEqual(['6'])
    expect(groups.movedUp).toBe(1)
  })

  it('addDaysIso crosses month boundaries in the local calendar', () => {
    expect(addDaysIso('2026-10-31', 1)).toBe('2026-11-01')
    expect(addDaysIso('2026-10-01', -14)).toBe('2026-09-17')
  })
})
