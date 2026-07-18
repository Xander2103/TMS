import { apiClient } from '../../../api/apiClient'
import type { CreateRoleInput, Permission, Role, UpdateRoleInput } from '../types/role'

export function getRoles(): Promise<Role[]> {
  return apiClient.getJson<Role[]>('/api/roles')
}

export function getRole(id: string): Promise<Role> {
  return apiClient.getJson<Role>(`/api/roles/${id}`)
}

export function createRole(input: CreateRoleInput): Promise<Role> {
  return apiClient.postJson<Role, CreateRoleInput>('/api/roles', input)
}

export function updateRole(id: string, input: UpdateRoleInput): Promise<Role> {
  return apiClient.putJson<Role, UpdateRoleInput>(`/api/roles/${id}`, input)
}

export function deactivateRole(id: string): Promise<Role> {
  return apiClient.postJson<Role, Record<string, never>>(`/api/roles/${id}/deactivate`, {})
}

export function getPermissions(): Promise<Permission[]> {
  return apiClient.getJson<Permission[]>('/api/permissions')
}

export function assignRolePermissions(id: string, permissionCodes: string[]): Promise<Role> {
  return apiClient.postJson<Role, { permissionCodes: string[] }>(`/api/roles/${id}/permissions`, { permissionCodes })
}
