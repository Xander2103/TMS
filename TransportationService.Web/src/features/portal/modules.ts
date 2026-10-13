export interface PortalModuleDef {
  to: string
  icon: string
  /** Translation key (portalHome.modules.*.label); render sites resolve it via t(). */
  label: string
  /** Translation key (portalHome.modules.*.description); render sites resolve it via t(). */
  description: string
  /** Any-of permission gate; empty = every signed-in employee. */
  permissions: string[]
}

/**
 * One role-based launcher for the future mobile app: the same account sees exactly the
 * modules its permissions allow — driver, warehouse and office profiles all land here.
 */
export const PORTAL_MODULES: PortalModuleDef[] = [
  { to: '/my-trips', icon: '🚚', label: 'portalHome.modules.myTrips.label', description: 'portalHome.modules.myTrips.description', permissions: ['driver_workflow.view'] },
  { to: '/my-trips', icon: '📦', label: 'portalHome.modules.scanning.label', description: 'portalHome.modules.scanning.description', permissions: ['scanning.execute'] },
  { to: '/exceptions', icon: '⚠️', label: 'portalHome.modules.exceptions.label', description: 'portalHome.modules.exceptions.description', permissions: ['exceptions.view', 'exceptions.create'] },
  { to: '/warehouse', icon: '🏭', label: 'portalHome.modules.warehouse.label', description: 'portalHome.modules.warehouse.description', permissions: ['warehouse.view'] },
  { to: '/tasks?mine=1', icon: '✅', label: 'portalHome.modules.myTasks.label', description: 'portalHome.modules.myTasks.description', permissions: ['tasks.view_own'] },
  { to: '/portal/time', icon: '⏱', label: 'portalHome.modules.myTime.label', description: 'portalHome.modules.myTime.description', permissions: ['attendance.self'] },
  { to: '/portal/planning', icon: '🗓', label: 'portalHome.modules.myPlanning.label', description: 'portalHome.modules.myPlanning.description', permissions: [] },
  { to: '/portal/absences', icon: '🏖', label: 'portalHome.modules.leave.label', description: 'portalHome.modules.leave.description', permissions: [] },
  { to: '/portal/qualifications', icon: '🪪', label: 'portalHome.modules.qualifications.label', description: 'portalHome.modules.qualifications.description', permissions: [] },
  { to: '/notifications', icon: '🔔', label: 'portalHome.modules.notifications.label', description: 'portalHome.modules.notifications.description', permissions: [] },
  { to: '/portal/profile', icon: '👤', label: 'portalHome.modules.myProfile.label', description: 'portalHome.modules.myProfile.description', permissions: [] },
]

export function visibleModules(
  modules: PortalModuleDef[],
  hasAnyPermission: (codes: string[]) => boolean,
): PortalModuleDef[] {
  return modules.filter((module) => module.permissions.length === 0 || hasAnyPermission(module.permissions))
}
