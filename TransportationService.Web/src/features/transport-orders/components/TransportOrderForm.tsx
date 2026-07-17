import { useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { useCreateTransportOrder } from '../hooks/useCreateTransportOrder'
import {
  toCreateTransportOrderInput,
  validateTransportOrderForm,
  type TransportOrderFormErrors,
  type TransportOrderFormValues,
} from '../utils/transportOrderValidation'
import { TRANSPORT_ORDER_STATUS_OPTIONS } from '../types/transportOrder'
import './TransportOrderForm.css'

interface TransportOrderFormProps {
  onCreated: () => void
  secondaryAction?: ReactNode
}

const INITIAL_VALUES: TransportOrderFormValues = {
  reference: '',
  customerName: '',
  pickupAddress: '',
  deliveryAddress: '',
  status: TRANSPORT_ORDER_STATUS_OPTIONS[0],
}

export function TransportOrderForm({ onCreated, secondaryAction }: TransportOrderFormProps) {
  const [values, setValues] = useState<TransportOrderFormValues>(INITIAL_VALUES)
  const [errors, setErrors] = useState<TransportOrderFormErrors>({})
  const { isSubmitting, error, submit } = useCreateTransportOrder()

  function handleChange(event: ChangeEvent<HTMLInputElement | HTMLSelectElement>) {
    const { value } = event.target
    const field = event.target.name as keyof TransportOrderFormValues

    setValues((previous) => ({ ...previous, [field]: value }))
    setErrors((previous) => ({ ...previous, [field]: undefined }))
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isSubmitting) {
      return
    }

    const validationErrors = validateTransportOrderForm(values)
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors)
      return
    }

    const created = await submit(toCreateTransportOrderInput(values))
    if (created) {
      onCreated()
    }
  }

  return (
    <form className="transport-order-form" onSubmit={handleSubmit} noValidate>
      <div className="form-field">
        <label htmlFor="reference">Referentie</label>
        <input
          id="reference"
          name="reference"
          type="text"
          autoComplete="off"
          required
          maxLength={50}
          value={values.reference}
          onChange={handleChange}
          aria-invalid={Boolean(errors.reference)}
          aria-describedby={errors.reference ? 'reference-error' : undefined}
        />
        {errors.reference && (
          <p id="reference-error" className="field-error">
            {errors.reference}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="customerName">Klantnaam</label>
        <input
          id="customerName"
          name="customerName"
          type="text"
          autoComplete="organization"
          required
          maxLength={200}
          value={values.customerName}
          onChange={handleChange}
          aria-invalid={Boolean(errors.customerName)}
          aria-describedby={errors.customerName ? 'customerName-error' : undefined}
        />
        {errors.customerName && (
          <p id="customerName-error" className="field-error">
            {errors.customerName}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="pickupAddress">Ophaaladres</label>
        <input
          id="pickupAddress"
          name="pickupAddress"
          type="text"
          autoComplete="street-address"
          required
          maxLength={500}
          value={values.pickupAddress}
          onChange={handleChange}
          aria-invalid={Boolean(errors.pickupAddress)}
          aria-describedby={errors.pickupAddress ? 'pickupAddress-error' : undefined}
        />
        {errors.pickupAddress && (
          <p id="pickupAddress-error" className="field-error">
            {errors.pickupAddress}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="deliveryAddress">Leveringsadres</label>
        <input
          id="deliveryAddress"
          name="deliveryAddress"
          type="text"
          autoComplete="street-address"
          required
          maxLength={500}
          value={values.deliveryAddress}
          onChange={handleChange}
          aria-invalid={Boolean(errors.deliveryAddress)}
          aria-describedby={errors.deliveryAddress ? 'deliveryAddress-error' : undefined}
        />
        {errors.deliveryAddress && (
          <p id="deliveryAddress-error" className="field-error">
            {errors.deliveryAddress}
          </p>
        )}
      </div>

      <div className="form-field">
        <label htmlFor="status">Status</label>
        <select
          id="status"
          name="status"
          required
          value={values.status}
          onChange={handleChange}
          aria-invalid={Boolean(errors.status)}
          aria-describedby={errors.status ? 'status-error' : undefined}
        >
          {TRANSPORT_ORDER_STATUS_OPTIONS.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
        {errors.status && (
          <p id="status-error" className="field-error">
            {errors.status}
          </p>
        )}
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      <div className="form-actions">
        <button type="submit" className="primary-button" disabled={isSubmitting}>
          {isSubmitting ? 'Opdracht wordt aangemaakt...' : 'Opdracht aanmaken'}
        </button>
        {secondaryAction}
      </div>
    </form>
  )
}
