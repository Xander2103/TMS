import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import type { CustomerListItem } from '../../customers/types'
import {
  deleteMessageTemplate,
  getMessageTemplateKinds,
  getPlaceholders,
  listCustomerMessageTemplates,
  listMessageTemplates,
  previewTemplate,
  saveMessageTemplate,
  type PreviewResult,
} from '../api/notificationAdminApi'
import { kindLabel, type CustomerMessageTemplate, type MessageChannel, type MessageTemplate } from '../types'

const LANGUAGES = [
  { value: 'nl', label: 'Nederlands' },
  { value: 'fr', label: 'Frans' },
  { value: 'en', label: 'Engels' },
]

type ActiveField = 'subject' | 'body' | 'bodyHtml'

/** 'create' = brand new tenant default; 'override' = first customer-specific override for an
 * otherwise-inherited row; 'edit' = an existing row (tenant default or an already-overridden
 * customer row) — distinction is display-only, `saveMessageTemplate` upserts by
 * (kind, channel, language, customerId) regardless. */
type DraftMode = 'create' | 'override' | 'edit'

interface Draft {
  mode: DraftMode
  kind: string
  channel: MessageChannel
  language: string
  customerId: string | null
  subject: string
  body: string
  bodyHtml: string
  isActive: boolean
}

interface DeleteTarget {
  id: string
  label: string
  successMessage: string
}

const DRAFT_TITLES: Record<DraftMode, string> = {
  create: 'Sjabloon toevoegen',
  override: 'Klantspecifiek sjabloon aanmaken',
  edit: 'Sjabloon bewerken',
}

function insertTokenAt(
  el: HTMLTextAreaElement | HTMLInputElement | null,
  value: string,
  setValue: (v: string) => void,
  token: string,
) {
  const insertText = `{{${token}}}`
  if (!el) {
    setValue(value + insertText)
    return
  }
  const start = el.selectionStart ?? value.length
  const end = el.selectionEnd ?? value.length
  setValue(value.slice(0, start) + insertText + value.slice(end))
  requestAnimationFrame(() => {
    el.focus()
    const caret = start + insertText.length
    el.setSelectionRange(caret, caret)
  })
}

interface TemplatesTabProps {
  canManage: boolean
}

/** "Sjablonen" tab: tenant-wide message templates plus, per customer, the full effective-vs-
 * overridden round-trip — pick a customer to see every kind's effective template (inherited or
 * that customer's own override), edit either into an override, and delete an override back to
 * inherited. The editor's optional customer field creates the override; this tab is where you
 * find, re-edit and remove it afterwards. */
