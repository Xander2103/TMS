import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useLocale } from '../../i18n/localeContext'
import './AttentionList.css'

export type AttentionSeverity = 'critical' | 'warning' | 'info'

export interface AttentionItem {
  key: string
  severity: AttentionSeverity
  /** The fact, in words: "Keuring verloopt op 25/10/2026". */
  text: ReactNode
  /** Muted extra context (document name, number). */
  detail?: ReactNode
  /** Where to resolve it: a route (`to`) or an in-page jump (`onSelect`). */
  action?: { label: string; to?: string; onSelect?: () => void }
}

interface AttentionListProps {
  items: AttentionItem[]
  /** Defaults to "Aandacht". */
  title?: string
  className?: string
}

const ICON: Record<AttentionSeverity, string> = { critical: '⛔', warning: '⚠', info: 'ℹ' }

/**
 * The entity-detail attention strip (B15/B16/B17/B24): only what needs a person's action, each
 * line naming the fact and offering the jump that resolves it. Renders nothing when the list is
 * empty — normal configuration is never shown as a warning. The strip takes the colour of its
 * worst item; every item still carries an icon + text so severity is never colour-only.
 */
export function AttentionList({ items, title, className }: AttentionListProps) {
  const { t } = useLocale()
  if (items.length === 0) return null
  const worst: AttentionSeverity = items.some((i) => i.severity === 'critical')
    ? 'critical'
    : items.some((i) => i.severity === 'warning')
      ? 'warning'
      : 'info'
  const heading = title ?? t('ui.attention.title')
  return (
    <section className={['ui-attention', `is-${worst}`, className].filter(Boolean).join(' ')} aria-label={heading}>
      <h2 className="ui-attention-title">{heading}</h2>
      <ul className="ui-attention-items">
        {items.map((item) => (
          <li key={item.key} className={`ui-attention-item is-${item.severity}`}>
            <span className="ui-attention-icon" role="img" aria-label={t(`ui.attention.severity.${item.severity}`)}>
              {ICON[item.severity]}
            </span>
            <span className="ui-attention-text">{item.text}</span>
            {item.detail && <span className="ui-attention-detail">{item.detail}</span>}
            {item.action &&
              (item.action.to ? (
                <Link className="ui-attention-action" to={item.action.to}>
                  {item.action.label}
                </Link>
              ) : (
                <button type="button" className="ui-attention-action" onClick={item.action.onSelect}>
                  {item.action.label}
                </button>
              ))}
          </li>
        ))}
      </ul>
    </section>
  )
}
