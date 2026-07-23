import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
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
  const { showSuccess, showError } = useToast()
  const [templates, setTemplates] = useState<IssuedItemTemplate[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Sjablonen konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [reloadToken])

  const categories = useMemo(
    () => [...new Set((templates ?? []).map((t) => t.category))].sort((a, b) => a.localeCompare(b)),
    [templates],
  )

  const visible = useMemo(
    () =>
      (templates ?? []).filter((t) => {
        if (categoryFilter && t.category !== categoryFilter) return false
        if (activeFilter === 'active' && !t.isActive) return false
        if (activeFilter === 'inactive' && t.isActive) return false
        if (stockFilter === 'managed' && !t.stockTrackingEnabled) return false
        if (stockFilter === 'unmanaged' && t.stockTrackingEnabled) return false
        if (stockFilter === 'low' && !(t.stockTrackingEnabled && t.lowStock)) return false
        return true
      }),
    [templates, categoryFilter, activeFilter, stockFilter],
  )

  async function handleDelete() {
    if (!deleteTarget) return
    try {
      await deleteIssuedItemTemplate(deleteTarget.id)
      showSuccess('Sjabloon verwijderd.')
      setDeleteTarget(null)
      setReloadToken((t) => t + 1)
    } catch {
      showError('Het sjabloon kon niet worden verwijderd.')
      setDeleteTarget(null)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Bedrijfsmiddelen' }]} />
      <PageHeader
        title="Bedrijfsmiddelen — sjablonen"
        subtitle="Standaardlijst die per medewerker als checklist wordt aangeboden; met optioneel voorraadbeheer."
        action={
          <Button
            onClick={() => {
              setEditing(null)
              setEditorOpen(true)
            }}
          >
            Sjabloon toevoegen
          </Button>
        }
      />

      <div className="issued-items-filters">
        <FormField label="Categorie" htmlFor="tpl-filter-cat">
          <select id="tpl-filter-cat" value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)}>
            <option value="">Alle categorieën</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Status" htmlFor="tpl-filter-active">
          <select id="tpl-filter-active" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as typeof activeFilter)}>
            <option value="active">Actief</option>
            <option value="inactive">Inactief</option>
            <option value="all">Alles</option>
          </select>
        </FormField>
        <FormField label="Voorraad" htmlFor="tpl-filter-stock">
          <select id="tpl-filter-stock" value={stockFilter} onChange={(e) => setStockFilter(e.target.value as StockFilter)}>
            <option value="all">Alles</option>
            <option value="managed">Met voorraadbeheer</option>
            <option value="unmanaged">Zonder voorraadbeheer</option>
            <option value="low">Lage voorraad</option>
          </select>
        </FormField>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && templates === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && templates !== null && visible.length === 0 && (
        <p className="placeholder-text">
          {templates.length === 0 ? 'Nog geen sjablonen. Voeg er een toe om te beginnen.' : 'Geen sjablonen voor deze filters.'}
        </p>
      )}

      {!loadError && templates !== null && visible.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Categorie</th>
              <th>Voorraadbeheer</th>
              <th>Varianten</th>
              <th>Beschikbaar</th>
              <th>Serienr.</th>
              <th>Retour</th>
              <th>Status</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {visible.map((t) => (
              <tr key={t.id}>
                <td>
                  <Link className="issued-items-link" to={`/settings/issued-item-templates/${t.id}`}>
                    {t.name}
                  </Link>
                </td>
                <td>{t.category}</td>
                <td>{t.stockTrackingEnabled ? 'Ja' : 'Nee'}</td>
                <td>{t.variantsEnabled ? t.variantCount : '—'}</td>
                <td>
                  {t.stockTrackingEnabled ? (
                    <span className="issued-items-stock-cell">
                      {t.totalAvailable}
                      {t.unit ? ` ${t.unit}` : ''}
                      {t.lowStock && <Badge tone="warning">Lage voorraad</Badge>}
                    </span>
                  ) : (
                    '—'
                  )}
                </td>
                <td>{t.requiresSerialNumber ? 'Ja' : 'Nee'}</td>
                <td>{t.returnRequired ? 'Ja' : 'Nee'}</td>
                <td>
                  <Badge tone={t.isActive ? 'success' : 'neutral'}>{t.isActive ? 'Actief' : 'Inactief'}</Badge>
                </td>
                <td className="issued-items-row-actions">
                  <button
                    type="button"
                    className="issued-items-link"
                    onClick={() => {
                      setEditing(t)
                      setEditorOpen(true)
                    }}
                  >
                    Bewerken
                  </button>
                  <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteTarget(t)}>
                    Verwijderen
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
          onSaved={() => {
            showSuccess(editing ? 'Sjabloon bijgewerkt.' : 'Sjabloon toegevoegd.')
            setEditorOpen(false)
            setReloadToken((t) => t + 1)
          }}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Sjabloon verwijderen"
          message={`Weet je zeker dat je "${deleteTarget.name}" wilt verwijderen? Reeds uitgereikte middelen blijven behouden.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
