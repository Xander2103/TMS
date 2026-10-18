import { useEffect, useMemo, useState } from 'react'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useUrlListState } from '../../../hooks/useUrlListState'
import { Link, useNavigate } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { RowActionMenu } from '../../../components/ui/RowActionMenu'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import { getReferenceConflict, type ReferenceConflict } from '../../../api/conflicts'
import { ReferenceConflictDialog } from '../../../components/ui/ReferenceConflictDialog'
import {
  deleteIssuedItemTemplate,
  listIssuedItemTemplates,
  templateToInput,
  updateIssuedItemTemplate,
  type IssuedItemTemplate,
} from '../issuedItemsApi'
import { formatQuantityWithUnit } from '../issuedItemUnits'
import { MANAGEMENT_MODES, MANAGEMENT_MODE_LABELS, modeFromFlags, type ManagementMode } from '../managementMode'
import { TemplateFormModal } from '../TemplateFormModal'
import '../issued-items.css'

type ModeFilter = 'all' | ManagementMode | 'low'

/**
 * The "Bedrijfsmiddelen" overview: what the company issues to employees, each with its
 * Beheerwijze (how it is tracked) instead of technical yes/no columns. Embedded without its
 * own page header so the surrounding page owns title, breadcrumbs and tabs.
 */
const PANEL_DEFAULTS: Record<'q' | 'category' | 'active' | 'mode', string> = { q: '', category: '', active: 'active', mode: 'all' }
const PANEL_ALLOWED = { active: ['active', 'inactive', 'all'], mode: ['all', 'individual', 'stock', 'variants', 'low'] }

