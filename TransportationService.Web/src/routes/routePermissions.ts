import { getNavModules, type NavItem } from '../components/layout/nav/navConfig'

function collectNavPermissions(): Map<string, string[] | undefined> {
  const byPath = new Map<string, string[] | undefined>()
  const visit = (items: NavItem[]) => {
    for (const item of items) {
      byPath.set(item.to, item.permissions)
      if (item.children) visit(item.children)
    }
  }
  for (const module of getNavModules()) {
    visit(module.items ?? [])
    for (const subgroup of module.subgroups ?? []) visit(subgroup.items)
  }
  return byPath
}

const NAV_PERMISSIONS = collectNavPermissions()

/**
 * The any-of permissions the sidebar entry for `path` requires — the sidebar (navConfig) stays the
 * single source of truth, so a page is guarded by exactly the rights that make it visible in the
 * menu. Throws on an unknown path so a renamed menu entry cannot silently unguard a route.
 */
export function navPermissions(path: string): string[] {
  const permissions = NAV_PERMISSIONS.get(path)
  if (!permissions) {
    throw new Error(`navPermissions: no nav entry with permissions for "${path}"`)
  }
  return permissions
}
