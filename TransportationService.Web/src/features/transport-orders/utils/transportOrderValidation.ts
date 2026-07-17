import { TRANSPORT_ORDER_STATUS_OPTIONS, type CreateTransportOrderInput } from '../types/transportOrder'

export interface TransportOrderFormValues {
  reference: string
  customerName: string
  pickupAddress: string
  deliveryAddress: string
  status: string
}

export type TransportOrderFormErrors = Partial<Record<keyof TransportOrderFormValues, string>>

const MAX_LENGTHS = {
  reference: 50,
  customerName: 200,
  pickupAddress: 500,
  deliveryAddress: 500,
} as const

export function validateTransportOrderForm(values: TransportOrderFormValues): TransportOrderFormErrors {
  const errors: TransportOrderFormErrors = {}

  const reference = values.reference.trim()
  const customerName = values.customerName.trim()
  const pickupAddress = values.pickupAddress.trim()
  const deliveryAddress = values.deliveryAddress.trim()
  const status = values.status.trim()

  if (!reference) {
    errors.reference = 'Referentie is verplicht.'
  } else if (reference.length > MAX_LENGTHS.reference) {
    errors.reference = `Referentie mag maximaal ${MAX_LENGTHS.reference} tekens bevatten.`
  }

  if (!customerName) {
    errors.customerName = 'Klantnaam is verplicht.'
  } else if (customerName.length > MAX_LENGTHS.customerName) {
    errors.customerName = `Klantnaam mag maximaal ${MAX_LENGTHS.customerName} tekens bevatten.`
  }

  if (!pickupAddress) {
    errors.pickupAddress = 'Ophaaladres is verplicht.'
  } else if (pickupAddress.length > MAX_LENGTHS.pickupAddress) {
    errors.pickupAddress = `Ophaaladres mag maximaal ${MAX_LENGTHS.pickupAddress} tekens bevatten.`
  }

  if (!deliveryAddress) {
    errors.deliveryAddress = 'Leveringsadres is verplicht.'
  } else if (deliveryAddress.length > MAX_LENGTHS.deliveryAddress) {
    errors.deliveryAddress = `Leveringsadres mag maximaal ${MAX_LENGTHS.deliveryAddress} tekens bevatten.`
  }

  if (!status) {
    errors.status = 'Status is verplicht.'
  } else if (!TRANSPORT_ORDER_STATUS_OPTIONS.includes(status)) {
    errors.status = 'Ongeldige status.'
  }

  return errors
}

export function toCreateTransportOrderInput(values: TransportOrderFormValues): CreateTransportOrderInput {
  return {
    reference: values.reference.trim(),
    customerName: values.customerName.trim(),
    pickupAddress: values.pickupAddress.trim(),
    deliveryAddress: values.deliveryAddress.trim(),
    status: values.status.trim(),
  }
}
