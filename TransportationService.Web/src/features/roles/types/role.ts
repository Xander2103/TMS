export interface Role {
  id: string
  name: string
  description: string | null
  isSystemRole: boolean
  isActive: boolean
  permissionCodes: string[]
}

export interface Permission {
  id: string
  code: string
  module: string
  action: string
  description: string
}

export interface CreateRoleInput {
  name: string
  description: string | null
}

export interface UpdateRoleInput {
  name: string
  description: string | null
}
