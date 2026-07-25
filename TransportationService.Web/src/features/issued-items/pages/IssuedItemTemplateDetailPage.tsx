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
  setTemplateAttributes,
  updateVariant,
  STOCK_MOVEMENT_LABELS,
  type CurrentHolder,
  type IssuedItemAttributeDefinition,
  type IssuedItemTemplateDetail,
  type IssuedItemVariant,
  type StockMovement,
  type VariantValueInput,
} from '../inventoryApi'
import { TemplateFormModal } from '../TemplateFormModal'
import '../issued-items.css'

interface VariantEditorState {
  variant: IssuedItemVariant | null
  values: Record<string, { optionId: string; customValue: string }>
  isActive: boolean
  initialStock: string
  /** Free label for templates without linked attributes ("Small", "maat 43"). */
  label: string
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
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const canManageInventory = hasPermission('inventory.manage') || hasPermission('issued_items.manage_templates')
  const canAdjustStock = hasPermission('inventory.adjust') || hasPermission('inventory.manage')

  const [detail, setDetail] = useState<IssuedItemTemplateDetail | null>(null)
  const [allDefinitions, setAllDefinitions] = useState<IssuedItemAttributeDefinition[]>([])
  const [movements, setMovements] = useState<StockMovement[] | null>(null)
  const [holders, setHolders] = useState<CurrentHolder[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
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
  const [generateDialog, setGenerateDialog] = useState<Record<string, string[]> | null>(null)
  const [generateError, setGenerateError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()

  const reload = useCallback(() => setReloadToken((t) => t + 1), [])

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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Het sjabloon kon niet worden geladen.')
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
      const message = describeApiError(err, 'De bewerking is mislukt.').message
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
      'Attribuut gekoppeld.',
    )
    setAttributePickerValue('')
  }

