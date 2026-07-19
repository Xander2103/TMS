export interface PortalModuleDef {
  to: string
  icon: string
  label: string
  description: string
  /** Any-of permission gate; empty = every signed-in employee. */
  permissions: string[]
}

/**
 * One role-based launcher for the future mobile app: the same account sees exactly the
 * modules its permissions allow — driver, warehouse and office profiles all land here.
 */
export const PORTAL_MODULES: PortalModuleDef[] = [
  { to: '/my-trips', icon: '🚚', label: 'Mijn ritten', description: 'Stops, scannen, POD en foto’s', permissions: ['driver_workflow.view'] },
  { to: '/my-trips', icon: '📦', label: 'Scannen & laden', description: 'Laadlijsten en pakketten scannen', permissions: ['scanning.execute'] },
  { to: '/exceptions', icon: '⚠️', label: 'Afwijkingen', description: 'Schade of problemen melden en opvolgen', permissions: ['exceptions.view', 'exceptions.create'] },
  { to: '/portal/planning', icon: '🗓', label: 'Mijn planning', description: 'Shifts, ritten en afwezigheden', permissions: [] },
  { to: '/portal/absences', icon: '🏖', label: 'Verlof', description: 'Aanvragen en opvolgen', permissions: [] },
  { to: '/portal/qualifications', icon: '🪪', label: 'Kwalificaties', description: 'Documenten en vervaldata', permissions: [] },
  { to: '/notifications', icon: '🔔', label: 'Meldingen', description: 'Persoonlijke berichten', permissions: [] },
  { to: '/portal/profile', icon: '👤', label: 'Mijn profiel', description: 'Gegevens en wachtwoord', permissions: [] },
]

export function visibleModules(
  modules: PortalModuleDef[],
  hasAnyPermission: (codes: string[]) => boolean,
): PortalModuleDef[] {
  return modules.filter((module) => module.permissions.length === 0 || hasAnyPermission(module.permissions))
}
