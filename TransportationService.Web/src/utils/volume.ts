/**
 * Shared volume derivation for vehicles, trailers and (later) cargo — mirrors the backend
 * FleetFieldRules: volume is only computed when all three dimensions are positive; it is
 * never invented from partial data. Rounded to 3 decimals (m³).
 */
export function computeVolumeM3(
  lengthMeters: number | null,
  widthMeters: number | null,
  heightMeters: number | null,
): number | null {
  if (
    lengthMeters === null || widthMeters === null || heightMeters === null ||
    !(lengthMeters > 0) || !(widthMeters > 0) || !(heightMeters > 0)
  ) {
    return null
  }
  return Math.round(lengthMeters * widthMeters * heightMeters * 1000) / 1000
}
