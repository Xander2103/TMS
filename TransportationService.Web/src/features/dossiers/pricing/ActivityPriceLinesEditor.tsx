import { useImperativeHandle, useRef, useState, type Ref } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { formatCurrency, formatQuantity, parseDecimalInput } from '../../../utils/numbers'
import type { DossierActivity, DossierDetail } from '../types'
import { saveActivityPriceLines } from './activityPriceLinesApi'
import type { ActivityPriceLineInput, DossierActivityPriceLine } from './types'

export interface ActivityPriceLinesEditorHandle {
  /** Starts a new line and focuses its description. */
  addLine: () => void
}

interface ActivityPriceLinesEditorProps {
  dossier: DossierDetail
  activity: DossierActivity
  /** dossiers.price AND dossier open AND price not locked. */
  editable: boolean
  /** True when the activity currently carries a flat price (the lines will replace it on save). */
  replacesFlatPrice: boolean
  /** Reports whether unsaved rows exist (the host hides the flat-price form meanwhile). */
  onDirtyChange?: (dirty: boolean) => void
  /** A successful save — and the reload after a 409 — hand the authoritative dossier to the workspace. */
  onDossierUpdated: (dossier: DossierDetail) => void
  /** Reloads the dossier after a 409 whose body did not carry it; resolves with the fresh state. */
  reloadDossier: () => Promise<DossierDetail>
  ref?: Ref<ActivityPriceLinesEditorHandle>
}

/** One editable row: raw input text, parsed on save (tenant decimal separator via parseDecimalInput). */
interface DraftRow {
  key: string
  id?: string
  label: string
  quantity: string
  unit: string
  unitPrice: string
  salesCategoryId: string | null
}

let draftSeq = 0
const nextKey = () => `new-${++draftSeq}`

function toDraft(line: DossierActivityPriceLine): DraftRow {
  return {
    key: line.id,
    id: line.id,
    label: line.label,
    quantity: String(line.quantity),
    unit: line.unit ?? '',
    unitPrice: String(line.unitPrice),
    salesCategoryId: line.salesCategoryId,
  }
}

/** Preview only — the server computes the real amount (quantity × unit price, rounded to cents). */
function previewAmount(row: DraftRow): number | null {
  const quantity = parseDecimalInput(row.quantity)
  const unitPrice = parseDecimalInput(row.unitPrice)
  return quantity === null || unitPrice === null ? null : Math.round(quantity * unitPrice * 100) / 100
}

/** True when the 409 body is the current dossier (the dossier endpoints answer a stale token with it). */
function isDossier(body: unknown): body is DossierDetail {
  return Boolean(body) && typeof body === 'object' && 'dossierNumber' in (body as object)
}

/**
 * Master sprint 2026-09-21 (D5): the sales lines of a STANDALONE billable activity (Plateau,
 * Kraanwerk, Opslag …). While the planner edits, the rows are a local draft and every amount is a
 * PREVIEW; "Verkooplijnen opslaan" sends the whole list with the price record's version and from
 * then on only the server's amounts and total are shown. A 409 reloads the dossier (fresh version)
 * but keeps the typed rows on screen with an explicit conflict message — nothing is silently lost
 * or silently overwritten.
 */
