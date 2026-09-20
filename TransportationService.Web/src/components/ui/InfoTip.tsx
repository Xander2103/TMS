import { useId, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { CircleHelp } from 'lucide-react'
import { useLocale } from '../../i18n/localeContext'
import './InfoTip.css'

export type InfoTipPlacement = 'bottom-start' | 'bottom-end' | 'top-start' | 'top-end'

export interface InfoTipLink {
  href: string
  label: string
}

interface InfoTipProps {
  /** Explanation shown in the tooltip. */
  text: string
  /** Optional external "read more" link rendered under the text (opens in a new tab). */
  link?: InfoTipLink
  /** Accessible name of the trigger; defaults to the localised "More information". */
  ariaLabel?: string
  /** Where the bubble opens relative to the trigger. Default `bottom-start`. */
  placement?: InfoTipPlacement
  className?: string
}

/**
 * Small round "?" trigger with an accessible tooltip. Opens on hover and on keyboard focus,
 * closes on mouse leave, blur (focus leaving the whole widget) and Escape. The tooltip text is
 * always linked through `aria-describedby`, so assistive tech gets the explanation even while
 * the bubble is hidden; when a link is present it stays reachable with Tab because focus moving
 * from the trigger to the link is still inside the wrapper.
 */
export function InfoTip({ text, link, ariaLabel, placement = 'bottom-start', className }: InfoTipProps) {
  const { t } = useLocale()
  const tooltipId = useId()
  const [open, setOpen] = useState(false)

  function handleBlur(event: FocusEvent<HTMLSpanElement>) {
    if (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget)) return
    setOpen(false)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLSpanElement>) {
    if (event.key === 'Escape' && open) {
      event.stopPropagation()
      setOpen(false)
    }
  }

  return (
    <span
      className={['ui-info-tip', `ui-info-tip-${placement}`, open ? 'is-open' : '', className].filter(Boolean).join(' ')}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={handleBlur}
      onKeyDown={handleKeyDown}
    >
      <button
        type="button"
        className="ui-info-tip-trigger"
        aria-label={ariaLabel ?? t('ui.infoTip.ariaLabel')}
        aria-describedby={tooltipId}
        onClick={() => setOpen(true)}
      >
        <CircleHelp size={16} aria-hidden="true" focusable="false" />
      </button>
      <span className="ui-info-tip-popover" role="tooltip" id={tooltipId} hidden={!open}>
        <span className="ui-info-tip-bubble">
          <span className="ui-info-tip-text">{text}</span>
          {link && (
            <a className="ui-info-tip-link" href={link.href} target="_blank" rel="noopener noreferrer">
              {link.label}
            </a>
          )}
        </span>
      </span>
    </span>
  )
}
