import { useEffect, useState, type ReactNode } from 'react'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { lockBodyScroll } from '../../../components/ui/bodyScrollLock'
import { useEscapeLayer } from '../../../components/ui/escapeLayerStack'
import { useLocale } from '../../../i18n/localeContext'
import './section-drawer.css'

interface SectionDrawerProps {
  title: string
  /** Dirty state drives the close confirmation + the navigation guard. */
  dirty: boolean
  busy?: boolean
  onClose: () => void
  /** Renders the standard Annuleren/Opslaan footer; omit for read-only or custom footers. */
  onSave?: () => void
  saveLabel?: string
  /** Extra footer content left of the standard buttons (e.g. Verwijderen). */
  footerExtra?: ReactNode
  children: ReactNode
}

/**
 * §17 editing model: right-side drawer (full-screen sheet below 900px) with explicit
 * Opslaan/Annuleren. ESC and overlay clicks ask for confirmation while dirty; in-app
 * navigation is guarded by UnsavedChangesGuard.
 */
export function SectionDrawer({ title, dirty, busy = false, onClose, onSave, saveLabel, footerExtra, children }: SectionDrawerProps) {
  const { t } = useLocale()
  const [confirmClose, setConfirmClose] = useState(false)
  // Captured on the first render, like Modal: the element that opened the drawer gets focus back
  // when the drawer unmounts, so Escape-Escape (dialog, then drawer) lands where the user started.
  const [opener] = useState(() => document.activeElement as HTMLElement | null)

  function requestClose() {
    if (busy) return
    if (dirty) setConfirmClose(true)
    else onClose()
  }

  // Only the topmost overlay receives Escape (see escapeLayerStack): while the nested confirm
  // dialog below is open it is the top layer, so Escape cancels that dialog only and the drawer
  // stays open (and dirty) for the next press to ask again.
  useEscapeLayer(true, requestClose)

  useEffect(() => {
    return () => {
      if (opener && typeof opener.focus === 'function' && document.contains(opener)) opener.focus()
    }
  }, [opener])

  // Scroll lock follows open/closed only (the drawer is mounted exactly while it is open), never
  // dirty/busy. The shared counter keeps the body locked while a nested dialog is still open and
  // restores the original value whichever of the two unmounts first.
  useEffect(() => lockBodyScroll(), [])

  return (
    <div className="section-drawer-backdrop" onClick={requestClose}>
      <UnsavedChangesGuard when={dirty} />
      <aside
        className="section-drawer"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="section-drawer-header">
          <h3>{title}</h3>
          <button
            type="button"
            className="section-drawer-close"
            onClick={requestClose}
            disabled={busy}
            aria-label={t('ui.actions.close')}
          >
            ×
          </button>
        </header>
        <div className="section-drawer-body">{children}</div>
        {(onSave || footerExtra) && (
          <footer className="section-drawer-footer">
            {footerExtra && <div className="section-drawer-footer-extra">{footerExtra}</div>}
            <div className="section-drawer-footer-actions">
              <Button variant="secondary" onClick={requestClose} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              {onSave && (
                <Button onClick={onSave} disabled={busy}>
                  {busy ? t('dossiers.sectionDrawer.saving') : saveLabel ?? t('ui.actions.save')}
                </Button>
              )}
            </div>
          </footer>
        )}
      </aside>

      {confirmClose && (
        <ConfirmDialog
          title={t('dossiers.sectionDrawer.confirmTitle')}
          message={t('dossiers.sectionDrawer.confirmMessage')}
          confirmLabel={t('dossiers.sectionDrawer.confirmLeave')}
          cancelLabel={t('ui.unsaved.stay')}
          destructive
          onConfirm={() => {
            setConfirmClose(false)
            onClose()
          }}
          onCancel={() => setConfirmClose(false)}
        />
      )}
    </div>
  )
}
