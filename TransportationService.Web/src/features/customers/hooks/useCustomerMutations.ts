import { useRef, useState } from 'react'
import {
  addCustomerContact,
  createCustomer,
  deleteCustomer,
  removeCustomerContact,
  setCustomerBlocked,
  updateCustomer,
  updateCustomerContact,
} from '../api/customersApi'
import type { CustomerContactInput, CustomerDetail, CustomerInput, UpdateCustomerInput } from '../types'

interface UseCustomerMutationsResult {
  isSubmitting: boolean
  error: string | null
  create: (input: CustomerInput) => Promise<CustomerDetail | null>
  update: (id: string, input: UpdateCustomerInput) => Promise<CustomerDetail | null>
  remove: (id: string) => Promise<boolean>
  setBlocked: (id: string, isBlocked: boolean, reason: string | null) => Promise<boolean>
  addContact: (customerId: string, input: CustomerContactInput) => Promise<boolean>
  updateContact: (customerId: string, contactId: string, input: CustomerContactInput) => Promise<boolean>
  removeContact: (customerId: string, contactId: string) => Promise<boolean>
}

const GENERIC_ERROR = 'De actie kon niet worden uitgevoerd.'

export function useCustomerMutations(): UseCustomerMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const isMounted = useRef(true)

  async function run<T>(action: () => Promise<T>, fallback: T): Promise<T> {
    setIsSubmitting(true)
    setError(null)
    try {
      return await action()
    } catch {
      if (isMounted.current) setError(GENERIC_ERROR)
      return fallback
    } finally {
      if (isMounted.current) setIsSubmitting(false)
    }
  }

  return {
    isSubmitting,
    error,
    create: (input) => run(() => createCustomer(input), null),
    update: (id, input) => run(() => updateCustomer(id, input), null),
    remove: (id) => run(async () => (await deleteCustomer(id), true), false),
    setBlocked: (id, isBlocked, reason) => run(async () => (await setCustomerBlocked(id, isBlocked, reason), true), false),
    addContact: (customerId, input) => run(async () => (await addCustomerContact(customerId, input), true), false),
    updateContact: (customerId, contactId, input) =>
      run(async () => (await updateCustomerContact(customerId, contactId, input), true), false),
    removeContact: (customerId, contactId) =>
      run(async () => (await removeCustomerContact(customerId, contactId), true), false),
  }
}