export function TemplatesTab({ canManage }: TemplatesTabProps) {
  const { showSuccess, showError } = useToast()
  const [templates, setTemplates] = useState<MessageTemplate[] | null>(null)
  const [customerTemplates, setCustomerTemplates] = useState<CustomerMessageTemplate[] | null>(null)
  const [scopeCustomerId, setScopeCustomerId] = useState<string | null>(null)
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  const [kinds, setKinds] = useState<string[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)

  const [draft, setDraft] = useState<Draft | null>(null)
  const [placeholders, setPlaceholders] = useState<string[]>([])
  const [activeField, setActiveField] = useState<ActiveField>('body')
  const [preview, setPreview] = useState<PreviewResult | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null)
  const [busy, setBusy] = useState(false)

  const subjectRef = useRef<HTMLInputElement>(null)
  const bodyRef = useRef<HTMLTextAreaElement>(null)
  const bodyHtmlRef = useRef<HTMLTextAreaElement>(null)

  const reload = useCallback(() => {
    if (!canManage) return
    if (scopeCustomerId) {
      listCustomerMessageTemplates(scopeCustomerId)
        .then((data) => {
          setCustomerTemplates(data)
          setLoadError(null)
        })
        .catch(() => setLoadError('De klantsjablonen konden niet worden geladen.'))
    } else {
      listMessageTemplates()
        .then((data) => {
          setTemplates(data)
          setLoadError(null)
        })
        .catch(() => setLoadError('De sjablonen konden niet worden geladen.'))
    }
  }, [canManage, scopeCustomerId])

  useEffect(() => {
    reload()
  }, [reload])

  useEffect(() => {
    if (!canManage) return
    getMessageTemplateKinds().then(setKinds).catch(() => setKinds([]))
    searchCustomers({ page: 1, pageSize: 500 })
      .then((result) => setCustomers(result.items))
      .catch(() => setCustomers([]))
  }, [canManage])

  useEffect(() => {
    if (!draft) return
    getPlaceholders(draft.kind).then(setPlaceholders).catch(() => setPlaceholders([]))
    // Deliberately keyed on the kind alone: re-running on every keystroke elsewhere in the draft
    // would refetch the (static, kind-derived) token list for no reason.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft?.kind])

  const customerOptions: SearchableSelectOption[] = customers.map((c) => ({ value: c.id, label: `${c.customerNumber} — ${c.name}` }))
  const scopeCustomerName = customers.find((c) => c.id === scopeCustomerId)?.name ?? ''

  function resetDraftState() {
    setPreview(null)
    setDraftError(null)
    setFieldErrors({})
  }

  function openNew() {
    setDraft({
      mode: 'create',
      kind: kinds[0] ?? '',
      channel: 'Email',
      language: 'nl',
      customerId: scopeCustomerId,
      subject: '',
      body: '',
      bodyHtml: '',
      isActive: true,
    })
    resetDraftState()
  }

  function openEdit(template: MessageTemplate) {
    setDraft({
      mode: 'edit',
      kind: template.kind,
      channel: template.channel,
      language: template.language,
      customerId: template.customerId,
      subject: template.subject ?? '',
      body: template.body,
      bodyHtml: template.bodyHtml ?? '',
      isActive: template.isActive,
    })
    resetDraftState()
  }

  /** Editing an effective row from the customer-scoped table: an inherited (non-overridden) row
   * starts a new override pre-filled with the default's own content; an already-overridden row
   * edits that override directly. Either way `saveMessageTemplate` upserts by
   * (kind, channel, language, customerId), so no id is needed here. */
  function openCustomerRow(row: CustomerMessageTemplate) {
    if (!scopeCustomerId) return
    setDraft({
      mode: row.isOverridden ? 'edit' : 'override',
      kind: row.kind,
      channel: row.channel,
      language: row.language,
      customerId: scopeCustomerId,
      subject: row.subject ?? '',
      body: row.body,
      bodyHtml: row.bodyHtml ?? '',
      isActive: row.isActive,
    })
    resetDraftState()
  }

  function insertToken(token: string) {
    if (!draft) return
    if (activeField === 'subject') insertTokenAt(subjectRef.current, draft.subject, (v) => setDraft({ ...draft, subject: v }), token)
    else if (activeField === 'body') insertTokenAt(bodyRef.current, draft.body, (v) => setDraft({ ...draft, body: v }), token)
    else insertTokenAt(bodyHtmlRef.current, draft.bodyHtml, (v) => setDraft({ ...draft, bodyHtml: v }), token)
  }

  async function loadPreview() {
    if (!draft) return
    setBusy(true)
    try {
      setPreview(await previewTemplate({ kind: draft.kind, channel: draft.channel, language: draft.language, tokens: null }))
    } catch {
      showError('De preview kon niet worden geladen.')
    } finally {
      setBusy(false)
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    if (!draft.body.trim()) {
      setDraftError('De inhoud van het sjabloon is verplicht.')
      return
    }
    setBusy(true)
    setDraftError(null)
    setFieldErrors({})
    try {
      await saveMessageTemplate({
        kind: draft.kind,
        channel: draft.channel,
        language: draft.language,
        subject: draft.subject.trim() || null,
        body: draft.body,
        bodyHtml: draft.bodyHtml.trim() || null,
        isActive: draft.isActive,
        customerId: draft.customerId,
      })
      showSuccess(draft.customerId ? 'Klantspecifiek sjabloon opgeslagen.' : 'Sjabloon opgeslagen.')
      setDraft(null)
      reload()
    } catch (err) {
      const described = describeApiError(err, 'Het sjabloon kon niet worden opgeslagen.')
      setDraftError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  async function confirmDelete() {
    const target = deleteTarget
    if (!target) return
    setDeleteTarget(null)
    try {
      await deleteMessageTemplate(target.id)
      showSuccess(target.successMessage)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'Het sjabloon kon niet worden verwijderd.').message)
    }
  }

  if (!canManage) return null
  if (loadError) return <p className="placeholder-text">{loadError}</p>

  return (
    <div>
      <div className="notification-admin-toolbar">
        <div className="notification-admin-customer-picker">
          <SearchableSelect
            ariaLabel="Klantweergave"
            value={scopeCustomerId}
            onChange={setScopeCustomerId}
            options={customerOptions}
            placeholder="Standaard (alle klanten) — kies een klant voor klantspecifieke sjablonen"
          />
        </div>
        <Button onClick={openNew}>{scopeCustomerId ? '+ Klantspecifiek sjabloon' : '+ Sjabloon'}</Button>
      </div>

      {!scopeCustomerId && templates === null && <p className="placeholder-text">Sjablonen laden…</p>}
      {!scopeCustomerId && templates !== null && templates.length === 0 && (
        <p className="placeholder-text">Nog geen eigen sjablonen — ingebouwde standaardteksten worden gebruikt.</p>
      )}
      {!scopeCustomerId && templates !== null && templates.length > 0 && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>Kanaal</th>
              <th>Taal</th>
              <th>Bereik</th>
              <th>Status</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {templates.map((template) => (
              <tr key={template.id}>
                <td>{kindLabel(template.kind)}</td>
                <td>{template.channel === 'Email' ? 'E-mail' : 'SMS'}</td>
                <td>{template.language}</td>
                <td>
                  <Badge tone="neutral">Standaard</Badge>
                </td>
                <td>{!template.isActive && <Badge tone="neutral">inactief</Badge>}</td>
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openEdit(template)}>
                    Bewerken
                  </button>
                  <button
                    type="button"
                    className="issued-items-link issued-items-link-danger"
                    onClick={() =>
                      setDeleteTarget({
                        id: template.id,
                        label: kindLabel(template.kind),
                        successMessage: 'Sjabloon verwijderd — het ingebouwde sjabloon geldt weer.',
                      })
                    }
                  >
                    Verwijderen
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {scopeCustomerId && customerTemplates === null && <p className="placeholder-text">Klantsjablonen laden…</p>}
      {scopeCustomerId && customerTemplates !== null && (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>Kanaal</th>
              <th>Taal</th>
              <th>Bereik</th>
              <th>Status</th>
              <th aria-label="Acties" />
            </tr>
          </thead>
          <tbody>
            {customerTemplates.map((row) => (
              <tr key={`${row.kind}:${row.channel}:${row.language}`}>
                <td>{kindLabel(row.kind)}</td>
                <td>{row.channel === 'Email' ? 'E-mail' : 'SMS'}</td>
                <td>{row.language}</td>
                <td>
                  {row.isOverridden ? (
                    <Badge tone="info">Klantspecifiek: {scopeCustomerName}</Badge>
                  ) : (
                    <Badge tone="neutral">Standaard</Badge>
                  )}
                </td>
                <td>{!row.isActive && <Badge tone="neutral">inactief</Badge>}</td>
                <td className="issued-items-row-actions">
                  <button type="button" className="issued-items-link" onClick={() => openCustomerRow(row)}>
                    Bewerken
                  </button>
                  {row.isOverridden && row.id && (
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() =>
                        setDeleteTarget({
                          id: row.id!,
                          label: kindLabel(row.kind),
                          successMessage: `Klantspecifiek sjabloon verwijderd — standaard geldt weer voor ${scopeCustomerName}.`,
                        })
                      }
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

      {draft && (
        <Modal
          title={DRAFT_TITLES[draft.mode]}
          onClose={() => setDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => void loadPreview()} disabled={busy}>
                Voorbeeld
              </Button>
              <Button type="submit" form="notification-admin-template-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="notification-admin-template-form" className="notification-admin-form" onSubmit={submit} noValidate>
            {draftError && (
              <div role="alert" className="notification-admin-form-error">
                {draftError}
              </div>
            )}
            <div className="notification-admin-form-row">
              <FormField label="Type" htmlFor="nat-kind">
                <select
                  id="nat-kind"
                  value={draft.kind}
                  disabled={busy}
                  onChange={(e) => setDraft({ ...draft, kind: e.target.value })}
                >
                  {kinds.map((kind) => (
                    <option key={kind} value={kind}>
                      {kindLabel(kind)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Kanaal" htmlFor="nat-channel">
                <select
                  id="nat-channel"
                  value={draft.channel}
                  disabled={busy}
                  onChange={(e) => setDraft({ ...draft, channel: e.target.value as MessageChannel })}
                >
                  <option value="Email">E-mail</option>
                  <option value="Sms">SMS</option>
                </select>
              </FormField>
              <FormField label="Taal" htmlFor="nat-language">
                <select
                  id="nat-language"
                  value={draft.language}
                  disabled={busy}
                  onChange={(e) => setDraft({ ...draft, language: e.target.value })}
                >
                  {LANGUAGES.map((l) => (
                    <option key={l.value} value={l.value}>
                      {l.label}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>

            <FormField label="Klant (optioneel)" htmlFor="nat-customer" hint="Leeg = standaardsjabloon voor alle klanten; kies een klant voor een afwijkend sjabloon.">
              <SearchableSelect
                id="nat-customer"
                value={draft.customerId}
                onChange={(value) => setDraft({ ...draft, customerId: value })}
                options={customerOptions}
                placeholder="Geen — standaardsjabloon"
              />
            </FormField>

            {placeholders.length > 0 && (
              <div className="notification-admin-placeholder-chips">
                <span className="notification-admin-muted">Beschikbare velden (klik om in te voegen in het laatst gebruikte veld):</span>
                <div className="notification-admin-chip-row">
                  {placeholders.map((token) => (
                    <button key={token} type="button" className="notification-admin-chip" onClick={() => insertToken(token)}>
                      {`{{${token}}}`}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {draft.channel === 'Email' && (
              <FormField label="Onderwerp" htmlFor="nat-subject" error={getFieldError(fieldErrors, 'subject')}>
                <input
                  id="nat-subject"
                  ref={subjectRef}
                  value={draft.subject}
                  disabled={busy}
                  maxLength={300}
                  onFocus={() => setActiveField('subject')}
                  onChange={(e) => setDraft({ ...draft, subject: e.target.value })}
                />
              </FormField>
            )}

            <FormField label="Inhoud" htmlFor="nat-body" required error={getFieldError(fieldErrors, 'body')}>
              <textarea
                id="nat-body"
                ref={bodyRef}
                rows={6}
                value={draft.body}
                disabled={busy}
                maxLength={8000}
                onFocus={() => setActiveField('body')}
                onChange={(e) => setDraft({ ...draft, body: e.target.value })}
              />
            </FormField>

            <FormField
              label="Opgemaakte inhoud (HTML, optioneel)"
              htmlFor="nat-body-html"
              hint="Toegestane tags: p, br, strong, em, ul, ol, li, a (href), h1–h3. Andere tags/attributen worden verwijderd bij opslaan."
              error={getFieldError(fieldErrors, 'bodyHtml')}
            >
              <textarea
                id="nat-body-html"
                ref={bodyHtmlRef}
                rows={6}
                value={draft.bodyHtml}
                disabled={busy}
                maxLength={8000}
                onFocus={() => setActiveField('bodyHtml')}
                onChange={(e) => setDraft({ ...draft, bodyHtml: e.target.value })}
              />
            </FormField>

            <label className="notification-admin-checkbox">
              <input type="checkbox" checked={draft.isActive} disabled={busy} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
              Actief
            </label>

            {preview && (
              <div className="notification-admin-preview">
                <h3>Voorbeeld</h3>
                {preview.subject && <p className="notification-admin-preview-subject">{preview.subject}</p>}
                <pre>{preview.body}</pre>
              </div>
            )}
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Sjabloon verwijderen"
          message={`Weet je zeker dat je het sjabloon voor '${deleteTarget.label}' wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
