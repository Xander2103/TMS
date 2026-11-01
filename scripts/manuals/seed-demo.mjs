// Fills the demo database with one believable working day for the manual screenshots.
// Runs through the real API so every record obeys the product's own rules. Never run this outside the demo stack.
import { api, ok, sql, ids, saveIds, day, sleep } from './lib.mjs'
const d = day(0), SIG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='
const A = ids.customerA, B = ids.customerB
const t = (h) => `${d}T${String(h).padStart(2, '0')}:00:00Z`
const stops = (fromName, fromCity, fromPc, toName, toCity, toPc, h = 7) => [
  { stopType: 'Loading', locationName: fromName, address: 'Industrieweg 12', postalCode: fromPc, city: fromCity, countryCode: 'BE', requestedFrom: t(h), requestedTo: t(h + 3) },
  { stopType: 'Unloading', locationName: toName, address: 'Werfstraat 5', postalCode: toPc, city: toCity, countryCode: 'BE', requestedFrom: t(h + 3), requestedTo: t(h + 7) },
]
async function order(customerId, ref, goods, st, { price, status = 'Confirmed', qty = 4 } = {}) {
  const o = await ok('POST', '/api/transport-orders', { customerId, customerReference: ref, orderDate: d, goodsDescription: goods, quantity: qty, quantityUnit: 'pallets', totalWeightKg: qty * 450, stops: st }, 'planner')
  if (price) await ok('POST', `/api/transport-orders/${o.id}/pricing/one-off`, { fixedAmount: price }, 'planner')
  if (status === 'Confirmed') await ok('POST', `/api/transport-orders/${o.id}/status`, { status: 'Confirmed' }, 'planner')
  return o
}
async function trip(orderIds, driverId, vehicleId, trailerId = null, notes = null, date = d) {
  const x = await ok('POST', '/api/trips', { tripDate: date, driverId, vehicleId, trailerId, plannedStart: null, plannedEnd: null, notes, orderIds }, 'planner')
  return x.trip ?? x
}
const start = async (tr) => {
  await ok('POST', `/api/trips/${tr.id}/status`, { status: 'Planned', version: tr.version }, 'planner')
  const p = await ok('GET', `/api/trips/${tr.id}`, undefined, 'dispatcher')
  await ok('POST', `/api/trips/${tr.id}/status`, { status: 'InProgress', version: p.version }, 'dispatcher')
}
const exec = (id, who) => ok('GET', `/api/trips/${id}/execution`, undefined, who)
const mv = (id, sid, s, who, reason = null) => ok('POST', `/api/trips/${id}/stops/${sid}/transition`, { toStatus: s, reason }, who)
const stopsOf = (ex, oid, type) => ex.stops.find((s) => s.stopType === type && (!oid || s.transportOrderId === oid || s.orderId === oid)).transportOrderStopId
async function finish(orderId, who, driver, vehicle, outcome = 'ok') {
  const tr = await trip([orderId], driver, vehicle); await start(tr)
  const ex = await exec(tr.id, who); const L = stopsOf(ex, null, 'Loading'), D = stopsOf(ex, null, 'Unloading')
  for (const s of ['Arrived', 'Loading', 'Loaded', 'Completed']) await mv(tr.id, L, s, who)
  await mv(tr.id, D, 'Arrived', who)
  if (outcome === 'fail') await mv(tr.id, D, 'Failed', who, 'Niemand aanwezig op het losadres')
  else {
    await mv(tr.id, D, 'Unloading', who)
    await ok('POST', `/api/trips/${tr.id}/stops/${D}/pod`, { recipientName: 'Sara Peeters', recipientRole: 'Magazijnier', outcome: 'Complete', damageReported: false, missingReported: false, notes: null, signatureBase64: SIG, latitude: null, longitude: null, packagesAcknowledged: true }, who)
    await mv(tr.id, D, 'Completed', who)
  }
  return { tripId: tr.id, D }
}
const bart = ids.driver.chauffeur, chris = ids.driver.chauffeurb, good = ids.vehicle.good, second = ids.vehicle.second, third = ids.vehicle.third, blocked = ids.vehicle.blocked
const demo = {}
// Finished work for accounting
const c1 = await order(A, 'KS-2026-3101', 'Staalprofielen', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Bouwbedrijf Janssens', 'Hasselt', '3500'), { price: 480 }); await finish(c1.id, 'chauffeur', bart, good); demo.finishedPriced = c1.id
const c2 = await order(B, 'BB-2211', 'Snijbloemen op karren', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum De Linde', 'Gent', '9000'), { qty: 6 }); await finish(c2.id, 'chauffeur', bart, good); demo.finishedUnpriced = c2.id
const c3 = await order(B, 'BB-2212', 'Potplanten', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum Groenhof', 'Kortrijk', '8500'), { price: 265 }); await finish(c3.id, 'chauffeurb', chris, third); demo.finishedPriced2 = c3.id
// A failed delivery with its redelivery still to confirm
const f1 = await order(A, 'KS-2026-3102', 'Wapeningsnetten', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Aannemer Willems', 'Leuven', '3000'), { price: 300 }); const ft = await finish(f1.id, 'chauffeur', bart, good, 'fail')
demo.failedOrder = f1.id; demo.failedTrip = ft.tripId; demo.failedIncident = sql(`select "Id" from incidents where "SourceStopId"='${ft.D}'`)
const red = await ok('POST', `/api/incidents/${demo.failedIncident}/redelivery`, {}, 'dispatcher'); demo.redeliveryOrder = red.linkedRedeliveryOrderId
// A finished pair: the redelivery holds on Nakijken because the original carries the price
const f2 = await order(B, 'BB-2213', 'Sierheesters', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum Oosthoek', 'Aalst', '9300'), { price: 310 }); const ft2 = await finish(f2.id, 'chauffeurb', chris, third, 'fail')
const inc2 = sql(`select "Id" from incidents where "SourceStopId"='${ft2.D}'`); const red2 = await ok('POST', `/api/incidents/${inc2}/redelivery`, {}, 'dispatcher')
await ok('POST', `/api/transport-orders/${red2.linkedRedeliveryOrderId}/status`, { status: 'Confirmed' }, 'planner'); await finish(red2.linkedRedeliveryOrderId, 'chauffeurb', chris, third)
demo.pairOriginal = f2.id; demo.pairRedelivery = red2.linkedRedeliveryOrderId; demo.pairIncident = inc2
// Live trips of today. Bart: loaded, on the way. Chris: delayed, with an afwijking.
const l1 = await order(A, 'KS-2026-3103', 'Stalen balken', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Bouwwerf Noordlaan', 'Antwerpen', '2030'), { price: 520 })
const l2 = await order(B, 'BB-2214', 'Tuinaarde op paletten', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum De Linde', 'Gent', '9000', 8), { price: 240 })
const T1 = await trip([l1.id, l2.id], bart, good, ids.trailer, 'Oplegger meenemen vanaf de kaai'); await start(T1)
{ const ex = await exec(T1.id, 'chauffeur'); const L = stopsOf(ex, null, 'Loading'); for (const s of ['Arrived', 'Loading', 'Loaded', 'Completed']) await mv(T1.id, L, s, 'chauffeur') }
demo.tripBart = T1.id
const l3 = await order(A, 'KS-2026-3104', 'Hekwerk', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Gemeentewerf Mol', 'Mol', '2400', 9), { price: 180 })
const T2 = await trip([l3.id], chris, third, null, null); await start(T2)
{ const ex = await exec(T2.id, 'chauffeurb'); const L = stopsOf(ex, null, 'Loading'); await mv(T2.id, L, 'Arrived', 'chauffeurb')
  await ok('POST', `/api/trips/${T2.id}/delay`, { minutes: 45, reason: 'File op de E313' }, 'dispatcher')
  const e = await ok('POST', `/api/trips/${T2.id}/exceptions`, { type: 'DamagedPackage', severity: 'Medium', description: 'Eén palet hekwerk beschadigd bij het laden.', transportOrderStopId: L }, 'chauffeurb'); demo.exception = e.id }
demo.tripChris = T2.id
// Concept trip with a blocked vehicle (inspector shows Blokkerend)
const p1 = await order(A, 'KS-2026-3105', 'Betonijzer', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Bouwwerf Kanaalzone', 'Genk', '3600', 10), { price: 410 })
const T3 = await trip([p1.id], null, blocked, null, null, day(1)); demo.tripConcept = T3.id
// Open work on the board
for (const [c, r, g, s, o] of [
  [A, 'KS-2026-3106', 'Stalen platen', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Metaalwerken Claes', 'Turnhout', '2300', 8), { price: 350 }],
  [B, 'BB-2215', 'Kerstbomen', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum Groenhof', 'Kortrijk', '8500', 9), { price: 295, qty: 8 }],
  [A, 'KS-2026-3107', 'Buizen', stops('Kempen Staal Magazijn', 'Geel', '2440', 'Installatiebedrijf Maes', 'Lier', '2500', 11), { price: 275 }],
  [B, 'BB-2216', 'Bloembollen', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum Oosthoek', 'Aalst', '9300', 12), { price: 225, qty: 3 }],
]) await order(c, r, g, s, o)
demo.draftOrder = (await order(B, 'BB-2217', 'Vaste planten', stops('Brugge Bloem Depot', 'Brugge', '8000', 'Tuincentrum De Linde', 'Gent', '9000', 13), { status: 'Draft', qty: 2 })).id
// Portal requests: one to review, one with an open question
const portalStops = (a, b) => [{ stopType: 'Loading', locationName: a, address: 'Industrieweg 12', postalCode: '2440', city: 'Geel', countryCode: 'BE', requestedFrom: t(8), requestedTo: t(12) }, { stopType: 'Unloading', locationName: b, address: 'Kerkstraat 8', postalCode: '2800', city: 'Mechelen', countryCode: 'BE', requestedFrom: t(12), requestedTo: t(17) }]
const pr1 = await ok('POST', '/api/customer-portal/orders', { customerReference: 'KS-WEB-5001', orderDate: day(1), goodsDescription: 'Staalplaten, 6 paletten', stops: portalStops('Kempen Staal Magazijn', 'Metaalbouw Verhoeven') }, 'klantA'); demo.portalOrder = pr1.id
const pr2 = await ok('POST', '/api/customer-portal/orders', { customerReference: 'KS-WEB-5002', orderDate: day(2), goodsDescription: 'Profielen, afmeting nog te bevestigen', stops: portalStops('Kempen Staal Magazijn', 'Constructie Dewit') }, 'klantA'); demo.portalQuestion = pr2.id
await ok('POST', `/api/transport-orders/${pr2.id}/portal-review`, { action: 'RequestInfo', reason: 'Wat is de langste lengte van de profielen? We kiezen daarop de oplegger.' }, 'planner')
ids.demo = demo; saveIds(); console.log(JSON.stringify(demo, null, 1))
