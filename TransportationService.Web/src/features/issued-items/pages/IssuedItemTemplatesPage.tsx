import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import {
  createIssuedItemTemplate,
  deleteIssuedItemTemplate,
  listIssuedItemTemplates,
  updateIssuedItemTemplate,
  type IssuedItemTemplate,
  type IssuedItemTemplateInput,
} from '../issuedItemsApi'
import '../issued-items.css'

function emptyForm(): IssuedItemTemplateInput {
  return {
    name: '',
    category: 'Algemeen',
    applicableJobFunctionCodes: null,
    defaultQuantity: 1,
    requiresSerialNumber: false,
    requiresReceivedDate: true,
    returnRequired: false,
    isActive: true,
    sortOrder: 0,
  }
}

/** Settings page to manage issued-item ("Bedrijfsmiddelen") templates. */
export function IssuedItemTemplatesPage() {
  const { showSuccess, showError } = useToast()
  const [templates, setTemplates] = useState<IssuedItemTemplate[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<IssuedItemTemplateInput>(emptyForm())
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
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

  function set<K extends keyof IssuedItemTemplateInput>(key: K, value: IssuedItemTemplateInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm())
    setFormError(null)
    setEditorOpen(true)
  }

  function openEdit(template: IssuedItemTemplate) {
    setEditingId(template.id)
    setForm({
      name: template.name,
      category: template.category,
      applicableJobFunctionCodes: template.applicableJobFunctionCodes,
      defaultQuantity: template.defaultQuantity,
      requiresSerialNumber: template.requiresSerialNumber,
      requiresReceivedDate: template.requiresReceivedDate,
      returnRequired: template.returnRequired,
      isActive: template.isActive,
      sortOrder: template.sortOrder,
    })
    setFormError(null)
    setEditorOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!form.name.trim() || !form.category.trim()) {
      setFormError('Naam en categorie zijn verplicht.')
      return
    }
    setSaving(true)
    try {
      if (editingId) {
        await updateIssuedItemTemplate(editingId, form)
        showSuccess('Sjabloon bijgewerkt.')
      } else {
        await createIssuedItemTemplate(form)
        showSuccess('Sjabloon toegevoegd.')
      }
      setEditorOpen(false)
      setReloadToken((t) => t + 1)
    } catch {
      setFormError('Het sjabloon kon niet worden opgeslagen.')
    } finally {
      setSaving(false)
    }
  }

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
        subtitle="Standaardlijst die per medewerker als checklist wordt aangeboden."
        action={<Button onClick={openCreate}>Sjabloon toevoegen</Button>}
      />

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && templates === null && <p className="placeholder-text">Laden…</p>}
      {!loadError && templates !== null && templates.length === 0 && (
        <p className="placeholder-text">Nog geen sjablonen. Voeg er een toe om te beginnen.</p>
      )}

      {!loadError && templates !== null && templates.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Naam</th>
              <th>Categorie</th>
              <th>Aantal</th>
              <th>Serienr.</th>
              <th>Retour</th>
              <th>Status</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {templates.map((t) => (
              <tr key={t.id}>
                <td>{t.name}</td>
                <td>{t.category}</td>
                <td>{t.defaultQuantity}</td>
                <td>{t.requiresSerialNumber ? 'Ja' : 'Nee'}</td>
                <td>{t.returnRequired ? 'Ja' : 'Nee'}</td>
                <td>
                  <Badge tone={t.isActive ? 'success' : 'neutral'}>{t.isActive ? 'Actief' : 'Inactief'}</Badge>
                </td>
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openEdit(t)}>
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
        <Modal
          title={editingId ? 'Sjabloon bewerken' : 'Sjabloon toevoegen'}
          onClose={() => setEditorOpen(false)}
          busy={saving}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditorOpen(false)} disabled={saving}>
                Annuleren
              </Button>
              <Button type="submit" form="template-form" disabled={saving}>
                {saving ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <form id="template-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="issued-items-form-error" role="alert">
                {formError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label="Naam" htmlFor="tpl-name" required>
                <input id="tpl-name" value={form.name} onChange={(e) => set('name', e.target.value)} disabled={saving} maxLength={150} />
              </FormField>
              <FormField label="Categorie" htmlFor="tpl-cat" required hint="bv. Algemeen / Chauffeur / Magazijn">
                <input id="tpl-cat" value={form.category} onChange={(e) => set('category', e.target.value)} disabled={saving} maxLength={100} />
              </FormField>
            </div>
            <FormField
              label="Toepasselijke functiecodes"
              htmlFor="tpl-functions"
              hint="Komma-gescheiden functiecodes; leeg = voor iedereen."
            >
              <input
                id="tpl-functions"
                value={form.applicableJobFunctionCodes ?? ''}
                onChange={(e) => set('applicableJobFunctionCodes', e.target.value || null)}
                disabled={saving}
                maxLength={300}
              />
            </FormField>
            <div className="issued-items-form-row">
              <FormField label="Standaardaantal" htmlFor="tpl-qty">
                <input id="tpl-qty" type="number" min={1} value={form.defaultQuantity} onChange={(e) => set('defaultQuantity', Number(e.target.value) || 1)} disabled={saving} />
              </FormField>
              <FormField label="Volgorde" htmlFor="tpl-sort">
                <input id="tpl-sort" type="number" value={form.sortOrder} onChange={(e) => set('sortOrder', Number(e.target.value) || 0)} disabled={saving} />
              </FormField>
            </div>
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.requiresSerialNumber} onChange={(e) => set('requiresSerialNumber', e.target.checked)} disabled={saving} />
              <span>Serienummer vereist</span>
            </label>
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.requiresReceivedDate} onChange={(e) => set('requiresReceivedDate', e.target.checked)} disabled={saving} />
              <span>Uitreikingsdatum vereist</span>
            </label>
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.returnRequired} onChange={(e) => set('returnRequired', e.target.checked)} disabled={saving} />
              <span>Moet worden teruggebracht</span>
            </label>
            <label className="issued-items-checkbox">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} disabled={saving} />
              <span>Actief</span>
            </label>
          </form>
        </Modal>
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
