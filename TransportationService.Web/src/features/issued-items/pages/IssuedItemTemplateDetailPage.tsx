import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { TabPanel, Tabs, type TabItem } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  addAttributeOption,
  correctStock,
  createAttributeDefinition,
  createVariant,
  deleteVariant,
  generateVariants,
  getTemplateDetail,
  listAttributeDefinitions,
  listCurrentHolders,
  listStockMovements,
  receiveStock,
  parseNegativeStockPayload,
  setTemplateAttributes,
  updateVariant,
  STOCK_MOVEMENT_LABELS,
  type CurrentHolder,
  type IssuedItemAttributeDefinition,
  type IssuedItemTemplateDetail,
  type IssuedItemVariant,
  type NegativeStockPayload,
  type StockCorrectionInput,
  type StockMovement,
  type VariantValueInput,
} from '../inventoryApi'
import { formatDateTime } from '../../../utils/dates'
import { TemplateFormModal } from '../TemplateFormModal'
import { NegativeStockConfirmModal } from '../components/NegativeStockConfirmModal'
import { StockThresholdsCard } from '../components/StockThresholdsCard'
import '../issued-items.css'

interface VariantEditorState {
  variant: IssuedItemVariant | null
  values: Record<string, { optionId: string; customValue: string }>
  isActive: boolean
  initialStock: string
  /** Free label for templates without linked attributes ("Small", "maat 43"). */
  label: string
  /** Per-variant low-stock threshold; must round-trip on edit or saving wipes it. */
  lowStockThreshold: string
}

interface StockDialogState {
  kind: 'receipt' | 'correction'
  variantId: string | null
  quantity: string
  reason: string
  notes: string
}

type DetailTab = 'algemeen' | 'voorraad' | 'varianten' | 'houders' | 'bewegingen'

/** Aliases keep older deep links (e.g. notification LinkPaths) working. */
const TAB_ALIASES: Record<string, DetailTab> = {
  stock: 'voorraad',
  variants: 'varianten',
  holders: 'houders',
  movements: 'bewegingen',
}

