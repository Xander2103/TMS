/**
 * Periodekeuze van "Mijn uren" (vandaag / deze week / deze maand) → een from/to ISO-datumbereik.
 * Eigen module (react-refresh: een componentbestand exporteert alleen componenten); direct
 * unit-getest in myTimePage.test.tsx.
 */
export type Period = 'today' | 'week' | 'month'

function toIso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function periodRange(period: Period, now = new Date()): { from: string; to: string } {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  if (period === 'today') {
    return { from: toIso(today), to: toIso(today) }
  }
  if (period === 'week') {
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))
    return { from: toIso(monday), to: toIso(today) }
  }
  return { from: toIso(new Date(today.getFullYear(), today.getMonth(), 1)), to: toIso(today) }
}