  async function unlinkAttribute(definitionId: string) {
    if (!detail) return
    await run(
      () => setTemplateAttributes(id, detail.attributes.filter((a) => a.id !== definitionId).map((a) => a.id)),
      'Attribuut losgekoppeld.',
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
    }, 'Attribuut aangemaakt en gekoppeld.')
    setNewAttribute(null)
  }

  async function handleAddOption(definitionId: string) {
    const draft = (optionDrafts[definitionId] ?? '').trim()
    if (!draft) return
    const definition = detail?.attributes.find((a) => a.id === definitionId)
    await run(
      () => addAttributeOption(definitionId, { value: draft, sortOrder: definition?.options.length ?? 0, isActive: true }),
      'Waarde toegevoegd.',
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
    setVariantEditor({ variant, values, isActive: variant?.isActive ?? true, initialStock: '', label: variant?.label ?? '' })
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
    }
    const ok = await run(
      () => (variantEditor.variant ? updateVariant(id, variantEditor.variant.id, input) : createVariant(id, input)),
      variantEditor.variant ? 'Variant bijgewerkt.' : 'Variant toegevoegd.',
      setVariantError,
    )
    if (ok) setVariantEditor(null)
  }

  async function handleVariantDelete() {
    if (!variantDeleteTarget) return
    const target = variantDeleteTarget
    setVariantDeleteTarget(null)
    await run(() => deleteVariant(id, target.id), 'Variant verwijderd.')
  }

  async function handleStockSubmit(event: FormEvent) {
    event.preventDefault()
    if (!stockDialog) return
    const quantity = Number(stockDialog.quantity)
    if (!Number.isFinite(quantity)) {
      setStockError('Geef een geldig aantal op.')
      return
    }
    const ok = await run(
      () =>
        stockDialog.kind === 'receipt'
          ? receiveStock(id, { variantId: stockDialog.variantId, quantity, notes: stockDialog.notes.trim() || null })
          : correctStock(id, { variantId: stockDialog.variantId, newQuantity: quantity, reason: stockDialog.reason.trim() }),
      stockDialog.kind === 'receipt' ? 'Voorraad toegevoegd.' : 'Voorraad gecorrigeerd.',
      setStockError,
    )
    if (ok) setStockDialog(null)
  }

  async function handleGenerateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!generateDialog || !detail) return
    const dimensions = detail.attributes.map((attribute) => ({
      attributeDefinitionId: attribute.id,
      optionIds: generateDialog[attribute.id] ?? [],
    }))
    if (dimensions.some((d) => d.optionIds.length === 0)) {
      setGenerateError('Kies per attribuut minstens één waarde.')
      return
    }
    const ok = await run(() => generateVariants(id, dimensions), 'Varianten gegenereerd.', setGenerateError)
    if (ok) setGenerateDialog(null)
  }

  if (loadError) {
    return <p className="placeholder-text">{loadError}</p>
  }

  if (!detail) {
    return <p className="placeholder-text">Laden…</p>
  }

  const template = detail.template

  const tabs: TabItem[] = [
    { id: 'algemeen', label: 'Algemeen' },
    ...(template.stockTrackingEnabled && !template.variantsEnabled ? [{ id: 'voorraad', label: 'Voorraad' }] : []),
    ...(template.stockTrackingEnabled && template.variantsEnabled
      ? [{ id: 'varianten', label: 'Varianten & voorraad', badge: detail.variants.length || undefined }]
      : []),
    { id: 'houders', label: 'Huidige houders', badge: visibleHolders?.length || undefined },
    ...(template.stockTrackingEnabled ? [{ id: 'bewegingen', label: 'Voorraadhistoriek' }] : []),
  ]
  const rawTab = searchParams.get('tab') ?? 'algemeen'
  const requestedTab = (TAB_ALIASES[rawTab] ?? rawTab) as DetailTab
  const activeTab: DetailTab = tabs.some((t) => t.id === requestedTab) ? requestedTab : 'algemeen'
  const setActiveTab = (tabId: string) => {
    const next = new URLSearchParams(searchParams)
    next.set('tab', tabId)
    setSearchParams(next, { replace: true })
  }

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: 'Instellingen', to: '/settings' },
          { label: 'Bedrijfsmiddelen', to: '/settings/issued-item-templates' },
          { label: template.name },
        ]}
      />
      <PageHeader
        title={template.name}
        subtitle={template.description ?? template.category}
        action={
          canManageInventory ? <Button onClick={() => setTemplateEditorOpen(true)}>Sjabloon bewerken</Button> : undefined
        }
      />

      <Tabs tabs={tabs} activeId={activeTab} onChange={setActiveTab} />

      {activeTab === 'algemeen' && (
      <TabPanel tabId="algemeen">
      <section className="issued-items-card">
        <h2>Instellingen</h2>
        <dl className="issued-items-summary">
          <div>
            <dt>Categorie</dt>
            <dd>{template.category}</dd>
          </div>
          <div>
            <dt>Voorraadbeheer</dt>
            <dd>{template.stockTrackingEnabled ? 'Aan' : 'Uit'}</dd>
          </div>
          <div>
            <dt>Varianten</dt>
            <dd>{template.variantsEnabled ? 'Aan' : 'Uit'}</dd>
          </div>
          <div>
            <dt>Serienummer verplicht</dt>
            <dd>{template.requiresSerialNumber ? 'Ja' : 'Nee'}</dd>
          </div>
          <div>
            <dt>Retour verplicht</dt>
            <dd>{template.returnRequired ? 'Ja' : 'Nee'}</dd>
          </div>
          {template.stockTrackingEnabled && (
            <>
              <div>
                <dt>Beschikbaar</dt>
                <dd>
                  {template.totalAvailable}
                  {template.unit ? ` ${template.unit}` : ''}{' '}
                  {template.lowStock && <Badge tone="warning">Lage voorraad</Badge>}
                </dd>
              </div>
              {template.lowStockThreshold !== null && (
                <div>
                  <dt>Lage-voorraadgrens</dt>
                  <dd>{template.lowStockThreshold}</dd>
                </div>
              )}
              {template.storageLocation && (
                <div>
                  <dt>Opslaglocatie</dt>
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
            <h2>Attributen</h2>
            {canManageInventory && (
              <div className="issued-items-card-actions">
                <select
                  aria-label="Attribuut koppelen"
                  value={attributePickerValue}
                  disabled={busy || linkableDefinitions.length === 0}
                  onChange={(e) => {
                    setAttributePickerValue(e.target.value)
                    if (e.target.value) void linkAttribute(e.target.value)
                  }}
                >
                  <option value="">
                    {linkableDefinitions.length === 0 ? 'Geen herbruikbare attributen over' : '+ Bestaand attribuut koppelen'}
                  </option>
                  {linkableDefinitions.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
                <Button variant="secondary" onClick={() => setNewAttribute({ name: '', allowCustomValues: false })} disabled={busy}>
                  Nieuw attribuut
                </Button>
              </div>
            )}
          </div>
          {detail.attributes.length === 0 && (
            <p className="placeholder-text">
              Geen attributen gekoppeld: varianten krijgen een vrije naam (bv. Small, maat 43). Koppel bv. Maat of
              Kleur wanneer je combinaties wilt genereren.
            </p>
          )}
          {detail.attributes.map((attribute) => (
            <div key={attribute.id} className="issued-items-attribute">
              <div className="issued-items-attribute-header">
                <strong>{attribute.name}</strong>
                {attribute.isShared && <Badge tone="info">Herbruikbaar</Badge>}
                {attribute.allowCustomValues && <Badge tone="neutral">Vrije waarden</Badge>}
                {canManageInventory && (
                  <button type="button" className="issued-items-link" onClick={() => unlinkAttribute(attribute.id)} disabled={busy}>
                    Loskoppelen
                  </button>
                )}
              </div>
              <div className="issued-items-attribute-options">
                {attribute.options.filter((o) => o.isActive).map((option) => (
                  <span key={option.id} className="issued-items-chip">
                    {option.value}
                  </span>
                ))}
                {attribute.options.length === 0 && <span className="customer-form-muted">Nog geen waarden.</span>}
                {canManageInventory && (
                  <span className="issued-items-option-add">
                    <input
                      aria-label={`Nieuwe waarde voor ${attribute.name}`}
                      placeholder="Nieuwe waarde…"
                      value={optionDrafts[attribute.id] ?? ''}
                      maxLength={100}
                      onChange={(e) => setOptionDrafts((d) => ({ ...d, [attribute.id]: e.target.value }))}
                    />
                    <Button variant="secondary" onClick={() => handleAddOption(attribute.id)} disabled={busy}>
                      Toevoegen
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
              Varianten & voorraad{' '}
              <span className="issued-items-computed-stock">— totale voorraad: {template.totalAvailable} (berekend)</span>
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
                  Varianten genereren
                </Button>
                <Button variant="secondary" onClick={() => openVariantEditor(null)} disabled={busy}>
                  + Variant toevoegen
                </Button>
              </div>
            )}
          </div>
          {detail.variants.length === 0 && (
            <p className="placeholder-text">
              Nog geen varianten. Kies "Varianten genereren" om alle combinaties (bv. maat × kleur) in één keer aan te maken.
            </p>
          )}
          {detail.variants.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>Variant</th>
                  <th>Voorraad</th>
                  <th>Status</th>
                  <th aria-label="Acties" />
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
                          <Badge tone="warning">Laag</Badge>
                        )}
                      </span>
                    </td>
                    <td>
                      <Badge tone={variant.isActive ? 'success' : 'neutral'}>{variant.isActive ? 'Actief' : 'Gearchiveerd'}</Badge>
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
                          + Voorraad
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
                          Corrigeren
                        </button>
                      )}
                      {canManageInventory && (
                        <button type="button" className="issued-items-link" onClick={() => openVariantEditor(variant)}>
                          Bewerken
                        </button>
                      )}
                      {canManageInventory && (
                        <button
                          type="button"
                          className="issued-items-link issued-items-link-danger"
                          onClick={() => setVariantDeleteTarget(variant)}
                        >
                          Verwijderen
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </TabPanel>
      )}

      {activeTab === 'voorraad' && (
      <TabPanel tabId="voorraad">
        <section className="issued-items-card">
          <div className="issued-items-card-header">
            <h2>Voorraad</h2>
            {canAdjustStock && (
              <div className="issued-items-card-actions">
                <Button
                  onClick={() => {
                    setStockError(null)
                    setStockDialog({ kind: 'receipt', variantId: null, quantity: '', reason: '', notes: '' })
                  }}
                  disabled={busy}
                >
                  Voorraad toevoegen
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setStockError(null)
                    setStockDialog({ kind: 'correction', variantId: null, quantity: String(template.currentStock), reason: '', notes: '' })
                  }}
                  disabled={busy}
                >
                  Corrigeren
                </Button>
              </div>
            )}
          </div>
          <p className="issued-items-stock-figure">
            {template.currentStock}
            {template.unit ? ` ${template.unit}` : ''} beschikbaar {template.lowStock && <Badge tone="warning">Lage voorraad</Badge>}
          </p>
          {template.lowStockThreshold !== null && (
            <p className="issued-items-computed-stock">Lage-voorraadgrens: {template.lowStockThreshold}</p>
          )}
        </section>
      </TabPanel>
      )}

      {activeTab === 'bewegingen' && (
      <TabPanel tabId="bewegingen">
        <section className="issued-items-card">
          <h2>Voorraadhistoriek</h2>
          {visibleMovements === null && <p className="placeholder-text">Laden…</p>}
          {visibleMovements !== null && visibleMovements.length === 0 && <p className="placeholder-text">Nog geen voorraadbewegingen.</p>}
          {visibleMovements !== null && visibleMovements.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>Datum</th>
                  <th>Type</th>
                  <th>Variant</th>
                  <th>Aantal</th>
                  <th>Resultaat</th>
                  <th>Reden / notitie</th>
                  <th>Medewerker</th>
                </tr>
              </thead>
              <tbody>
                {visibleMovements.map((movement) => (
                  <tr key={movement.id}>
                    <td>{new Date(movement.timestamp).toLocaleString('nl-BE')}</td>
                    <td>{STOCK_MOVEMENT_LABELS[movement.movementType]}</td>
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
        <h2>Huidige houders</h2>
        {!stockTracked && (
          <p className="placeholder-text">Beschikbaar zodra voorraadbeheer aan staat, of via de medewerkerfiches.</p>
        )}
        {stockTracked && visibleHolders === null && <p className="placeholder-text">Laden…</p>}
        {visibleHolders !== null && visibleHolders.length === 0 && <p className="placeholder-text">Niemand heeft dit middel momenteel in gebruik.</p>}
        {visibleHolders !== null && visibleHolders.length > 0 && (
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Medewerker</th>
                <th>Nummer</th>
                <th>Variant</th>
                <th>Aantal</th>
                <th>Uitgereikt</th>
                <th>Serienr.</th>
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
            showSuccess('Sjabloon bijgewerkt.')
            setTemplateEditorOpen(false)
            reload()
          }}
        />
      )}

      {newAttribute && (
        <Modal
          title="Nieuw attribuut"
          onClose={() => setNewAttribute(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setNewAttribute(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="new-attribute-form" disabled={busy}>
                Aanmaken
              </Button>
            </>
          }
        >
          <form id="new-attribute-form" className="issued-items-form" onSubmit={handleCreateAttribute} noValidate>
            <FormField label="Naam" htmlFor="attr-name" required hint="bv. Maat, Kleur, Opslag, Model">
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
              <span>Vrije waarden toestaan naast de vaste lijst</span>
            </label>
            <p className="customer-form-muted">
              Attributen zijn herbruikbare stamgegevens: eenmaal aangemaakt kan elk sjabloon ze gebruiken.
            </p>
          </form>
        </Modal>
      )}

      {variantEditor && (
        <Modal
          title={variantEditor.variant ? `Variant bewerken — ${variantEditor.variant.label}` : 'Variant toevoegen'}
          onClose={() => setVariantEditor(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setVariantEditor(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="variant-form" disabled={busy}>
                Opslaan
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
              <FormField label="Variantnaam / uitvoering" htmlFor="var-label" required hint="Bv. Small, maat 43, zwart.">
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
                      <option value="">{attribute.allowCustomValues ? '— Vrije waarde —' : '— Kies waarde —'}</option>
                      {attribute.options.filter((o) => o.isActive).map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.value}
                        </option>
                      ))}
                    </select>
                    {attribute.allowCustomValues && !draft.optionId && (
                      <input
                        aria-label={`Vrije waarde voor ${attribute.name}`}
                        placeholder="Vrije waarde…"
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
              <FormField label="Beginvoorraad" htmlFor="var-initial" hint="Optioneel; kan ook later via een ontvangst.">
                <input
                  id="var-initial"
                  type="number"
                  min={0}
                  value={variantEditor.initialStock}
                  onChange={(e) => setVariantEditor((s) => (s ? { ...s, initialStock: e.target.value } : s))}
                />
              </FormField>
            )}
            <label className="issued-items-checkbox">
              <input
                type="checkbox"
                checked={variantEditor.isActive}
                onChange={(e) => setVariantEditor((s) => (s ? { ...s, isActive: e.target.checked } : s))}
              />
              <span>Actief (uitreikbaar)</span>
            </label>
          </form>
        </Modal>
      )}

      {stockDialog && (
        <Modal
          title={stockDialog.kind === 'receipt' ? 'Voorraad toevoegen' : 'Voorraad corrigeren'}
          onClose={() => setStockDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setStockDialog(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="stock-form" disabled={busy}>
                Opslaan
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
                Variant: {detail.variants.find((v) => v.id === stockDialog.variantId)?.label ?? '—'}
              </p>
            )}
            <FormField
              label={stockDialog.kind === 'receipt' ? 'Aantal ontvangen' : 'Nieuwe voorraad'}
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
              <FormField label="Reden" htmlFor="stock-reason" required hint="Verplicht bij een correctie.">
                <input
                  id="stock-reason"
                  value={stockDialog.reason}
                  onChange={(e) => setStockDialog((s) => (s ? { ...s, reason: e.target.value } : s))}
                  maxLength={300}
                />
              </FormField>
            )}
            {stockDialog.kind === 'receipt' && (
              <FormField label="Notitie" htmlFor="stock-notes">
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
          title="Varianten genereren"
          onClose={() => setGenerateDialog(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setGenerateDialog(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="generate-variants-form" disabled={busy}>
                Genereren
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
              Kies per attribuut de gewenste waarden; elke combinatie wordt één variant met eigen voorraad. Bestaande
              combinaties blijven ongewijzigd.
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
                  <p className="customer-form-muted">Voeg eerst waarden toe bij het attribuut.</p>
                )}
              </fieldset>
            ))}
          </form>
        </Modal>
      )}

      {variantDeleteTarget && (
        <ConfirmDialog
          title="Variant verwijderen"
          message={`Weet je zeker dat je variant "${variantDeleteTarget.label}" wilt verwijderen? Dit kan alleen bij voorraad 0; uitgereikte items behouden hun label.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleVariantDelete}
          onCancel={() => setVariantDeleteTarget(null)}
        />
      )}
    </div>
  )
}