/** Detail/edit page of one issued-item template: configuration, attributes, variants, stock. */
export function IssuedItemTemplateDetailPage() {
  const { id = '' } = useParams()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManageInventory = hasPermission('inventory.manage') || hasPermission('issued_items.manage_templates')
  const canAdjustStock = hasPermission('inventory.adjust') || hasPermission('inventory.manage')
  const canOverrideNegative = hasPermission('inventory.override_negative_stock')

  const [detail, setDetail] = useState<IssuedItemTemplateDetail | null>(null)
  const [allDefinitions, setAllDefinitions] = useState<IssuedItemAttributeDefinition[]>([])
  const [movements, setMovements] = useState<StockMovement[] | null>(null)
  const [holders, setHolders] = useState<CurrentHolder[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [templateEditorOpen, setTemplateEditorOpen] = useState(false)
  const [attributePickerValue, setAttributePickerValue] = useState('')
  const [newAttribute, setNewAttribute] = useState<{ name: string; allowCustomValues: boolean } | null>(null)
  const [optionDrafts, setOptionDrafts] = useState<Record<string, string>>({})
  const [variantEditor, setVariantEditor] = useState<VariantEditorState | null>(null)
  const [variantError, setVariantError] = useState<string | null>(null)
  const [variantDeleteTarget, setVariantDeleteTarget] = useState<IssuedItemVariant | null>(null)
  const [stockDialog, setStockDialog] = useState<StockDialogState | null>(null)
  const [stockError, setStockError] = useState<string | null>(null)
  // 409-bevestigingsflow bij correcties die onder nul gaan.
  const [stockNegative, setStockNegative] = useState<{ payload: NegativeStockPayload; input: StockCorrectionInput } | null>(null)
  const [generateDialog, setGenerateDialog] = useState<Record<string, string[]> | null>(null)
  const [generateError, setGenerateError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()

  const reload = useCallback(() => setReloadToken((token) => token + 1), [])

  useEffect(() => {
    let mounted = true
    Promise.all([
      getTemplateDetail(id),
      listAttributeDefinitions(false).catch(() => [] as IssuedItemAttributeDefinition[]),
    ])
      .then(([templateDetail, definitions]) => {
        if (!mounted) return
        setDetail(templateDetail)
        setAllDefinitions(definitions)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('issuedItems.detail.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [id, reloadToken])

  const stockTracked = detail?.template.stockTrackingEnabled ?? false

  useEffect(() => {
    if (!stockTracked) return
    let mounted = true
    listStockMovements(id)
      .then((data) => {
        if (mounted) setMovements(data)
      })
      .catch(() => {})
    listCurrentHolders(id)
      .then((data) => {
        if (mounted) setHolders(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [id, reloadToken, stockTracked])

  // Stale data from a previous stock-enabled state is never shown once the toggle is off.
  const visibleMovements = stockTracked ? movements : null
  const visibleHolders = stockTracked ? holders : null

  const linkableDefinitions = useMemo(
    () => allDefinitions.filter((d) => d.isActive && !detail?.attributes.some((a) => a.id === d.id)),
    [allDefinitions, detail],
  )

  async function run(action: () => Promise<unknown>, successMessage: string, onError?: (message: string) => void) {
    setBusy(true)
    try {
      await action()
      showSuccess(successMessage)
      reload()
      return true
    } catch (err) {
      const message = describeApiError(err, t('issuedItems.detail.operationFailed')).message
      if (onError) onError(message)
      else showError(message)
      return false
    } finally {
      setBusy(false)
    }
  }

  async function linkAttribute(definitionId: string) {
    if (!detail || !definitionId) return
    await run(
      () => setTemplateAttributes(id, [...detail.attributes.map((a) => a.id), definitionId]),
      t('issuedItems.detail.attributeLinked'),
    )
    setAttributePickerValue('')
  }

  async function unlinkAttribute(definitionId: string) {
    if (!detail) return
    await run(
      () => setTemplateAttributes(id, detail.attributes.filter((a) => a.id !== definitionId).map((a) => a.id)),
      t('issuedItems.detail.attributeUnlinked'),
    )
  }

  async function handleCreateAttribute(event: FormEvent) {
    event.preventDefault()
    if (!newAttribute || !detail) return
    if (!newAttribute.name.trim()) return
    await run(async () => {
      const definition = await createAttributeDefinition({
        name: newAttribute.name.trim(),
        allowCustomValues: newAttribute.allowCustomValues,
        isShared: true,
        sortOrder: allDefinitions.length,
        isActive: true,
      })
      await setTemplateAttributes(id, [...detail.attributes.map((a) => a.id), definition.id])
    }, t('issuedItems.detail.attributeCreated'))
    setNewAttribute(null)
  }

  async function handleAddOption(definitionId: string) {
    const draft = (optionDrafts[definitionId] ?? '').trim()
    if (!draft) return
    const definition = detail?.attributes.find((a) => a.id === definitionId)
    await run(
      () => addAttributeOption(definitionId, { value: draft, sortOrder: definition?.options.length ?? 0, isActive: true }),
      t('issuedItems.detail.valueAdded'),
    )
    setOptionDrafts((d) => ({ ...d, [definitionId]: '' }))
  }

  function openVariantEditor(variant: IssuedItemVariant | null) {
    if (!detail) return
    const values: VariantEditorState['values'] = {}
    for (const attribute of detail.attributes) {
      const existing = variant?.values.find((v) => v.attributeDefinitionId === attribute.id)
      values[attribute.id] = {
        optionId: existing?.attributeOptionId ?? '',
        customValue: existing && !existing.attributeOptionId ? existing.value : '',
      }
    }
    setVariantError(null)
    setVariantEditor({
      variant,
      values,
      isActive: variant?.isActive ?? true,
      initialStock: '',
      label: variant?.label ?? '',
      lowStockThreshold: variant?.lowStockThreshold != null ? String(variant.lowStockThreshold) : '',
    })
  }

  async function handleVariantSubmit(event: FormEvent) {
    event.preventDefault()
    if (!variantEditor || !detail) return
    const values: VariantValueInput[] = detail.attributes.map((attribute) => {
      const draft = variantEditor.values[attribute.id]
      return {
        attributeDefinitionId: attribute.id,
        attributeOptionId: draft?.optionId || null,
        customValue: draft?.optionId ? null : draft?.customValue.trim() || null,
      }
    })
    const initialStock = variantEditor.initialStock.trim() === '' ? null : Number(variantEditor.initialStock)
    const input = {
      values,
      isActive: variantEditor.isActive,
      sortOrder: variantEditor.variant?.sortOrder ?? detail.variants.length,
      initialStock,
      label: variantEditor.label.trim() || null,
      // Always round-trip the threshold — omitting it made every edit silently clear it.
      lowStockThreshold: variantEditor.lowStockThreshold.trim() === '' ? null : Number(variantEditor.lowStockThreshold),
    }
    const ok = await run(
      () => (variantEditor.variant ? updateVariant(id, variantEditor.variant.id, input) : createVariant(id, input)),
      variantEditor.variant ? t('issuedItems.detail.variantUpdated') : t('issuedItems.detail.variantAdded'),
      setVariantError,
    )
    if (ok) setVariantEditor(null)
  }

  async function handleVariantDelete() {
    if (!variantDeleteTarget) return
    const target = variantDeleteTarget
    setVariantDeleteTarget(null)
    await run(() => deleteVariant(id, target.id), t('issuedItems.detail.variantDeleted'))
  }

  async function handleStockSubmit(event: FormEvent) {
    event.preventDefault()
    if (!stockDialog) return
    const quantity = Number(stockDialog.quantity)
    if (!Number.isFinite(quantity)) {
      setStockError(t('issuedItems.detail.invalidQty'))
      return
    }
    if (stockDialog.kind === 'receipt') {
      const ok = await run(
        () => receiveStock(id, { variantId: stockDialog.variantId, quantity, notes: stockDialog.notes.trim() || null }),
        t('issuedItems.detail.receiptSaved'),
        setStockError,
      )
      if (ok) setStockDialog(null)
      return
    }
    await submitCorrection({ variantId: stockDialog.variantId, newQuantity: quantity, reason: stockDialog.reason.trim() })
  }

  /** Correcties lopen buiten run(): een 409 opent de bevestigingsmodal in plaats van een foutmelding. */
  async function submitCorrection(input: StockCorrectionInput) {
    setBusy(true)
    try {
      await correctStock(id, input)
      showSuccess(t('issuedItems.detail.correctionSaved'))
      setStockNegative(null)
      setStockDialog(null)
      reload()
    } catch (err) {
      const conflict = parseNegativeStockPayload(err)
      if (conflict) {
        // Bij versionMismatch bevat de nieuwe payload de nieuwe cijfers + version.
        setStockNegative({ payload: conflict, input })
        return
      }
      setStockNegative(null)
      setStockError(describeApiError(err, t('issuedItems.detail.operationFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleGenerateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!generateDialog || !detail) return
    const dimensions = detail.attributes.map((attribute) => ({
      attributeDefinitionId: attribute.id,
      optionIds: generateDialog[attribute.id] ?? [],
    }))
    if (dimensions.some((d) => d.optionIds.length === 0)) {
      setGenerateError(t('issuedItems.detail.generateMissing'))
      return
    }
    const ok = await run(() => generateVariants(id, dimensions), t('issuedItems.detail.generated'), setGenerateError)
    if (ok) setGenerateDialog(null)
  }

  if (loadErrorKey) {
    return <p className="placeholder-text">{t(loadErrorKey)}</p>
  }

  if (!detail) {
    return <p className="placeholder-text">{t('issuedItems.detail.loading')}</p>
  }

  const template = detail.template

  const tabs: TabItem[] = [
    { id: 'algemeen', label: t('issuedItems.detail.tabGeneral') },
    ...(template.stockTrackingEnabled && !template.variantsEnabled ? [{ id: 'voorraad', label: t('issuedItems.detail.tabStock') }] : []),
    ...(template.stockTrackingEnabled && template.variantsEnabled
      ? [{ id: 'varianten', label: t('issuedItems.detail.tabVariants'), badge: detail.variants.length || undefined }]
      : []),
    { id: 'houders', label: t('issuedItems.detail.tabHolders'), badge: visibleHolders?.length || undefined },
    ...(template.stockTrackingEnabled ? [{ id: 'bewegingen', label: t('issuedItems.detail.tabMovements') }] : []),
  ]
  const rawTab = searchParams.get('tab') ?? 'algemeen'
  const requestedTab = (TAB_ALIASES[rawTab] ?? rawTab) as DetailTab
  const activeTab: DetailTab = tabs.some((tab) => tab.id === requestedTab)
    ? requestedTab
    : // Deep links naar ?tab=voorraad (bv. vanuit het voorraadoverzicht) landen bij variantsjablonen op Varianten.
      requestedTab === 'voorraad' && tabs.some((tab) => tab.id === 'varianten')
      ? 'varianten'
      : 'algemeen'
  const setActiveTab = (tabId: string) => {
    const next = new URLSearchParams(searchParams)
    next.set('tab', tabId)
    setSearchParams(next, { replace: true })
  }

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: t('issuedItems.detail.breadcrumbSettings'), to: '/settings' },
          { label: t('issuedItems.detail.breadcrumbTemplates'), to: '/settings/issued-item-templates' },
          { label: template.name },
        ]}
      />
      <PageHeader
        title={template.name}
        subtitle={template.description ?? template.category}
        action={
          canManageInventory ? <Button onClick={() => setTemplateEditorOpen(true)}>{t('issuedItems.detail.editTemplate')}</Button> : undefined
        }
      />

      <Tabs tabs={tabs} activeId={activeTab} onChange={setActiveTab} />

      {activeTab === 'algemeen' && (
      <TabPanel tabId="algemeen">
      <section className="issued-items-card">
        <h2>{t('issuedItems.detail.settingsTitle')}</h2>
        <dl className="issued-items-summary">
          <div>
            <dt>{t('issuedItems.detail.category')}</dt>
            <dd>{template.category}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.detail.stockTracking')}</dt>
            <dd>{template.stockTrackingEnabled ? t('issuedItems.detail.on') : t('issuedItems.detail.off')}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.detail.variants')}</dt>
            <dd>{template.variantsEnabled ? t('issuedItems.detail.on') : t('issuedItems.detail.off')}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.detail.serialRequired')}</dt>
            <dd>{template.requiresSerialNumber ? t('issuedItems.detail.yes') : t('issuedItems.detail.no')}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.detail.returnRequired')}</dt>
            <dd>{template.returnRequired ? t('issuedItems.detail.yes') : t('issuedItems.detail.no')}</dd>
          </div>
          {template.stockTrackingEnabled && (
            <>
              <div>
                <dt>{t('issuedItems.detail.available')}</dt>
                <dd>
                  {template.totalAvailable}
                  {template.unit ? ` ${template.unit}` : ''}{' '}
                  {template.lowStock && <Badge tone="warning">{t('issuedItems.detail.lowStock')}</Badge>}
                </dd>
              </div>
              {template.lowStockThreshold !== null && (
                <div>
                  <dt>{t('issuedItems.detail.lowThreshold')}</dt>
                  <dd>{template.lowStockThreshold}</dd>
                </div>
              )}
              {template.storageLocation && (
                <div>
                  <dt>{t('issuedItems.detail.storage')}</dt>
                  <dd>{template.storageLocation}</dd>
                </div>
              )}
            </>
          )}
        </dl>
      </section>
      </TabPanel>
      )}

      {activeTab === 'varianten' && (
      <TabPanel tabId="varianten">
        <section className="issued-items-card">
          <div className="issued-items-card-header">
            <h2>{t('issuedItems.detail.attributesTitle')}</h2>
            {canManageInventory && (
              <div className="issued-items-card-actions">
                <select
                  aria-label={t('issuedItems.detail.linkAria')}
                  value={attributePickerValue}
                  disabled={busy || linkableDefinitions.length === 0}
                  onChange={(e) => {
                    setAttributePickerValue(e.target.value)
                    if (e.target.value) void linkAttribute(e.target.value)
                  }}
                >
                  <option value="">
                    {linkableDefinitions.length === 0 ? t('issuedItems.detail.noLinkable') : t('issuedItems.detail.linkExisting')}
                  </option>
                  {linkableDefinitions.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
                <Button variant="secondary" onClick={() => setNewAttribute({ name: '', allowCustomValues: false })} disabled={busy}>
                  {t('issuedItems.detail.newAttribute')}
                </Button>
              </div>
            )}
          </div>
          {detail.attributes.length === 0 && (
            <p className="placeholder-text">
              {t('issuedItems.detail.attributesEmpty')}
            </p>
          )}
          {detail.attributes.map((attribute) => (
            <div key={attribute.id} className="issued-items-attribute">
              <div className="issued-items-attribute-header">
                <strong>{attribute.name}</strong>
                {attribute.isShared && <Badge tone="info">{t('issuedItems.detail.reusable')}</Badge>}
                {attribute.allowCustomValues && <Badge tone="neutral">{t('issuedItems.detail.freeValues')}</Badge>}
                {canManageInventory && (
                  <button type="button" className="issued-items-link" onClick={() => unlinkAttribute(attribute.id)} disabled={busy}>
                    {t('issuedItems.detail.unlink')}
                  </button>
                )}
              </div>
              <div className="issued-items-attribute-options">
                {attribute.options.filter((o) => o.isActive).map((option) => (
                  <span key={option.id} className="issued-items-chip">
                    {option.value}
                  </span>
                ))}
                {attribute.options.length === 0 && <span className="customer-form-muted">{t('issuedItems.detail.noValues')}</span>}
                {canManageInventory && (
                  <span className="issued-items-option-add">
                    <input
                      aria-label={t('issuedItems.detail.newValueAria', { name: attribute.name })}
                      placeholder={t('issuedItems.detail.newValuePlaceholder')}
                      value={optionDrafts[attribute.id] ?? ''}
                      maxLength={100}
                      onChange={(e) => setOptionDrafts((d) => ({ ...d, [attribute.id]: e.target.value }))}
                    />
                    <Button variant="secondary" onClick={() => handleAddOption(attribute.id)} disabled={busy}>
                      {t('issuedItems.detail.addValue')}
                    </Button>
                  </span>
                )}
              </div>
            </div>
          ))}
        </section>

        <section className="issued-items-card">
          <div className="issued-items-card-header">
            <h2>
              {t('issuedItems.detail.variantsTitle')}{' '}
              <span className="issued-items-computed-stock">{t('issuedItems.detail.variantsComputed', { total: template.totalAvailable })}</span>
            </h2>
            {canManageInventory && (
              <div className="issued-items-card-actions">
                <Button
                  onClick={() => {
                    setGenerateError(null)
                    setGenerateDialog({})
                  }}
                  disabled={busy || detail.attributes.length === 0}
                >
                  {t('issuedItems.detail.generateVariants')}
                </Button>
                <Button variant="secondary" onClick={() => openVariantEditor(null)} disabled={busy}>
                  {t('issuedItems.detail.addVariant')}
                </Button>
              </div>
            )}
          </div>
          {detail.variants.length === 0 && (
            <p className="placeholder-text">
              {t('issuedItems.detail.variantsEmpty')}
            </p>
          )}
          {detail.variants.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('issuedItems.detail.colVariant')}</th>
                  <th>{t('issuedItems.detail.colStock')}</th>
                  <th>{t('issuedItems.detail.colStatus')}</th>
                  <th aria-label={t('issuedItems.tab.colActions')} />
                </tr>
              </thead>
              <tbody>
                {detail.variants.map((variant) => (
                  <tr key={variant.id}>
                    <td>{variant.label}</td>
                    <td>
                      <span className="issued-items-stock-cell">
                        {variant.currentStock}
                        {template.lowStockThreshold !== null && variant.currentStock <= template.lowStockThreshold && (
                          <Badge tone="warning">{t('issuedItems.detail.low')}</Badge>
                        )}
                      </span>
                    </td>
                    <td>
                      <Badge tone={variant.isActive ? 'success' : 'neutral'}>
                        {variant.isActive ? t('issuedItems.detail.statusActive') : t('issuedItems.detail.statusArchived')}
                      </Badge>
                    </td>
                    <td className="issued-items-row-actions">
                      {canAdjustStock && (
                        <button
                          type="button"
                          className="issued-items-link"
                          onClick={() => {
                            setStockError(null)
                            setStockDialog({ kind: 'receipt', variantId: variant.id, quantity: '', reason: '', notes: '' })
                          }}
                        >
                          {t('issuedItems.detail.receiptAction')}
                        </button>
                      )}
                      {canAdjustStock && (
                        <button
                          type="button"
                          className="issued-items-link"
                          onClick={() => {
                            setStockError(null)
                            setStockDialog({
                              kind: 'correction',
                              variantId: variant.id,
                              quantity: String(variant.currentStock),
                              reason: '',
                              notes: '',
                            })
                          }}
                        >
                          {t('issuedItems.detail.correctAction')}
                        </button>
                      )}
                      {canManageInventory && (
                        <button type="button" className="issued-items-link" onClick={() => openVariantEditor(variant)}>
                          {t('ui.actions.edit')}
                        </button>
                      )}
                      {canManageInventory && (
                        <button
                          type="button"
                          className="issued-items-link issued-items-link-danger"
                          onClick={() => setVariantDeleteTarget(variant)}
                        >
                          {t('ui.actions.delete')}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <StockThresholdsCard template={template} onSaved={reload} />
      </TabPanel>
      )}

      {activeTab === 'voorraad' && (
      <TabPanel tabId="voorraad">
        <section className="issued-items-card">
          <div className="issued-items-card-header">
            <h2>{t('issuedItems.detail.stockTitle')}</h2>
            {canAdjustStock && (
              <div className="issued-items-card-actions">
                <Button
                  onClick={() => {
                    setStockError(null)
                    setStockDialog({ kind: 'receipt', variantId: null, quantity: '', reason: '', notes: '' })
                  }}
                  disabled={busy}
                >
                  {t('issuedItems.detail.addStock')}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setStockError(null)
                    setStockDialog({ kind: 'correction', variantId: null, quantity: String(template.currentStock), reason: '', notes: '' })
                  }}
                  disabled={busy}
                >
                  {t('issuedItems.detail.correctAction')}
                </Button>
              </div>
            )}
          </div>
          <p className="issued-items-stock-figure">
            {template.currentStock}
            {template.unit ? ` ${template.unit}` : ''} {t('issuedItems.detail.availableFigure')}{' '}
            {template.lowStock && <Badge tone="warning">{t('issuedItems.detail.lowStock')}</Badge>}
          </p>
          {template.lowStockThreshold !== null && (
            <p className="issued-items-computed-stock">{t('issuedItems.detail.lowThresholdFigure', { value: template.lowStockThreshold })}</p>
          )}
        </section>
        <StockThresholdsCard template={template} onSaved={reload} />
      </TabPanel>
      )}

      {activeTab === 'bewegingen' && (
      <TabPanel tabId="bewegingen">
        <section className="issued-items-card">
          <h2>{t('issuedItems.detail.movementsTitle')}</h2>
          {visibleMovements === null && <p className="placeholder-text">{t('issuedItems.detail.loading')}</p>}
          {visibleMovements !== null && visibleMovements.length === 0 && <p className="placeholder-text">{t('issuedItems.detail.movementsEmpty')}</p>}
          {visibleMovements !== null && visibleMovements.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('issuedItems.detail.colDate')}</th>
                  <th>{t('issuedItems.detail.colType')}</th>
                  <th>{t('issuedItems.detail.colVariant')}</th>
                  <th>{t('issuedItems.detail.colQty')}</th>
                  <th>{t('issuedItems.detail.colResult')}</th>
                  <th>{t('issuedItems.detail.colReason')}</th>
                  <th>{t('issuedItems.detail.colEmployee')}</th>
                </tr>
              </thead>
              <tbody>
                {visibleMovements.map((movement) => (
                  <tr key={movement.id}>
                    <td>{formatDateTime(movement.timestamp)}</td>
                    <td>{t(STOCK_MOVEMENT_LABELS[movement.movementType])}</td>
                    <td>{movement.variantLabel ?? '—'}</td>
                    <td>{movement.quantity > 0 ? `+${movement.quantity}` : movement.quantity}</td>
                    <td>{movement.resultingStock}</td>
                    <td>{movement.reason ?? movement.notes ?? '—'}</td>
                    <td>{movement.employeeName ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </TabPanel>
      )}

      {activeTab === 'houders' && (
      <TabPanel tabId="houders">
      <section className="issued-items-card">
        <h2>{t('issuedItems.detail.holdersTitle')}</h2>
        {!stockTracked && (
          <p className="placeholder-text">{t('issuedItems.detail.holdersUnavailable')}</p>
        )}
        {stockTracked && visibleHolders === null && <p className="placeholder-text">{t('issuedItems.detail.loading')}</p>}
        {visibleHolders !== null && visibleHolders.length === 0 && <p className="placeholder-text">{t('issuedItems.detail.holdersEmpty')}</p>}
        {visibleHolders !== null && visibleHolders.length > 0 && (
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>{t('issuedItems.detail.colEmployee')}</th>
                <th>{t('issuedItems.detail.colNumber')}</th>
                <th>{t('issuedItems.detail.colVariant')}</th>
                <th>{t('issuedItems.detail.colQty')}</th>
                <th>{t('issuedItems.detail.colIssued')}</th>
                <th>{t('issuedItems.detail.colSerial')}</th>
              </tr>
            </thead>
            <tbody>
              {visibleHolders.map((holder) => (
                <tr key={holder.itemId}>
                  <td>{holder.employeeName}</td>
                  <td>{holder.employeeNumber}</td>
                  <td>{holder.variantLabel ?? '—'}</td>
                  <td>{holder.quantity}</td>
                  <td>{holder.issuedDate ?? '—'}</td>
                  <td>{holder.serialNumber ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      </TabPanel>
      )}

      {templateEditorOpen && (
        <TemplateFormModal
          editing={template}
          onClose={() => setTemplateEditorOpen(false)}
          onSaved={() => {
            showSuccess(t('issuedItems.detail.templateUpdated'))
            setTemplateEditorOpen(false)
            reload()
          }}
        />
      )}

      {newAttribute && (
        <Modal
          title={t('issuedItems.detail.newAttributeTitle')}
          onClose={() => setNewAttribute(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setNewAttribute(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="new-attribute-form" disabled={busy}>
                {t('issuedItems.detail.create')}
              </Button>
            </>
          }
        >
          <form id="new-attribute-form" className="issued-items-form" onSubmit={handleCreateAttribute} noValidate>
            <FormField label={t('issuedItems.detail.attrName')} htmlFor="attr-name" required hint={t('issuedItems.detail.attrNameHint')}>
              <input
                id="attr-name"
                value={newAttribute.name}
                onChange={(e) => setNewAttribute((a) => (a ? { ...a, name: e.target.value } : a))}
                maxLength={100}
              />
            </FormField>
            <label className="issued-items-checkbox">
              <input
                type="checkbox"
                checked={newAttribute.allowCustomValues}
                onChange={(e) => setNewAttribute((a) => (a ? { ...a, allowCustomValues: e.target.checked } : a))}
              />
              <span>{t('issuedItems.detail.allowCustom')}</span>
            </label>
            <p className="customer-form-muted">
              {t('issuedItems.detail.attributesShared')}
            </p>
          </form>
        </Modal>
      )}

      {variantEditor && (
        <Modal
          title={
            variantEditor.variant
              ? t('issuedItems.detail.variantEditTitle', { label: variantEditor.variant.label })
              : t('issuedItems.detail.variantAddTitle')
          }
          onClose={() => setVariantEditor(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setVariantEditor(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="variant-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="variant-form" className="issued-items-form" onSubmit={handleVariantSubmit} noValidate>
            {variantError && (
              <div className="issued-items-form-error" role="alert">
                {variantError}
              </div>
            )}
            {detail.attributes.length === 0 && (
              <FormField label={t('issuedItems.detail.variantNameLabel')} htmlFor="var-label" required hint={t('issuedItems.detail.variantNameHint')}>
                <input
                  id="var-label"
                  value={variantEditor.label}
                  maxLength={150}
                  onChange={(e) => setVariantEditor((s) => (s ? { ...s, label: e.target.value } : s))}
                />
              </FormField>
            )}
            {detail.attributes.map((attribute) => {
              const draft = variantEditor.values[attribute.id] ?? { optionId: '', customValue: '' }
              return (
                <FormField key={attribute.id} label={attribute.name} htmlFor={`var-${attribute.id}`}>
                  <div className="issued-items-variant-value">
                    <select
                      id={`var-${attribute.id}`}
                      value={draft.optionId}
                      onChange={(e) =>
                        setVariantEditor((s) =>
                          s ? { ...s, values: { ...s.values, [attribute.id]: { ...draft, optionId: e.target.value } } } : s,
                        )
                      }
                    >
                      <option value="">
                        {attribute.allowCustomValues ? t('issuedItems.detail.freeValueOption') : t('issuedItems.detail.chooseValueOption')}
                      </option>
                      {attribute.options.filter((o) => o.isActive).map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.value}
                        </option>
                      ))}
                    </select>
                    {attribute.allowCustomValues && !draft.optionId && (
                      <input
                        aria-label={t('issuedItems.detail.freeValueAria', { name: attribute.name })}
                        placeholder={t('issuedItems.detail.freeValuePlaceholder')}
                        value={draft.customValue}
                        maxLength={100}
                        onChange={(e) =>
                          setVariantEditor((s) =>
                            s ? { ...s, values: { ...s.values, [attribute.id]: { ...draft, customValue: e.target.value } } } : s,
                          )
                        }
                      />
                    )}
                  </div>
                </FormField>
              )
            })}
            {!variantEditor.variant && (
              <FormField label={t('issuedItems.detail.initialStock')} htmlFor="var-initial" hint={t('issuedItems.detail.initialStockHint')}>
                <input
                  id="var-initial"
                  type="number"
                  min={0}
                  value={variantEditor.initialStock}
                  onChange={(e) => setVariantEditor((s) => (s ? { ...s, initialStock: e.target.value } : s))}
                />
              </FormField>
            )}
            <FormField label={t('issuedItems.detail.variantThreshold')} htmlFor="var-threshold" hint={t('issuedItems.detail.variantThresholdHint')}>
              <input
                id="var-threshold"
                type="number"
                min={0}
                value={variantEditor.lowStockThreshold}
                onChange={(e) => setVariantEditor((s) => (s ? { ...s, lowStockThreshold: e.target.value } : s))}
              />
            </FormField>
            <label className="issued-items-checkbox">
              <input
                type="checkbox"
                checked={variantEditor.isActive}
                onChange={(e) => setVariantEditor((s) => (s ? { ...s, isActive: e.target.checked } : s))}
              />
              <span>{t('issuedItems.detail.activeIssuable')}</span>
            </label>
          </form>
        </Modal>
      )}

      {stockDialog && (
        <Modal
          title={stockDialog.kind === 'receipt' ? t('issuedItems.detail.stockDialogReceiptTitle') : t('issuedItems.detail.stockDialogCorrectionTitle')}
          onClose={() => setStockDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setStockDialog(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="stock-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="stock-form" className="issued-items-form" onSubmit={handleStockSubmit} noValidate>
            {stockError && (
              <div className="issued-items-form-error" role="alert">
                {stockError}
              </div>
            )}
            {stockDialog.variantId && (
              <p className="customer-form-muted">
                {t('issuedItems.detail.stockVariant', {
                  label: detail.variants.find((v) => v.id === stockDialog.variantId)?.label ?? '—',
                })}
              </p>
            )}
            <FormField
              label={stockDialog.kind === 'receipt' ? t('issuedItems.detail.qtyReceived') : t('issuedItems.detail.newQuantity')}
              htmlFor="stock-qty"
              required
            >
              <input
                id="stock-qty"
                type="number"
                value={stockDialog.quantity}
                onChange={(e) => setStockDialog((s) => (s ? { ...s, quantity: e.target.value } : s))}
              />
            </FormField>
            {stockDialog.kind === 'correction' && (
              <FormField label={t('issuedItems.detail.reason')} htmlFor="stock-reason" required hint={t('issuedItems.detail.reasonHint')}>
                <input
                  id="stock-reason"
                  value={stockDialog.reason}
                  onChange={(e) => setStockDialog((s) => (s ? { ...s, reason: e.target.value } : s))}
                  maxLength={300}
                />
              </FormField>
            )}
            {stockDialog.kind === 'receipt' && (
              <FormField label={t('issuedItems.detail.note')} htmlFor="stock-notes">
                <input
                  id="stock-notes"
                  value={stockDialog.notes}
                  onChange={(e) => setStockDialog((s) => (s ? { ...s, notes: e.target.value } : s))}
                  maxLength={500}
                />
              </FormField>
            )}
          </form>
        </Modal>
      )}

      {generateDialog && (
        <Modal
          title={t('issuedItems.detail.generateTitle')}
          onClose={() => setGenerateDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setGenerateDialog(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="generate-variants-form" disabled={busy}>
                {t('issuedItems.detail.generate')}
              </Button>
            </>
          }
        >
          <form id="generate-variants-form" className="issued-items-form" onSubmit={handleGenerateSubmit} noValidate>
            {generateError && (
              <div className="issued-items-form-error" role="alert">
                {generateError}
              </div>
            )}
            <p className="customer-form-muted">
              {t('issuedItems.detail.generateHint')}
            </p>
            {detail.attributes.map((attribute) => (
              <fieldset key={attribute.id} className="issued-items-generate-dimension">
                <legend>{attribute.name}</legend>
                {attribute.options.filter((o) => o.isActive).map((option) => {
                  const selected = generateDialog[attribute.id] ?? []
                  const checked = selected.includes(option.id)
                  return (
                    <label key={option.id} className="issued-items-checkbox">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) =>
                          setGenerateDialog((s) =>
                            s
                              ? {
                                  ...s,
                                  [attribute.id]: e.target.checked
                                    ? [...selected, option.id]
                                    : selected.filter((v) => v !== option.id),
                                }
                              : s,
                          )
                        }
                      />
                      <span>{option.value}</span>
                    </label>
                  )
                })}
                {attribute.options.filter((o) => o.isActive).length === 0 && (
                  <p className="customer-form-muted">{t('issuedItems.detail.generateNoValues')}</p>
                )}
              </fieldset>
            ))}
          </form>
        </Modal>
      )}

      {stockNegative && (
        <NegativeStockConfirmModal
          payload={stockNegative.payload}
          kind="correction"
          storageLocation={template.storageLocation}
          canConfirm={canOverrideNegative}
          busy={busy}
          onConfirm={() =>
            void submitCorrection({
              ...stockNegative.input,
              confirmNegativeStock: true,
              expectedVersion: stockNegative.payload.version,
            })
          }
          onCancel={() => setStockNegative(null)}
        />
      )}

      {variantDeleteTarget && (
        <ConfirmDialog
          title={t('issuedItems.detail.variantDeleteTitle')}
          message={t('issuedItems.detail.variantDeleteMessage', { label: variantDeleteTarget.label })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleVariantDelete}
          onCancel={() => setVariantDeleteTarget(null)}
        />
      )}
    </div>
  )
}