export function ActivityPriceLinesEditor({
  dossier, activity, editable, replacesFlatPrice, onDirtyChange, onDossierUpdated, reloadDossier, ref,
}: ActivityPriceLinesEditorProps) {
  const { t } = useLocale()
  const toast = useToast()
  const serverLines = [...(activity.priceLines ?? [])].sort((a, b) => a.sequence - b.sequence)
  // null = no unsaved edits: the table mirrors the server lines.
  const [draft, setDraft] = useState<DraftRow[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [conflict, setConflict] = useState<string | null>(null)
  const focusKey = useRef<string | null>(null)

  const dirty = draft !== null
  const rows = draft ?? serverLines.map(toDraft)

  function update(next: DraftRow[] | null) {
    setDraft(next)
    onDirtyChange?.(next !== null)
  }

  function addLine() {
    const key = nextKey()
    focusKey.current = key
    setError(null)
    update([...rows, { key, label: '', quantity: '1', unit: '', unitPrice: '', salesCategoryId: null }])
  }

  useImperativeHandle(ref, () => ({ addLine }))

  function patch(key: string, change: Partial<DraftRow>) {
    update(rows.map((row) => (row.key === key ? { ...row, ...change } : row)))
  }

  function move(index: number, delta: -1 | 1) {
    const target = index + delta
    if (target < 0 || target >= rows.length) return
    const next = [...rows]
    ;[next[index], next[target]] = [next[target], next[index]]
    update(next)
  }

  function discard() {
    setError(null)
    setConflict(null)
    update(null)
  }

  /** Parsed payload, or the first validation message (mirrors the server rules). */
  function buildLines(): ActivityPriceLineInput[] | string {
    if (rows.length === 0) return t('dossierPricing.activity.noRowsToSave')
    const lines: ActivityPriceLineInput[] = []
    for (const [i, row] of rows.entries()) {
      const index = i + 1
      const quantity = parseDecimalInput(row.quantity)
      const unitPrice = parseDecimalInput(row.unitPrice)
      if (!row.label.trim()) return t('dossierPricing.activity.labelRequired', { index })
      if (quantity === null || quantity <= 0) return t('dossierPricing.activity.quantityInvalid', { index })
      if (unitPrice === null || unitPrice < 0) return t('dossierPricing.activity.unitPriceInvalid', { index })
      lines.push({
        ...(row.id ? { id: row.id } : {}),
        label: row.label.trim(),
        quantity,
        unit: row.unit.trim() || null,
        unitPrice,
        ...(row.salesCategoryId ? { salesCategoryId: row.salesCategoryId } : {}),
      })
    }
    return lines
  }

  async function save() {
    if (busy || !editable) return
    const lines = buildLines()
    if (typeof lines === 'string') {
      setError(lines)
      return
    }
    setBusy(true)
    setError(null)
    setConflict(null)
    try {
      const updated = await saveActivityPriceLines(dossier.id, activity.id, { version: activity.pricingVersion, lines, freeConfirmed: false })
      toast.showSuccess(t('dossierPricing.activity.linesSaved'))
      // From here on the server's amounts and total are the only ones on screen.
      update(null)
      onDossierUpdated(updated)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        // Reload (fresh version + the colleague's state everywhere else) but keep the typed rows.
        try {
          onDossierUpdated(isDossier(err.body) ? err.body : await reloadDossier())
          setConflict(t('dossierPricing.activity.conflict'))
        } catch {
          setConflict(t('dossierPricing.activity.conflictReloadFailed'))
        }
      } else {
        setError(describeApiError(err, t('dossierPricing.activity.linesSaveFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  const previewTotal = rows.reduce<number | null>((sum, row) => {
    const amount = previewAmount(row)
    return sum === null || amount === null ? null : sum + amount
  }, 0)

  return (
    <div className="dossier-activity-lines">
      <h3>{t('dossierPricing.activity.linesTitle')}</h3>

      {rows.length === 0 && <p className="placeholder-text">{t('dossierPricing.activity.noLines')}</p>}

      {rows.length > 0 && (
        <div className="dossier-activity-lines-wrap">
          <table className="dossier-price-table dossier-activity-lines-table">
            <thead>
              <tr>
                <th scope="col">{t('dossierPricing.activity.colLabel')}</th>
                <th scope="col" className="num">{t('dossierPricing.activity.colQuantity')}</th>
                <th scope="col">{t('dossierPricing.activity.colUnit')}</th>
                <th scope="col" className="num">{t('dossierPricing.activity.colUnitPrice')}</th>
                <th scope="col" className="num">{t('dossierPricing.activity.colAmount')}</th>
                {editable && <th scope="col">{t('dossierPricing.activity.colActions')}</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, i) => {
                const index = i + 1
                const server = dirty ? undefined : serverLines[i]
                const amount = server ? server.amount : previewAmount(row)
                if (!editable) {
                  return (
                    <tr key={row.key}>
                      <td>{row.label}</td>
                      <td className="num">{server ? formatQuantity(server.quantity) : row.quantity}</td>
                      <td>{row.unit}</td>
                      <td className="num">{server ? formatCurrency(server.unitPrice) : row.unitPrice}</td>
                      <td className="num">{amount !== null ? formatCurrency(amount) : '—'}</td>
                    </tr>
                  )
                }
                return (
                  <tr key={row.key}>
                    <td>
                      <input
                        ref={(element) => {
                          if (element && focusKey.current === row.key) {
                            focusKey.current = null
                            element.focus()
                          }
                        }}
                        aria-label={t('dossierPricing.activity.rowLabel', { index })}
                        value={row.label}
                        onChange={(event) => patch(row.key, { label: event.target.value })}
                        disabled={busy}
                        maxLength={200}
                      />
                    </td>
                    <td className="num">
                      <input
                        aria-label={t('dossierPricing.activity.rowQuantity', { index })}
                        type="text"
                        inputMode="decimal"
                        value={row.quantity}
                        onChange={(event) => patch(row.key, { quantity: event.target.value })}
                        disabled={busy}
                      />
                    </td>
                    <td>
                      <input
                        aria-label={t('dossierPricing.activity.rowUnit', { index })}
                        value={row.unit}
                        onChange={(event) => patch(row.key, { unit: event.target.value })}
                        disabled={busy}
                        maxLength={30}
                      />
                    </td>
                    <td className="num">
                      <input
                        aria-label={t('dossierPricing.activity.rowUnitPrice', { index })}
                        type="text"
                        inputMode="decimal"
                        value={row.unitPrice}
                        onChange={(event) => patch(row.key, { unitPrice: event.target.value })}
                        disabled={busy}
                        placeholder="0,00"
                      />
                    </td>
                    <td className="num" data-preview={dirty || undefined}>
                      {amount !== null ? formatCurrency(amount) : '—'}
                    </td>
                    <td className="actions">
                      <button type="button" className="link-button" onClick={() => move(i, -1)} disabled={busy || i === 0} aria-label={t('dossierPricing.activity.moveUp', { index })}>
                        ↑
                      </button>
                      <button type="button" className="link-button" onClick={() => move(i, 1)} disabled={busy || i === rows.length - 1} aria-label={t('dossierPricing.activity.moveDown', { index })}>
                        ↓
                      </button>
                      <button type="button" className="link-button" onClick={() => update(rows.filter((r) => r.key !== row.key))} disabled={busy} aria-label={t('dossierPricing.activity.removeRow', { index })}>
                        ✕
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colSpan={4}>
                  {dirty ? t('dossierPricing.activity.previewSubtotal') : t('dossierPricing.activity.subtotal')}
                </th>
                <td className="num">
                  {dirty
                    ? previewTotal !== null ? formatCurrency(previewTotal) : '—'
                    : activity.agreedPrice != null ? formatCurrency(activity.agreedPrice) : '—'}
                </td>
                {editable && <td />}
              </tr>
            </tfoot>
          </table>
        </div>
      )}

      {dirty && <p className="dossier-price-note">{t('dossierPricing.activity.previewHint')}</p>}
      {dirty && replacesFlatPrice && <p className="dossier-price-note">{t('dossierPricing.activity.linesReplaceFlat')}</p>}
      {error && (
        <p className="dossier-activity-lines-error" role="alert">
          {error}
        </p>
      )}
      {conflict && (
        <p className="dossier-activity-lines-error" role="alert">
          {conflict}
        </p>
      )}

      {editable && (
        <p className="dossier-price-actions">
          <Button variant="secondary" onClick={addLine} disabled={busy}>
            {t('dossierPricing.addLine')}
          </Button>
          {dirty && (
            <>
              <Button onClick={() => void save()} disabled={busy}>
                {t('dossierPricing.activity.saveLines')}
              </Button>
              <Button variant="ghost" onClick={discard} disabled={busy}>
                {t('dossierPricing.activity.discard')}
              </Button>
            </>
          )}
        </p>
      )}
    </div>
  )
}
