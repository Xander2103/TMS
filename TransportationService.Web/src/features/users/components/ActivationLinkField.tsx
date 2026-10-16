import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { activationUrl } from '../api/usersApi'
import './UserForm.css'

/** The one-time activation link, read-only and copyable (HR acceptance H7). */
export function ActivationLinkField({ token }: { token: string }) {
  const { t } = useLocale()
  const { showSuccess } = useToast()
  const url = activationUrl(token)

  async function copy() {
    try {
      await navigator.clipboard.writeText(url)
      showSuccess(t('usersRoles.users.activation.copied'))
    } catch {
      // Clipboard unavailable (insecure context): the field stays selectable.
    }
  }

  return (
    <FormField label={t('usersRoles.users.activation.link')} htmlFor="user-activation-link">
      <div className="user-activation-link">
        <input id="user-activation-link" readOnly value={url} onFocus={(event) => event.target.select()} />
        <Button variant="secondary" onClick={() => void copy()}>
          {t('usersRoles.users.activation.copy')}
        </Button>
      </div>
    </FormField>
  )
}
