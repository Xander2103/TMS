// Second seeding pass: invoices in every state, a driver standing at an unloading address, a new employee without an account,
// tidier planning times. Separate from seed-demo.mjs because it builds on its result.
import { api, ok, sql, ids, saveIds, day } from './lib.mjs'
const D = ids.demo, d = day(0)
// Chris: unloading stop late, so Live opvolging shows a real delay.
sql(`update transport_order_stops set "RequestedFrom"=now()-interval '4 hours', "RequestedTo"=now()-interval '1 hours' where "TransportOrderId" in (select "TransportOrderId" from trip_orders where "TripId"='${D.tripChris}') and "StopType"='Unloading'`)
await api('POST', `/api/trips/${D.tripChris}/eta/recalculate`, undefined, 'dispatcher')
// Bart: at the unloading address of the first order.
const ex = await ok('GET', `/api/trips/${D.tripBart}/execution`, undefined, 'chauffeur')
for (const s of ex.stops) if (s.stopType === 'Loading' && s.status !== 'Completed') for (const st of ['Arrived', 'Loading', 'Loaded', 'Completed']) await ok('POST', `/api/trips/${D.tripBart}/stops/${s.transportOrderStopId}/transition`, { toStatus: st, reason: null }, 'chauffeur')
const un = ex.stops.find((s) => s.stopType === 'Unloading')
await ok('POST', `/api/trips/${D.tripBart}/stops/${un.transportOrderStopId}/transition`, { toStatus: 'Arrived', reason: null }, 'chauffeur')
// Concept trip that shows a blocking conflict: damaged vehicle.
sql(`update trips set "VehicleId"='${ids.vehicle.second}', "DriverId"='${ids.driver.chauffeurb}' where "Id"='${D.tripConcept}'`)
// Spread today's trips over the day on the board.
const hours = { 'RIT-0006': [6, 8], 'RIT-0007': [7, 9], 'RIT-0008': [8, 10], 'RIT-0009': [9, 11], 'RIT-0010': [10, 12], 'RIT-0011': [12, 14], 'RIT-0012': [13, 17], 'RIT-0013': [14, 18], 'RIT-0014': [7, 12] }
for (const [no, [a, b]] of Object.entries(hours)) sql(`update trips set "PlannedStart"="TripDate"::timestamp + interval '${a - 1} hours', "PlannedEnd"="TripDate"::timestamp + interval '${b - 1} hours' where "TripNumber"='${no}' and "TenantId"=(select "Id" from tenants where "Slug"='noorderkempen')`)
// Test leftovers sink to the bottom of the dossier list.
sql(`update transport_dossiers set "DossierDate"='2026-10-01' where "Status"='Cancelled'`)
// Invoices
const inv = (body) => ok('POST', '/api/invoices', body, 'boekhouding')
const clean = await inv({ customerId: ids.customerB, orderIds: [D.finishedPriced2], manualLines: [], notes: null }); D.invoiceDraft = clean.id
const blocked = await inv({ customerId: ids.customerA, orderIds: [D.finishedPriced], manualLines: [], notes: null }); D.invoiceBlocked = blocked.id
// A new employee without an account, for the account step.
const e = await ok('POST', '/api/employees', { firstName: 'Nora', lastName: 'Nieuwkomer', employmentStatus: 'Active' }, 'hr').catch((x) => { console.log(x.message); return null }); if (e) D.employeeNew = e.id
saveIds(); console.log(JSON.stringify({ clean: clean.id, blocked: blocked.id, emp: e?.id }))
