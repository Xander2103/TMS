import type { DossierSectionId } from './sectionRegistry'

/**
 * Redesign 2026-09-11: the dossier is a navigation-based workspace. The URL segment after the
 * dossier id selects ONE subsection; the shell (header, attention strip, subnav, dossier state)
 * stays mounted while the user moves between them.
 */
export const DOSSIER_TABS = ['overzicht', 'activiteiten', 'route', 'goederen', 'prijs', 'documenten', 'historiek'] as const

export type DossierTab = (typeof DOSSIER_TABS)[number]

export const DOSSIER_TAB_LABEL_KEYS: Record<DossierTab, string> = {
  overzicht: 'dossiers.nav.overview',
  activiteiten: 'dossiers.nav.activities',
  route: 'dossiers.nav.route',
  goederen: 'dossiers.nav.goods',
  prijs: 'dossiers.nav.price',
  documenten: 'dossiers.nav.documents',
  historiek: 'dossiers.nav.history',
}

/** Which tab hosts a section of the registry (readiness sections + the collapsed compat ids). */
export const TAB_FOR_SECTION: Record<DossierSectionId, DossierTab> = {
  algemeen: 'overzicht',
  activiteiten: 'activiteiten',
  route: 'route',
  goederen: 'goederen',
  prijs: 'prijs',
  documenten: 'documenten',
  notities: 'historiek',
  meer: 'historiek',
}

export function isDossierTab(value: string | undefined): value is DossierTab {
  return value !== undefined && (DOSSIER_TABS as readonly string[]).includes(value)
}

/** Canonical path of a subsection; the overview is the dossier root (no trailing segment). */
export function dossierTabPath(dossierId: string, tab: DossierTab): string {
  return tab === 'overzicht' ? `/dossiers/${dossierId}` : `/dossiers/${dossierId}/${tab}`
}
