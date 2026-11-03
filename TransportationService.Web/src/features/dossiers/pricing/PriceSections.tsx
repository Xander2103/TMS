import { useState, type ReactNode } from 'react'
import { ChevronDown, Tag, type LucideIcon } from 'lucide-react'

interface PriceFoldProps {
  title: string
  icon: LucideIcon
  defaultOpen?: boolean
  children: ReactNode
}

/** One compact collapsible card under the sales lines. */
export function PriceFold({ title, icon: Icon, defaultOpen = false, children }: PriceFoldProps) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <details className="dossier-price-fold" open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>
        <Icon size={20} aria-hidden="true" />
        <h3>{title}</h3>
        <ChevronDown className="dossier-price-fold-chevron" size={18} aria-hidden="true" />
      </summary>
      <div className="dossier-price-fold-body">{children}</div>
    </details>
  )
}

interface FixedPriceSectionProps {
  title: string
  description?: string
  /** The switch that brings the fixed price forward; absent when the fixed price IS the current price. */
  toggle?: { checked: boolean; onChange: (checked: boolean) => void; disabled?: boolean }
  children?: ReactNode
}

/** The fixed price as its own card: a switch while sales lines carry the price, otherwise the form itself. */
export function FixedPriceSection({ title, description, toggle, children }: FixedPriceSectionProps) {
  return (
    <section className="dossier-price-fixed">
      <div className="dossier-price-fixed-head">
        <Tag size={20} aria-hidden="true" />
        <div className="dossier-price-fixed-text">
          <h3>{title}</h3>
          {description && <p>{description}</p>}
        </div>
        {toggle && (
          <button
            type="button"
            role="switch"
            className="dossier-price-switch"
            aria-checked={toggle.checked}
            aria-label={title}
            disabled={toggle.disabled}
            onClick={() => toggle.onChange(!toggle.checked)}
          >
            <span aria-hidden="true" />
          </button>
        )}
      </div>
      {children && <div className="dossier-price-fixed-body">{children}</div>}
    </section>
  )
}
