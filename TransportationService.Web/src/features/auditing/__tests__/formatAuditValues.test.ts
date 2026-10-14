import { describe, expect, it } from 'vitest'
import { auditFieldLabel, formatAuditValues, isSensitiveAuditKey } from '../formatAuditValues'

describe('formatAuditValues (B32: humanized, never a raw code)', () => {
  it('labels known fields in Dutch and renders booleans as ja/nee', () => {
    expect(formatAuditValues('{"Name":"Depot Noord","IsActive":true}')).toBe('Naam: Depot Noord · Actief: ja')
  })

  it('renders null and missing values as an em dash', () => {
    expect(formatAuditValues('{"Note":null}')).toBe('Notitie: —')
  })

  it('shows a GUID as a reference and an enum code as words', () => {
    expect(
      formatAuditValues('{"ActivityType":"HERLEVERING","Label":"Herlevering naar eindklant","LinkedTransportOrderId":"631a45c4-49c0-4cb8-9821-ce1e6a3afdc5","Template":"DIRECT_TRANSPORT"}'),
    ).toBe('Activiteitstype: Herlevering · Label: Herlevering naar eindklant · Transportopdracht: verwijzing · Sjabloon: Direct transport')
  })

  it('formats dates in the tenant notation and sentence-cases unknown property names', () => {
    expect(formatAuditValues('{"DossierDate":"2026-10-15","PlannedEmptyKm":25}')).toBe('Dossierdatum: 15/10/2026 · Planned empty km: 25')
    expect(auditFieldLabel('CustomerId')).toBe('Klant')
    expect(auditFieldLabel('SomeOtherThingId')).toBe('Some other thing')
  })

  it('lets an entity map its own enum ordinals', () => {
    const labels = { Type: (v: unknown) => (v === 11 ? 'Gedeeltelijke levering' : undefined), Severity: (v: unknown) => (v === 1 ? 'Middel' : undefined) }
    expect(formatAuditValues('{"Type":11,"Severity":1,"Description":"2 van 18 geweigerd"}', labels)).toBe(
      'Type: Gedeeltelijke levering · Ernst: Middel · Omschrijving: 2 van 18 geweigerd',
    )
  })

  it('masks confidential identifiers even when the backend stored them partially masked', () => {
    const formatted = formatAuditValues('{"NationalRegisterNumber":"93••••••21","Iban":"BE68••••7034","Name":"Jan"}')
    expect(formatted).toBe('National register number: •••• · Iban: •••• · Naam: Jan')
  })

  it('masks credential-shaped keys regardless of casing or separators', () => {
    expect(formatAuditValues('{"passwordHash":"x","refresh_token":"y","SecurityStamp":"z"}')).toBe(
      'Password hash: •••• · Refresh token: •••• · Security stamp: ••••',
    )
  })

  it('does not mask empty sensitive values (nothing to hide, keeps the diff readable)', () => {
    expect(formatAuditValues('{"Iban":null}')).toBe('Iban: —')
  })

  it('returns raw text for non-JSON payloads', () => {
    expect(formatAuditValues('legacy free text')).toBe('legacy free text')
  })

  it('returns an empty string for null input', () => {
    expect(formatAuditValues(null)).toBe('')
  })
})

describe('isSensitiveAuditKey', () => {
  it.each(['Iban', 'BIC', 'nationalRegisterNumber', 'Identity_Card_Number', 'ApiSecret', 'AccessToken'])(
    'flags %s',
    (key) => {
      expect(isSensitiveAuditKey(key)).toBe(true)
    },
  )

  it.each(['Name', 'Status', 'Amount', 'Tokens'])('passes %s through', (key) => {
    // "Tokens" DOES match the token pattern — document the deliberately-broad net.
    expect(isSensitiveAuditKey(key)).toBe(key === 'Tokens')
  })
})

// ---------------------------------------------------------------------------
// B25: field-level changes and one-line summaries
// ---------------------------------------------------------------------------
import { auditChanges, auditSummary } from '../formatAuditValues'

describe('auditChanges / auditSummary (B25)', () => {
  const orderId = '631a45c4-49c0-4c1e-8a7b-0d3b5c2e9f10'

  it('resolves ids to human references and never shows a GUID', () => {
    const changes = auditChanges(null, `{"LinkedTransportOrderId":"${orderId}","Label":"Herlevering"}`, { references: { [orderId]: 'ORD-0024' } })
    expect(changes).toEqual([
      { key: 'LinkedTransportOrderId', field: 'Transportopdracht', before: null, after: 'ORD-0024' },
      { key: 'Label', field: 'Label', before: null, after: 'Herlevering' },
    ])
    expect(JSON.stringify(changes)).not.toContain(orderId)
    // An id that cannot be resolved stays a neutral word.
    expect(auditChanges(null, `{"TripId":"${orderId}"}`)[0].after).toBe('verwijzing')
  })

  it('reads lifecycle values in Dutch and leaves unchanged fields out', () => {
    const changes = auditChanges('{"Status":"Planned","Notes":"x"}', '{"Status":"InProgress","Notes":"x"}')
    expect(changes).toEqual([{ key: 'Status', field: 'Status', before: 'Gepland', after: 'Onderweg' }])
    expect(auditSummary(changes)).toBe('Status gewijzigd van Gepland naar Onderweg')
  })

  it('summarises a creation by what was recorded, naming value first', () => {
    const changes = auditChanges(null, '{"DurationHours":2,"Label":"Plateau","PlannedDate":"2026-10-14","Notes":"a","Reason":null}')
    expect(auditSummary(changes)).toMatch(/^Label: Plateau · Duur \(uren\): 2 · Geplande datum: .+ \+1$/)
  })

  it('leads with the status move when other fields changed with it', () => {
    const changes = auditChanges('{"Status":"Planned","Notes":"x"}', '{"Status":"InProgress","Notes":"y"}')
    expect(auditSummary(changes)).toBe('Status gewijzigd van Gepland naar Onderweg · ook: Notities')
  })

  it('lists the changed fields of an ordinary edit', () => {
    const changes = auditChanges('{"Label":"A","DurationHours":1}', '{"Label":"B","DurationHours":2}')
    expect(auditSummary(changes)).toBe('Label: B · gewijzigd: Label, Duur (uren)')
  })

  it('masks sensitive values in the changes too', () => {
    expect(auditChanges('{"Iban":"BE68"}', '{"Iban":"BE99"}')[0]).toMatchObject({ before: '••••', after: '••••' })
  })
})
