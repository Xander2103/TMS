import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import {
  deleteIssuedItemTemplate,
  listIssuedItemTemplates,
  type IssuedItemTemplate,
} from '../issuedItemsApi'
import { TemplateFormModal } from '../TemplateFormModal'
import '../issued-items.css'

type StockFilter = 'all' | 'managed' | 'unmanaged' | 'low'

/** Settings page to manage issued-item ("Bedrijfsmiddelen") templates and their stock state. */
export function IssuedItemTemplatesPage() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const navigate = useNavigate()
  const [templates, setTemplates] = useState<IssuedItemTemplate[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [categoryFilter, setCategoryFilter] = useState('')
  const [activeFilter, setActiveFilter] = useState<'all' | 'active' | 'inactive'>('active')
  const [stockFilter, setStockFilter] = useState<StockFilter>('all')

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<IssuedItemTemplate | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<IssuedItemTemplate | null>(null)

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
        if (categoryFilter && tpl.category !== categoryFilter) return false
        if (activeFilter === 'active' && !tpl.isActive) return false
        if (activeFilter === 'inactive' && tpl.isActive) return false
        if (stockFilter === 'managed' && !tpl.stockTrackingEnabled) return false
        if (stockFilter === 'unmanaged' && tpl.stockTrackingEnabled) return false
        if (stockFilter === 'low' && !(tpl.stockTrackingEnabled && tpl.lowStock)) return false
        return true
      }),
    [templates, categoryFilter, activeFilter, stockFilter],
  )

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteIssuedItemTemplate(deleteTarget.id)
      showSuccess(t('issuedItems.templates.deleted'))
      setDeleteTarget(null)
      setReloadToken((token) => token + 1)
    } catch {
      showError(t('issuedItems.templates.deleteFailed'))
      setDeleteTarget(null)
    }
  }

  const yesNo = (value: boolean) => (value ? t('issuedItems.templates.yes') : t('issuedItems.templates.no'))

  return (
    <div>
      <Breadcrumbs items={[{ label: t('issuedItems.templates.breadcrumbSettings'), to: '/settings' }, { label: t('issuedItems.templates.breadcrumb') }]} />
      <PageHeader
        title={t('issuedItems.templates.title')}
        subtitle={t('issuedItems.templates.subtitle')}
        action={
          <Button
            onClick={() => {
              setEditing(null)
              setEditorOpen(true)
            }}
          >
            {t('issuedItems.templates.add')}
          </Button>
        }
      />

      <div className="issued-items-filters">
        <FormField label={t('issuedItems.templates.filterCategory')} htmlFor="tpl-filter-cat">
          <select id="tpl-filter-cat" value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)}>
            <option value="">{t('issuedItems.templates.allCategories')}</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('issuedItems.templates.filterStatus')} htmlFor="tpl-filter-active">
          <select id="tpl-filter-active" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as typeof activeFilter)}>
            <option value="active">{t('issuedItems.templates.optActive')}</option>
            <option value="inactive">{t('issuedItems.templates.optInactive')}</option>
            <option value="all">{t('issuedItems.templates.optAll')}</option>
          </select>
        </FormField>
        <FormField label={t('issuedItems.templates.filterStock')} htmlFor="tpl-filter-stock">
          <select id="tpl-filter-stock" value={stockFilter} onChange={(e) => setStockFilter(e.target.value as StockFilter)}>
            <option value="all">{t('issuedItems.templates.optStockAll')}</option>
            <option value="managed">{t('issuedItems.templates.optManaged')}</option>
            <option value="unmanaged">{t('issuedItems.templates.optUnmanaged')}</option>
            <option value="low">{t('issuedItems.templates.optLow')}</option>
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
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('issuedItems.templates.colName')}</th>
              <th>{t('issuedItems.templates.colCategory')}</th>
              <th>{t('issuedItems.templates.colStockTracking')}</th>
              <th>{t('issuedItems.templates.colVariants')}</th>
              <th>{t('issuedItems.templates.colAvailable')}</th>
              <th>{t('issuedItems.templates.colSerial')}</th>
              <th>{t('issuedItems.templates.colReturn')}</th>
              <th>{t('issuedItems.templates.colStatus')}</th>
              <th aria-label={t('issuedItems.tab.colActions')} />
            </tr>
          </thead>
          <tbody>
            {visible.map((tpl) => (
              <tr key={tpl.id}>
                <td>
                  <Link className="issued-items-link" to={`/settings/issued-item-templates/${tpl.id}`}>
                    {tpl.name}
                  </Link>
                </td>
                <td>{tpl.category}</td>
                <td>{yesNo(tpl.stockTrackingEnabled)}</td>
                <td>{tpl.variantsEnabled ? tpl.variantCount : '—'}</td>
                <td>
                  {tpl.stockTrackingEnabled ? (
                    <span className="issued-items-stock-cell">
                      {tpl.totalAvailable}
                      {tpl.unit ? ` ${tpl.unit}` : ''}
                      {tpl.lowStock && <Badge tone="warning">{t('issuedItems.templates.lowStock')}</Badge>}
                    </span>
                  ) : (
                    '—'
                  )}
                </td>
                <td>{yesNo(tpl.requiresSerialNumber)}</td>
                <td>{yesNo(tpl.returnRequired)}</td>
                <td>
                  <Badge tone={tpl.isActive ? 'success' : 'neutral'}>
                    {tpl.isActive ? t('issuedItems.templates.active') : t('issuedItems.templates.inactive')}
                  </Badge>
                </td>
                <td className="issued-items-row-actions">
                  <button
                    type="button"
                    className="issued-items-link"
                    onClick={() => {
                      setEditing(tpl)
                      setEditorOpen(true)
                    }}
                  >
                    {t('ui.actions.edit')}
                  </button>
                  <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(tpl)}>
                    {t('ui.actions.delete')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editorOpen && (
        <TemplateFormModal
          editing={editing}
          onClose={() => setEditorOpen(false)}
          onSaved={(saved) => {
            showSuccess(editing ? t('issuedItems.templates.updated') : t('issuedItems.templates.added'))
            setEditorOpen(false)
            // A brand-new variant template needs its variants configured — take the user there.
            if (!editing && saved.variantsEnabled) {
              navigate(`/settings/issued-item-templates/${saved.id}?tab=varianten`)
              return
            }
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
    </div>
  )
}
