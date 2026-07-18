export interface RoleSummary {
  id: string
  name: string
}

export interface User {
  id: string
  email: string
  firstName: string
  lastName: string
  employeeId: string | null
  customerId: string | null
  isActive: boolean
  isBlocked: boolean
  roles: RoleSummary[]
}

export interface CreateUserInput {
  email: string
  firstName: string
  lastName: string
  employeeId: string | null
  customerId: string | null
  roleIds: string[]
}

export interface UpdateUserInput {
  firstName: string
  lastName: string
  employeeId: string | null
  customerId: string | null
}
