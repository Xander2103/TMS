import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { todayLocalIsoDate } from '../../../utils/dates'
import { itemDisplayName, listIssuedItemTemplates, type IssuedItemTemplate } from '../issuedItemsApi'
import {
  getTemplateDetail,
  issueFromWarehouse,
  parseNegativeStockPayload,
  searchInventoryEmployees,
  type IssuedItemVariant,
  type NegativeStockPayload,
  type WarehouseIssueInput,
} from '../inventoryApi'
import { formatQuantityWithUnit } from '../issuedItemUnits'
import { parseMovementQuantity } from '../quantities'
import { NegativeStockConfirmModal } from './NegativeStockConfirmModal'

interface WarehouseIssueModalProps {
  canOverrideStock: boolean
  onClose: () => void
  /** Called after a successful issue with the Dutch success message. */
  onIssued: (message: string) => void
}

/**
 * WH-H4: "Uitgeven" from the warehouse. The clerk picks the employee through the SAFE picker
 * (number, name, active — no HR record), the item by code + name, the variant, quantity, serial
 * (mandatory for serialized items, one serial = one piece), dates and notes. The server is
 * authoritative for every rule (serial holder, stock, archived item, consumable); its Dutch
 * message is shown in the form.
 */
export function WarehouseIssueModal({ canOverrideStock, onClose, onIssued }: WarehouseIssueModalProps) {
  const { t } = useLocale()
  const [templates, setTemplates] = useState<IssuedItemTemplate[] | null>(null)
  const [templatesFailed, setTemplatesFailed] = useState(false)
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [employeeLabel, setEmployeeLabel] = useState<string>('')
  const [templateId, setTemplateId] = useState<string | null>(null)
  const [variants, setVariants] = useState<IssuedItemVariant[]>([])
  const [variantId, setVariantId] = useState<string>('')
  const [quantity, setQuantity] = useState('1')
  const [serialNumber, setSerialNumber] = useState('')
  const [issuedDate, setIssuedDate] = useState(() => todayLocalIsoDate())
  const [expectedReturnDate, setExpectedReturnDate] = useState('')
  const [conditionAtIssue, setConditionAtIssue] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [negativeStock, setNegativeStock] = useState<{ payload: NegativeStockPayload; input: WarehouseIssueInput } | null>(null)
  // Labels of employees seen in picker results, so the chosen one can be named in messages.
  const knownEmployees = useRef(new Map<string, string>())

  useEffect(() => {
    let mounted = true
    listIssuedItemTemplates()
      .then((data) => {
        if (mounted) setTemplates(data.filter((template) => template.isActive))
      })
      .catch(() => {
        if (mounted) setTemplatesFailed(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  const template = useMemo(() => templates?.find((candidate) => candidate.id === templateId) ?? null, [templates, templateId])
  const variant = variants.find((candidate) => candidate.id === variantId) ?? null
  const serialized = template?.requiresSerialNumber ?? false
  const consumable = template?.isConsumable ?? false
  const showReturnDate = (template?.returnRequired ?? false) || serialized
  const available = template?.variantsEnabled ? (variant?.currentStock ?? null) : template?.stockTrackingEnabled ? template.totalAvailable : null

  const templateOptions: SearchableSelectOption[] = useMemo(
    () =>
      (templates ?? []).map((candidate) => ({
        value: candidate.id,
        label: itemDisplayName(candidate),
        description: candidate.category,
        keywords: `${candidate.code ?? ''} ${candidate.name}`,
      })),
    [templates],
  )

  async function searchEmployees(query: string): Promise<SearchableSelectOption[]> {
    const options = (await searchInventoryEmployees(query)).map((option) => ({
      value: option.id,
      label: `${option.name} (${option.employeeNumber})`,
    }))
    for (const option of options) knownEmployees.current.set(option.value, option.label)
    return options
  }

  function chooseEmployee(value: string | null) {
    setEmployeeId(value)
    setEmployeeLabel(value ? (knownEmployees.current.get(value) ?? '') : '')
  }

  async function chooseTemplate(id: string | null) {
    setTemplateId(id)
    setVariantId('')
    setVariants([])
    setFieldErrors({})
    const chosen = templates?.find((candidate) => candidate.id === id)
    if (chosen) {
      setQuantity(String(chosen.defaultQuantity))
      if (chosen.requiresSerialNumber) setQuantity('1')
    }
    if (chosen?.variantsEnabled) {
      try {
        const detail = await getTemplateDetail(chosen.id)
        setVariants(detail.variants.filter((candidate) => candidate.isActive))
      } catch {
        /* variant list unavailable; the server still validates the choice */
      }
    }
  }

  function validate(): WarehouseIssueInput | null {
    const errors: Record<string, string> = {}
    if (!employeeId) errors.employeeId = t('inventoryDesk.issue.employeeRequired')
    if (!template) errors.templateId = t('inventoryDesk.issue.itemRequired')
    if (template?.variantsEnabled && !variantId) errors.variantId = t('issuedItems.tab.variantRequired')
    const parsed = parseMovementQuantity(quantity)
    if (!parsed.ok) errors.quantity = t(parsed.key, parsed.params)
    else if (serialized && parsed.value !== 1) errors.quantity = t('inventoryDesk.issue.serialOnePiece')
    if (serialized && !serialNumber.trim()) errors.serialNumber = t('issuedItems.tab.serialRequired')
    if (expectedReturnDate && issuedDate && expectedReturnDate < issuedDate) {
      errors.expectedReturnDate = t('inventoryDesk.issue.returnBeforeIssue')
    }
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0 || !parsed.ok || !employeeId || !template) return null
    return {
      employeeId,
      templateId: template.id,
      variantId: variantId || null,
      quantity: parsed.value,
      serialNumber: serialNumber.trim() || null,
      issuedDate: issuedDate || null,
      expectedReturnDate: showReturnDate && expectedReturnDate ? expectedReturnDate : null,
      conditionAtIssue: conditionAtIssue.trim() || null,
      notes: notes.trim() || null,
    }
  }

  async function send(input: WarehouseIssueInput) {
    setBusy(true)
    setError(null)
    try {
      await issueFromWarehouse(input)
      onIssued(
        consumable
          ? t('inventoryDesk.issue.consumedSuccess', { item: template ? itemDisplayName(template) : '', employee: employeeLabel })
          : t('inventoryDesk.issue.success', { item: template ? itemDisplayName(template) : '', employee: employeeLabel }),
      )
    } catch (err) {
      const conflict = parseNegativeStockPayload(err)
      if (conflict) {
        setNegativeStock({ payload: conflict, input })
      } else {
        const described = describeApiError(err, t('inventoryDesk.issue.failed'))
        setError(described.message)
      }
      setBusy(false)
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const input = validate()
    if (input) await send(input)
  }

  return (
    <>
      <Modal
        title={t('inventoryDesk.issue.title')}
        onClose={onClose}
        busy={busy}
        footer={
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              {t('ui.actions.cancel')}
            </Button>
            <Button type="submit" form="warehouse-issue-form" disabled={busy}>
              {busy ? t('issuedItems.tab.saving') : t('inventoryDesk.issue.confirm')}
            </Button>
          </>
        }
      >
        <form id="warehouse-issue-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
          {error && (
            <div className="issued-items-form-error" role="alert">
              {error}
            </div>
          )}
          <FormField label={t('inventoryDesk.issue.employee')} htmlFor="wi-employee" required error={fieldErrors.employeeId}
            hint={t('inventoryDesk.issue.employeeHint')}>
            <SearchableSelect
              id="wi-employee"
              value={employeeId}
              onChange={chooseEmployee}
              options={[]}
              onSearch={searchEmployees}
              selectedLabel={employeeLabel}
              placeholder={t('inventoryDesk.issue.employeePlaceholder')}
              ariaInvalid={Boolean(fieldErrors.employeeId)}
            />
          </FormField>
          <FormField label={t('inventoryDesk.issue.item')} htmlFor="wi-item" required error={fieldErrors.templateId}>
            <SearchableSelect
              id="wi-item"
              value={templateId}
              onChange={(value) => void chooseTemplate(value)}
              options={templateOptions}
              isLoading={templates === null && !templatesFailed}
              errorMessage={templatesFailed ? t('inventoryDesk.issue.itemsFailed') : null}
              placeholder={t('inventoryDesk.issue.itemPlaceholder')}
              ariaInvalid={Boolean(fieldErrors.templateId)}
            />
          </FormField>
          {consumable && <p className="issued-items-section-hint">{t('inventoryDesk.issue.consumableHint')}</p>}
          {serialized && <p className="issued-items-section-hint">{t('inventoryDesk.issue.serializedHint')}</p>}
          {template?.variantsEnabled && (
            <FormField label={t('issuedItems.tab.variantLabel')} htmlFor="wi-variant" required error={fieldErrors.variantId}>
              <select id="wi-variant" value={variantId} onChange={(e) => setVariantId(e.target.value)} disabled={busy}>
                <option value="">{t('issuedItems.tab.chooseVariant')}</option>
                {variants.map((candidate) => (
                  <option key={candidate.id} value={candidate.id}>
                    {t('issuedItems.tab.variantOption', { label: candidate.label, stock: candidate.currentStock })}
                  </option>
                ))}
              </select>
            </FormField>
          )}
          {available !== null && (
            <p className="issued-items-stock-preview" role="status">
              {t('issuedItems.tab.availableStock', { stock: formatQuantityWithUnit(t, available, template?.unit ?? null) })}
            </p>
          )}
          <div className="issued-items-form-row">
            <FormField label={t('issuedItems.tab.quantity')} htmlFor="wi-qty" required error={fieldErrors.quantity}
              hint={t('inventoryDesk.issue.wholeUnitsHint')}>
              <input id="wi-qty" type="number" min={1} step={1} inputMode="numeric" value={quantity}
                onChange={(e) => setQuantity(e.target.value)} disabled={busy || serialized} />
            </FormField>
            <FormField label={t('issuedItems.tab.serialNumber')} htmlFor="wi-serial" required={serialized} error={fieldErrors.serialNumber}>
              <input id="wi-serial" value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} disabled={busy} maxLength={100}
                autoComplete="off" />
            </FormField>
          </div>
          <div className="issued-items-form-row">
            <FormField label={t('issuedItems.tab.issuedDate')} htmlFor="wi-date">
              <input id="wi-date" type="date" value={issuedDate} onChange={(e) => setIssuedDate(e.target.value)} disabled={busy} />
            </FormField>
            {showReturnDate && (
              <FormField label={t('inventoryDesk.issue.expectedReturn')} htmlFor="wi-return-date" error={fieldErrors.expectedReturnDate}>
                <input id="wi-return-date" type="date" value={expectedReturnDate} min={issuedDate || undefined}
                  onChange={(e) => setExpectedReturnDate(e.target.value)} disabled={busy} />
              </FormField>
            )}
          </div>
          {!consumable && (
            <FormField label={t('inventoryDesk.issue.conditionAtIssue')} htmlFor="wi-condition">
              <input id="wi-condition" value={conditionAtIssue} onChange={(e) => setConditionAtIssue(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
          )}
          <FormField label={t('issuedItems.tab.notes')} htmlFor="wi-notes">
            <textarea id="wi-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} maxLength={1000} />
          </FormField>
        </form>
      </Modal>
      {negativeStock && (
        <NegativeStockConfirmModal
          payload={negativeStock.payload}
          kind="issue"
          employeeName={employeeLabel}
          storageLocation={template?.storageLocation}
          canConfirm={canOverrideStock}
          busy={busy}
          onConfirm={(reason) => {
            const retry: WarehouseIssueInput = {
              ...negativeStock.input,
              confirmNegativeStock: true,
              expectedVersion: negativeStock.payload.version,
              overrideReason: reason.trim() || null,
            }
            setNegativeStock(null)
            void send(retry)
          }}
          onCancel={() => setNegativeStock(null)}
        />
      )}
    </>
  )
}
