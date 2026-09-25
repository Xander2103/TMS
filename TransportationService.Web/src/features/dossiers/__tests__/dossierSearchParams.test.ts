import { describe, expect, it } from 'vitest'
import {
  activeFilterKeys,
  EMPTY_DOSSIER_FILTERS,
  parseDossierSearchParams,
  toSearchDossiersParams,
  toSearchParams,
  todayRange,
  weekRangeOf,
} from '../pages/dossierSearchParams'

describe('parseDossierSearchParams', () => {
  it('leest een lege querystring als de standaardfilters (pagina 1, geen sortering)', () => {
    expect(parseDossierSearchParams(new URLSearchParams())).toEqual(EMPTY_DOSSIER_FILTERS)
  })

  it('leest alle ondersteunde filters, sortering en pagina uit de URL', () => {
    const parsed = parseDossierSearchParams(
      new URLSearchParams(
        'search=nexans&status=Closed&customerId=c-1&dateFrom=2026-09-01&dateTo=2026-09-30&dossierNumber=DOS-1' +
          '&orderNumber=ORD-1&customerReference=REF&customerNumber=K-1&confirmationSource=Manual&confirmedFrom=2026-09-02' +
          '&confirmedTo=2026-09-03&createdFrom=2026-08-01&createdTo=2026-08-31&planningFrom=2026-09-05&planningTo=2026-09-06' +
          '&driverId=dr-1&vehicleId=v-1&licensePlate=1-ABC-123&trailerId=tr-1&activityTypeId=at-1&loadingCity=Antwerpen' +
          '&unloadingCity=Gent&postalCode=2000&countryCode=BE&priceStatus=Partial&invoiceStatus=Sent&hasCmr=true' +
          '&sort=number&dir=asc&page=2',
      ),
    )

    expect(parsed).toEqual({
      search: 'nexans',
      status: 'Closed',
      customerId: 'c-1',
      dateFrom: '2026-09-01',
      dateTo: '2026-09-30',
      dossierNumber: 'DOS-1',
      orderNumber: 'ORD-1',
      customerReference: 'REF',
      customerNumber: 'K-1',
      confirmationSource: 'Manual',
      confirmedFrom: '2026-09-02',
      confirmedTo: '2026-09-03',
      createdFrom: '2026-08-01',
      createdTo: '2026-08-31',
      planningFrom: '2026-09-05',
      planningTo: '2026-09-06',
      driverId: 'dr-1',
      vehicleId: 'v-1',
      licensePlate: '1-ABC-123',
      trailerId: 'tr-1',
      activityTypeId: 'at-1',
      loadingCity: 'Antwerpen',
      unloadingCity: 'Gent',
      postalCode: '2000',
      countryCode: 'BE',
      priceStatus: 'Partial',
      invoiceStatus: 'Sent',
      hasCmr: 'true',
      sort: 'number',
      dir: 'asc',
      page: 2,
    })
  })

  it('negeert ongeldige enumwaarden, sorteersleutels en pagina’s', () => {
    const parsed = parseDossierSearchParams(
      new URLSearchParams('status=Weird&confirmationSource=x&priceStatus=y&invoiceStatus=z&hasCmr=maybe&sort=title&dir=up&page=-3'),
    )
    expect(parsed.status).toBe('')
    expect(parsed.confirmationSource).toBe('')
    expect(parsed.priceStatus).toBe('')
    expect(parsed.invoiceStatus).toBe('')
    expect(parsed.hasCmr).toBe('')
    expect(parsed.sort).toBe('')
    expect(parsed.dir).toBe('')
    expect(parsed.page).toBe(1)
    expect(parseDossierSearchParams(new URLSearchParams('page=abc')).page).toBe(1)
    expect(parseDossierSearchParams(new URLSearchParams('page=2.7')).page).toBe(1)
  })
})

describe('toSearchParams', () => {
  it('laat lege filters en pagina 1 weg', () => {
    expect(toSearchParams(EMPTY_DOSSIER_FILTERS).toString()).toBe('')
  })

  it('schrijft gevulde filters, sortering en pagina > 1 in een stabiele volgorde', () => {
    const params = toSearchParams({ ...EMPTY_DOSSIER_FILTERS, page: 3, sort: 'date', dir: 'desc', status: 'Open', search: 'x' })
    expect(params.toString()).toBe('search=x&status=Open&sort=date&dir=desc&page=3')
  })

  it('is de inverse van parseDossierSearchParams', () => {
    const filters = { ...EMPTY_DOSSIER_FILTERS, customerId: 'c-1', hasCmr: 'false' as const, page: 2, loadingCity: 'Antwerpen' }
    expect(parseDossierSearchParams(toSearchParams(filters))).toEqual(filters)
  })
})

describe('toSearchDossiersParams', () => {
  it('zet filters om naar de API-parameters met hasCmr als boolean en zonder lege waarden', () => {
    const params = toSearchDossiersParams(
      { ...EMPTY_DOSSIER_FILTERS, status: 'Closed', hasCmr: 'true', sort: 'customer', dir: 'asc', page: 2, search: 'q' },
      25,
    )
    expect(params).toEqual({ search: 'q', status: 'Closed', hasCmr: true, sort: 'customer', dir: 'asc', page: 2, pageSize: 25 })
  })

  it('geeft hasCmr=false door als boolean', () => {
    expect(toSearchDossiersParams({ ...EMPTY_DOSSIER_FILTERS, hasCmr: 'false' }, 25).hasCmr).toBe(false)
  })
})

describe('activeFilterKeys', () => {
  it('somt enkel gevulde filters op — zoekterm, sortering en pagina tellen niet mee', () => {
    expect(activeFilterKeys({ ...EMPTY_DOSSIER_FILTERS, search: 'q', sort: 'number', dir: 'asc', page: 4 })).toEqual([])
    expect(activeFilterKeys({ ...EMPTY_DOSSIER_FILTERS, status: 'Open', postalCode: '2000' })).toEqual(['status', 'postalCode'])
  })
})

describe('datumbereiken', () => {
  it('todayRange geeft de lokale dag als van/tot', () => {
    expect(todayRange(new Date(2026, 8, 23, 23, 30))).toEqual({ dateFrom: '2026-09-23', dateTo: '2026-09-23' })
  })

  it('weekRangeOf geeft maandag t/m zondag van de week van de gegeven dag (lokale tijd)', () => {
    // Wednesday 23 September 2026 → Mon 21 … Sun 27.
    expect(weekRangeOf(new Date(2026, 8, 23))).toEqual({ dateFrom: '2026-09-21', dateTo: '2026-09-27' })
    // A Sunday belongs to the week that started the previous Monday.
    expect(weekRangeOf(new Date(2026, 8, 27))).toEqual({ dateFrom: '2026-09-21', dateTo: '2026-09-27' })
    // A Monday starts its own week.
    expect(weekRangeOf(new Date(2026, 8, 28))).toEqual({ dateFrom: '2026-09-28', dateTo: '2026-10-04' })
  })
})
