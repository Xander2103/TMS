import { useSearchParams } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import type { EmployeeAttention, EmployeeAttentionItem, EmployeeAttentionKind } from '../types/employee'
import { EMPLOYEE_DOCUMENT_CATEGORY_LABELS, type EmployeeDocumentCategory } from '../api/employeeDocumentsApi'
import './EmployeeAttentionStrip.css'

interface EmployeeAttentionStripProps {
  attention: EmployeeAttention | null | undefined
}

/** `?tab=` id and row-id query param per item kind — the tabs highlight + scroll to that row. */
const TARGET: Record<EmployeeAttentionKind, { tab: string; param: string }> = {
  document: { tab: 'documenten', param: 'documentId' },
  qualification: { tab: 'kwalificaties', param: 'qualificationId' },
}

/**
 * Compact expiry-warning strip under the employee page header (HR sees it before anything
 * else). Renders nothing when the backend reports no items; the backend also decides
 * expiring vs. expired — this component never reclassifies dates itself.
 */
export function EmployeeAttentionStrip({ attention }: EmployeeAttentionStripProps) {
  const { t } = useLocale()
  const [searchParams, setSearchParams] = useSearchParams()

  if (!attention || !attention.hasItems || attention.items.length === 0) return null

  const { items } = attention
  const anyExpired = attention.documentsExpired + attention.qualificationsExpired > 0 || items.some((i) => i.state === 'expired')

  function nearestDays(kind: EmployeeAttentionKind): number {
    const days = items.filter((i) => i.kind === kind && i.state === 'expiring').map((i) => i.daysLeft)
    return days.length === 0 ? 0 : Math.min(...days)
  }

  /** "1 document verloopt over 12 dagen" / "3 documenten verlopen binnenkort" — the singular
   * borrows the nearest item's daysLeft so HR reads the urgency without opening the tab. */
  function expiringLine(kind: EmployeeAttentionKind, count: number): string {
    if (count <= 0) return ''
    const prefix = kind === 'document' ? 'documentsExpiring' : 'qualificationsExpiring'
    const days = nearestDays(kind)
    if (count === 1 && days <= 0) return t(`employeeAttention.summary.${prefix}Today`)
    return t(`employeeAttention.summary.${prefix}`, {
      count,
      period: t('employeeAttention.summary.days', { count: days }),
    })
  }

  function expiredLine(kind: EmployeeAttentionKind, count: number): string {
    if (count <= 0) return ''
    const prefix = kind === 'document' ? 'documentsExpired' : 'qualificationsExpired'
    return t(`employeeAttention.summary.${prefix}`, { count })
  }

  const summary = [
    expiredLine('document', attention.documentsExpired),
    expiringLine('document', attention.documentsExpiring),
    expiredLine('qualification', attention.qualificationsExpired),
    expiringLine('qualification', attention.qualificationsExpiring),
  ].filter(Boolean)

  function goTo(item: EmployeeAttentionItem) {
    const target = TARGET[item.kind]
    // Keep every other param (page state) but drop the sibling row id so a stale highlight
    // never lingers on the other tab.
    const next = new URLSearchParams(searchParams)
    next.set('tab', target.tab)
    next.delete('documentId')
    next.delete('qualificationId')
    next.set(target.param, item.id)
    setSearchParams(next, { replace: true })
  }

  /** Documents without a custom label carry the raw category name; show the translated category instead. */
  function documentCategoryLabel(item: EmployeeAttentionItem): string | null {
    if (item.kind !== 'document' || !item.detail || !(item.detail in EMPLOYEE_DOCUMENT_CATEGORY_LABELS)) return null
    return t(EMPLOYEE_DOCUMENT_CATEGORY_LABELS[item.detail as EmployeeDocumentCategory])
  }

  function itemLabel(item: EmployeeAttentionItem): string {
    const category = documentCategoryLabel(item)
    return category && item.label === item.detail ? category : item.label
  }

  function itemDetail(item: EmployeeAttentionItem): string | null {
    if (item.kind !== 'document') return item.detail
    const category = documentCategoryLabel(item)
    // Only worth repeating when the item has its own label (the category is then extra context).
    return category && item.label !== item.detail ? category : null
  }

  function itemText(item: EmployeeAttentionItem): string {
    const params = { label: itemLabel(item), date: formatDate(item.expiryDate) }
    return item.state === 'expired'
      ? t('employeeAttention.item.expired', params)
      : t('employeeAttention.item.expires', params)
  }

  return (
    <section
      role="region"
      aria-label={t('employeeAttention.title')}
      className={`employee-attention-strip ${anyExpired ? 'is-expired' : 'is-expiring'}`}
    >
      <div className="employee-attention-head">
        <h2 className="employee-attention-title">
          <span className="employee-attention-icon" aria-hidden="true">
            {anyExpired ? '⛔' : '⚠'}
          </span>
          {t('employeeAttention.title')}
        </h2>
        <ul className="employee-attention-summary">
          {summary.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      </div>
      <ul className="employee-attention-items">
        {items.map((item) => (
          <li key={`${item.kind}-${item.id}`} className={`employee-attention-item is-${item.state}`}>
            <span className="employee-attention-kind">
              {t(item.kind === 'document' ? 'employeeAttention.kind.document' : 'employeeAttention.kind.qualification')}
            </span>
            <button type="button" className="employee-attention-link" onClick={() => goTo(item)}>
              {itemText(item)}
            </button>
            {itemDetail(item) && <span className="employee-attention-detail">{itemDetail(item)}</span>}
          </li>
        ))}
      </ul>
    </section>
  )
}