export function IssuedItemTemplatesPanel() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const navigate = useNavigate()
  const [templates, setTemplates] = useState<IssuedItemTemplate[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  // WH-M9: search and filters live in the URL — reload and Back keep the view.
  const { state: listState, apply } = useUrlListState({ defaults: PANEL_DEFAULTS, allowed: PANEL_ALLOWED })
  const categoryFilter = listState.category
  const activeFilter = listState.active as 'all' | 'active' | 'inactive'
  const modeFilter = listState.mode as ModeFilter
  const search = listState.q
  const [deactivating, setDeactivating] = useState(false)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<IssuedItemTemplate | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<IssuedItemTemplate | null>(null)
  const [deleteConflict, setDeleteConflict] = useState<{ template: IssuedItemTemplate; conflict: ReferenceConflict } | null>(null)

  useEffect(() => {
    let mounted = true
    listIssuedItemTemplates(true)
      .then((data) => {
        if (!mounted) return
        setTemplates(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('issuedItems.templates.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [reloadToken])

  const categories = useMemo(
    () => [...new Set((templates ?? []).map((tpl) => tpl.category))].sort((a, b) => a.localeCompare(b)),
    [templates],
  )

  const visible = useMemo(
    () =>
      (templates ?? []).filter((tpl) => {
        const query = search.trim().toLowerCase()
        if (query && !`${tpl.code ?? ''} ${tpl.name} ${tpl.description ?? ''}`.toLowerCase().includes(query)) return false
        if (categoryFilter && tpl.category !== categoryFilter) return false
        if (activeFilter === 'active' && !tpl.isActive) return false
        if (activeFilter === 'inactive' && tpl.isActive) return false
        if (modeFilter === 'low') return tpl.stockTrackingEnabled && tpl.lowStock
        if (modeFilter !== 'all' && modeFromFlags(tpl) !== modeFilter) return false
        return true
      }),
    [templates, categoryFilter, activeFilter, modeFilter, search],
  )

  /** WH-B1: the way out of a refused delete — deactivate in one click, history stays readable. */
  async function deactivate(template: IssuedItemTemplate) {
    setDeactivating(true)
    try {
      await updateIssuedItemTemplate(template.id, { ...templateToInput(template), isActive: false, stock: null })
      showSuccess(t('issuedItems.templates.deactivated', { name: template.name }))
      setDeleteConflict(null)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('issuedItems.templates.deactivateFailed')).message)
    } finally {
      setDeactivating(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteIssuedItemTemplate(deleteTarget.id)
      showSuccess(t('issuedItems.templates.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch (err) {
      // Issued/stock history still points at the item (409 record_referenced): explain why,
      // with the counts, instead of a generic failure.
      const conflict = getReferenceConflict(err)
      if (conflict) setDeleteConflict({ template: deleteTarget, conflict })
      else showError(describeApiError(err, t('issuedItems.templates.deleteFailed')).message)
      setDeleteTarget(null)
    }
  }

  return (
    <div className="issued-items">
      <div className="issued-items-panel-toolbar">
        <p className="issued-items-panel-intro">{t('issuedItems.templates.intro')}</p>
        <Button
          onClick={() => {
            setEditing(null)
            setEditorOpen(true)
          }}
        >
          {t('issuedItems.templates.add')}
        </Button>
      </div>

      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ q: value }, { replace: true })}
        searchPlaceholder={t('issuedItems.templates.searchPlaceholder')}
      />

      <div className="issued-items-filters">
        <FormField label={t('issuedItems.templates.filterCategory')} htmlFor="tpl-filter-cat">
          <select id="tpl-filter-cat" value={categoryFilter} onChange={(e) => apply({ category: e.target.value })}>
            <option value="">{t('issuedItems.templates.allCategories')}</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('issuedItems.templates.filterMode')} htmlFor="tpl-filter-mode">
          <select id="tpl-filter-mode" value={modeFilter} onChange={(e) => apply({ mode: e.target.value })}>
            <option value="all">{t('issuedItems.templates.optModeAll')}</option>
            {MANAGEMENT_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {t(MANAGEMENT_MODE_LABELS[mode])}
              </option>
            ))}
            <option value="low">{t('issuedItems.templates.optLow')}</option>
          </select>
        </FormField>
        <FormField label={t('issuedItems.templates.filterStatus')} htmlFor="tpl-filter-active">
          <select id="tpl-filter-active" value={activeFilter} onChange={(e) => apply({ active: e.target.value })}>
            <option value="active">{t('issuedItems.templates.optActive')}</option>
            <option value="inactive">{t('issuedItems.templates.optInactive')}</option>
            <option value="all">{t('issuedItems.templates.optAll')}</option>
          </select>
        </FormField>
      </div>

      {loadErrorKey && <p className="placeholder-text">{t(loadErrorKey)}</p>}
      {!loadErrorKey && templates === null && <p className="placeholder-text">{t('issuedItems.templates.loading')}</p>}
      {!loadErrorKey && templates !== null && visible.length === 0 && (
        <p className="placeholder-text">
          {templates.length === 0 ? t('issuedItems.templates.emptyNone') : t('issuedItems.templates.emptyFiltered')}
        </p>
      )}

      {!loadErrorKey && templates !== null && visible.length > 0 && (
        <table className="issued-items-table issued-items-table-dense">
          <thead>
            <tr>
              <th>{t('issuedItems.templates.colCode')}</th>
              <th>{t('issuedItems.templates.colName')}</th>
              <th>{t('issuedItems.templates.colCategory')}</th>
              <th>{t('issuedItems.templates.colMode')}</th>
              <th>{t('issuedItems.templates.colAvailable')}</th>
              <th>{t('issuedItems.templates.colIssue')}</th>
              <th>{t('issuedItems.templates.colStatus')}</th>
              <th aria-label={t('issuedItems.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {visible.map((tpl) => {
              const mode = modeFromFlags(tpl)
              return (
                <tr key={tpl.id}>
                  <td>{tpl.code ? <code>{tpl.code}</code> : '—'}</td>
                  <td>
                    <Link className="issued-items-link issued-items-name-link" to={`/issued-items/templates/${tpl.id}`}>
                      <TruncatedText tooltip={tpl.name} className="issued-items-cell-name">{tpl.name}</TruncatedText>
                    </Link>
                  </td>
                  <td>
                    <TruncatedText tooltip={tpl.category} className="issued-items-cell-category">{tpl.category}</TruncatedText>
                  </td>
                  <td>
                    <span className="issued-items-mode-cell">
                      {t(MANAGEMENT_MODE_LABELS[mode])}
                      {mode === 'variants' && <Badge tone="neutral">{t('issuedItems.templates.variantCount', { count: tpl.variantCount })}</Badge>}
                    </span>
                  </td>
                  <td>
                    {tpl.stockTrackingEnabled ? (
                      <span className="issued-items-stock-cell">
                        {formatQuantityWithUnit(t, tpl.totalAvailable, tpl.unit)}
                        {tpl.lowStock && <Badge tone="warning">{t('issuedItems.templates.lowStock')}</Badge>}
                      </span>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td>
                    {tpl.requiresSerialNumber || tpl.returnRequired || tpl.isConsumable ? (
                      <span className="issued-items-badges">
                        {tpl.requiresSerialNumber && <Badge tone="neutral">{t('issuedItems.templates.badgeSerial')}</Badge>}
                        {tpl.returnRequired && <Badge tone="neutral">{t('issuedItems.templates.badgeReturn')}</Badge>}
                        {tpl.isConsumable && <Badge tone="neutral">{t('issuedItems.templates.badgeConsumable')}</Badge>}
                      </span>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td>
                    <Badge tone={tpl.isActive ? 'success' : 'neutral'}>
                      {tpl.isActive ? t('issuedItems.templates.active') : t('issuedItems.templates.inactive')}
                    </Badge>
                  </td>
                  {/* B14: the name opens the item; edit and delete live in one "⋯" menu. */}
                  <td className="issued-items-row-actions">
                    <RowActionMenu
                      label={t('issuedItems.templates.rowActions', { name: tpl.name })}
                      items={[
                        {
                          key: 'edit',
                          label: t('ui.actions.edit'),
                          onSelect: () => {
                            setEditing(tpl)
                            setEditorOpen(true)
                          },
                        },
                        { key: 'delete', label: t('ui.actions.delete'), danger: true, onSelect: () => setDeleteTarget(tpl) },
                      ]}
                    />
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <TemplateFormModal
          editing={editing}
          existingItems={templates ?? []}
          onClose={() => setEditorOpen(false)}
          onSaved={(saved, result) => {
            setEditorOpen(false)
            if (result && result.variantFailures.length > 0) {
              // The item exists; only some variants did not make it. Send the user straight to
              // the place where the missing ones can be added, with the labels in hand.
              showError(
                t('issuedItems.templates.variantsPartiallyFailed', {
                  labels: result.variantFailures.map((failure) => failure.label).join(', '),
                }),
              )
              navigate(`/issued-items/templates/${saved.id}?tab=varianten`)
              return
            }
            showSuccess(editing ? t('issuedItems.templates.updated') : t('issuedItems.templates.added'))
            setReloadToken((token) => token + 1)
          }}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('issuedItems.templates.deleteTitle')}
          message={t('issuedItems.templates.deleteMessage', { name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}

      {deleteConflict && (
        <ReferenceConflictDialog
          title={t('issuedItems.templates.deleteRefusedTitle')}
          conflict={deleteConflict.conflict}
          onClose={() => setDeleteConflict(null)}
          action={
            deleteConflict.conflict.canDeactivate && deleteConflict.template.isActive
              ? {
                  label: t('issuedItems.templates.deactivate'),
                  busy: deactivating,
                  onSelect: () => void deactivate(deleteConflict.template),
                }
              : undefined
          }
        />
      )}
    </div>
  )
}
