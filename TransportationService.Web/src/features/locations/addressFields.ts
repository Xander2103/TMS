/**
 * The address record a stop takes its fields from (picker result, quick-created address or a
 * location detail) and the small text helpers shared by the address search, the stop editor and
 * the duplicate check. One shape, so "select an existing address" fills the same fields wherever
 * it happens.
 */
export interface PickedAddress {
  locationId: string
  /** The location's own NAME ("Novellini") — never the whole address line. */
  name: string
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
}

/** "Avenue Sabin 1" — street + house number, the single address line a stop stores. */
export function streetLine(address: Pick<PickedAddress, 'street' | 'houseNumber'>): string {
  return [address.street, address.houseNumber].filter(Boolean).join(' ')
}

/** "Avenue Sabin 1, 1300 Waver" — empty parts are skipped. */
export function fullAddressLine(address: Pick<PickedAddress, 'street' | 'houseNumber' | 'postalCode' | 'city'>): string {
  const cityPart = [address.postalCode, address.city].filter(Boolean).join(' ')
  return [streetLine(address), cityPart].filter(Boolean).join(', ')
}

/**
 * "Avenue Sabin 1" → street "Avenue Sabin", house number "1" (also "12A", "12 bus 3"). The house
 * number is everything from the LAST whitespace-separated token that starts with a digit; a line
 * without such a token is all street. Only used to feed the duplicate check, which compares
 * street and number separately — the stop itself keeps the line exactly as typed.
 */
export function splitStreetLine(line: string): { street: string | null; houseNumber: string | null } {
  const tokens = line.trim().split(/\s+/).filter(Boolean)
  if (tokens.length === 0) return { street: null, houseNumber: null }
  let numberStart = -1
  for (let i = tokens.length - 1; i >= 1; i--) {
    if (/^\d/.test(tokens[i])) numberStart = i
    else if (numberStart !== -1 && !/^(bus|bte|box|b)$/i.test(tokens[i])) break
  }
  if (numberStart === -1) return { street: tokens.join(' '), houseNumber: null }
  return { street: tokens.slice(0, numberStart).join(' '), houseNumber: tokens.slice(numberStart).join(' ') }
}
