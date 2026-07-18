import { apiClient } from '../../../api/apiClient'
import { getRoles } from '../../roles/api/rolesApi'
import type { CreateUserInput, UpdateUserInput, User } from '../types/user'

export function getUsers(): Promise<User[]> {
  return apiClient.getJson<User[]>('/api/users')
}

export function getUser(id: string): Promise<User> {
  return apiClient.getJson<User>(`/api/users/${id}`)
}

export function createUser(input: CreateUserInput): Promise<User> {
  return apiClient.postJson<User, CreateUserInput>('/api/users', input)
}

export function updateUser(id: string, input: UpdateUserInput): Promise<User> {
  return apiClient.putJson<User, UpdateUserInput>(`/api/users/${id}`, input)
}

export function setUserActive(id: string, isActive: boolean): Promise<User> {
  return apiClient.patchJson<User, { isActive: boolean }>(`/api/users/${id}/active`, { isActive })
}

export function setUserBlocked(id: string, isBlocked: boolean): Promise<User> {
  return apiClient.patchJson<User, { isBlocked: boolean }>(`/api/users/${id}/blocked`, { isBlocked })
}

export function assignUserRoles(id: string, roleIds: string[]): Promise<User> {
  return apiClient.postJson<User, { roleIds: string[] }>(`/api/users/${id}/roles`, { roleIds })
}

export { getRoles }
