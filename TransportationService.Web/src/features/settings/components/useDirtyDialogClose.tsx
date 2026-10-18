import { useState, type ReactNode } from 'react'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { useLocale } from '../../../i18n/localeContext'

/**
 * MD-M2: the edit dialogs on the settings pages (Eigen bedrijven, Boekhouding) follow the
 * SectionDrawer close model — Escape, the backdrop, × and Annuleren all go through
 * `requestClose`, which asks before discarding while the draft is dirty. In-app navigation is
 * blocked by the shared UnsavedChangesGuard while the dialog holds unsaved input.
 *
 * Render `guard` next to the dialog (outside it, so the confirm stacks on top as its own layer)
 * and only while that dialog is open: the router supports one navigation blocker at a time.
 */
export function useDirtyDialogClose(
  dirty: boolean,
  busy: boolean,
  onClose: () => void,
): { requestClose: () => void; guard: ReactNode } {
  const { t } = useLocale()
  const [confirming, setConfirming] = useState(false)

  function requestClose() {
    if (busy) return
    if (dirty) setConfirming(true)
    else onClose()
  }

  const guard = (
    <>
      <UnsavedChangesGuard when={dirty && !busy} />
      {confirming && (
        <ConfirmDialog
          title={t('settingsPages.dialogs.discardTitle')}
          message={t('settingsPages.dialogs.discardMessage')}
          confirmLabel={t('settingsPages.dialogs.discardConfirm')}
          cancelLabel={t('ui.unsaved.stay')}
          destructive
          onConfirm={() => {
            setConfirming(false)
            onClose()
          }}
          onCancel={() => setConfirming(false)}
        />
      )}
    </>
  )

  return { requestClose, guard }
}
