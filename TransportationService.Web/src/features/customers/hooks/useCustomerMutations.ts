import { useRef, useState } from 'react'
import { describeApiError, type FieldErrors } from '../../../api/problemDetails'
import {
  addCustomerContact,
  createCustomer,
  deleteCustomer,
  removeCustomerContact,
  setCustomerActive,
  setCustomerBlocked,
  updateCustomer,
  updateCustomerContact,
} from '../api/customersApi'
import type { CustomerContactInput, CustomerDetail, CustomerInput, UpdateCustomerInput } from '../types'

interface UseCustomerMutationsResult {
  isSubmitting: boolean
  error: string | null
  /** Per-field backend validation messages of the last failed action. */
  fieldErrors: FieldErrors
  create: (input: CustomerInput) => Promise<CustomerDetail | null>
  update: (id: string, input: UpdateCustomerInput) => Promise<CustomerDetail | null>
  remove: (id: string) => Promise<boolean>
  setBlocked: (id: string, isBlocked: boolean, reason: string | null) => Promise<boolean>
  setActive: (id: string, isActive: boolean) => Promise<boolean>
  addContact: (customerId: string, input: CustomerContactInput) => Promise<boolean>
  updateContact: (customerId: string, contactId: string, input: CustomerContactInput) => Promise<boolean>
  removeContact: (customerId: string, contactId: string) => Promise<boolean>
}

const GENERIC_ERROR = 'De actie kon niet worden uitgevoerd.'

export function useCustomerMutations(): UseCustomerMutationsResult {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const isMounted = useRef(true)

  async function run<T>(action: () => Promise<T>, fallback: T): Promise<T> {
    setIsSubmitting(true)
    setError(null)
    setFieldErrors({})
    try {
      return await action()
    } catch (err) {
      if (isMounted.current) {
        const described = describeApiError(err, GENERIC_ERROR)
        setError(described.message)
        setFieldErrors(described.fieldErrors)
      }
      return fallback
    } finally {
      if (isMounted.current) setIsSubmitting(false)
    }
  }

  return {
    isSubmitting,
    error,
    fieldErrors,
    create: (input) => run(() => createCustomer(input), null),
    update: (id, input) => run(() => updateCustomer(id, input), null),
    remove: (id) => run(async () => (await deleteCustomer(id), true), false),
    setBlocked: (id, isBlocked, reason) => run(async () => (await setCustomerBlocked(id, isBlocked, reason), true), false),
    setActive: (id, isActive) => run(async () => (await setCustomerActive(id, isActive), true), false),
    addContact: (customerId, input) => run(async () => (await addCustomerContact(customerId, input), true), false),
    updateContact: (customerId, contactId, input) =>
      run(async () => (await updateCustomerContact(customerId, contactId, input), true), false),
    removeContact: (customerId, contactId) =>
      run(async () => (await removeCustomerContact(customerId, contactId), true), false),
  }
}
