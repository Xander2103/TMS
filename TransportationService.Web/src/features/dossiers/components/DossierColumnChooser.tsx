import { useEffect, useId, useRef, useState } from 'react'
import { Columns3 } from 'lucide-react'
import { useLocale } from '../../../i18n/localeContext'
import { DOSSIER_DEFAULT_OPTIONAL, DOSSIER_OPTIONAL_COLUMNS, type DossierColumnKey } from '../pages/dossierListColumns'

interface DossierColumnChooserProps {
  optional: readonly DossierColumnKey[]
  onChange: (optional: DossierColumnKey[]) => void
  /** The optional columns this user may pick (default: all of them). */
  available?: readonly DossierColumnKey[]
}

/**
 * B10 "Kolommen ▾": a small popover with one checkbox per optional column and a reset to the
 * defaults. The fixed columns (number, status, customer, date) are not offered: a dossier row
 * without them answers nothing.
 */
export function DossierColumnChooser({ optional, onChange, available = DOSSIER_OPTIONAL_COLUMNS }: DossierColumnChooserProps) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const panelId = useId()

  useEffect(() => {
    if (!open) return
    function onPointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false)
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  function toggle(key: DossierColumnKey, checked: boolean) {
    const next = DOSSIER_OPTIONAL_COLUMNS.filter((candidate) => (candidate === key ? checked : optional.includes(candidate)))
    onChange(next)
  }

  const isDefault = optional.length === DOSSIER_DEFAULT_OPTIONAL.length && DOSSIER_DEFAULT_OPTIONAL.every((key) => optional.includes(key))

  return (
    <div className="dossier-columns" ref={rootRef}>
      <button
        type="button"
        className="dossier-columns-toggle"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((value) => !value)}
      >
        <Columns3 size={16} aria-hidden="true" />
        {t('dossiers.list.columnsButton')}
      </button>
      {open && (
        <div id={panelId} className="dossier-columns-panel" role="group" aria-label={t('dossiers.list.columnsPanelLabel')}>
          {available.map((key) => (
            <label key={key} className="dossier-columns-option">
              <input type="checkbox" checked={optional.includes(key)} onChange={(event) => toggle(key, event.target.checked)} />
              {t(`dossiers.list.columns.${key}`)}
            </label>
          ))}
          <button type="button" className="link-button dossier-columns-reset" disabled={isDefault} onClick={() => onChange([...DOSSIER_DEFAULT_OPTIONAL])}>
            {t('dossiers.list.columnsReset')}
          </button>
        </div>
      )}
    </div>
  )
}
