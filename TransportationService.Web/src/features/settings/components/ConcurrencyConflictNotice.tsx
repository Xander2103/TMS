import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import './settings-dialogs.css'

/**
 * MD-M2: shown instead of the generic save error when the server answered 409
 * `common.concurrency_conflict` — someone else saved the record since the dialog opened.
 * "Opnieuw laden" replaces the draft with the current server version.
 */
export function ConcurrencyConflictNotice({ onReload, busy = false }: { onReload: () => void; busy?: boolean }) {
  const { t } = useLocale()
  return (
    <div className="settings-conflict" role="alert">
      <p>{t('settingsPages.dialogs.conflictMessage')}</p>
      <Button variant="secondary" onClick={onReload} disabled={busy}>
        {t('settingsPages.dialogs.conflictReload')}
      </Button>
    </div>
  )
}
