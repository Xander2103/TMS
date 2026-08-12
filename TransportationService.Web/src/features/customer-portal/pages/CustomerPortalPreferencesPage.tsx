import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useLocale } from '../../../i18n/localeContext'
import {
  getPortalNotificationPreferences,
  savePortalNotificationPreferences,
  type PortalNotificationPreferences,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

/**
 * Wave 11: de klant beheert zijn eigen meldingsvoorkeuren (kanalen, taal en soorten) —
 * dezelfde MessagingProfile die de dispatcher ziet, maar beperkt tot de klantgerichte
 * meldingssoorten. Geen vinkjes bij soorten = alles ontvangen (het veilige default).
 */
export function CustomerPortalPreferencesPage() {
  const { t } = useLocale()
  const [prefs, setPrefs] = useState<PortalNotificationPreferences | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')

  useEffect(() => {
    let mounted = true
    getPortalNotificationPreferences()
      .then((data) => {
        if (mounted) setPrefs(data)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  if (loadError) return <ErrorState message={t('notifications.preferences.loadError')} />
  if (!prefs) return <LoadingState message={t('notifications.preferences.loading')} />

  const toggleKind = (kind: string) => {
    // null (= alles) wordt bij de eerste toggle expliciet: alle soorten behalve/inclusief deze.
    const current = prefs.enabledKinds ?? prefs.availableKinds
    const next = current.includes(kind) ? current.filter((k) => k !== kind) : [...current, kind]
    setPrefs({ ...prefs, enabledKinds: next })
    setSaveState('idle')
  }

  const save = () => {
    setSaving(true)
    setSaveState('idle')
    const allSelected =
      prefs.enabledKinds === null || prefs.availableKinds.every((k) => prefs.enabledKinds!.includes(k))
    savePortalNotificationPreferences({
      emailEnabled: prefs.emailEnabled,
      smsEnabled: prefs.smsEnabled,
      preferredLanguage: prefs.preferredLanguage,
      // Alles aangevinkt slaan we op als null (= alle, ook toekomstige soorten).
      enabledKinds: allSelected ? null : prefs.enabledKinds,
    })
      .then((data) => {
        setPrefs(data)
        setSaveState('saved')
      })
      .catch(() => setSaveState('error'))
      .finally(() => setSaving(false))
  }

  const isKindEnabled = (kind: string) => prefs.enabledKinds === null || prefs.enabledKinds.includes(kind)

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('notifications.preferences.title') }]}
      />
      <PageHeader title={t('notifications.preferences.title')} subtitle={t('notifications.preferences.subtitle')} />

      <section className="cpp-panel">
        <h2>{t('notifications.preferences.channelsTitle')}</h2>
        <label className="cpp-row">
          <span>{t('notifications.preferences.email')}</span>
          <input
            type="checkbox"
            checked={prefs.emailEnabled}
            onChange={(event) => {
              setPrefs({ ...prefs, emailEnabled: event.target.checked })
              setSaveState('idle')
            }}
          />
        </label>
        <label className="cpp-row">
          <span>{t('notifications.preferences.sms')}</span>
          <input
            type="checkbox"
            checked={prefs.smsEnabled}
            onChange={(event) => {
              setPrefs({ ...prefs, smsEnabled: event.target.checked })
              setSaveState('idle')
            }}
          />
        </label>
      </section>

      <section className="cpp-panel">
        <h2>{t('notifications.preferences.languageTitle')}</h2>
        <select
          value={prefs.preferredLanguage ?? ''}
          onChange={(event) => {
            setPrefs({
              ...prefs,
              preferredLanguage: (event.target.value || null) as PortalNotificationPreferences['preferredLanguage'],
            })
            setSaveState('idle')
          }}
        >
          <option value="">{t('notifications.preferences.languageAuto')}</option>
          <option value="nl">Nederlands</option>
          <option value="fr">Français</option>
          <option value="en">English</option>
        </select>
      </section>

      <section className="cpp-panel">
        <h2>{t('notifications.preferences.kindsTitle')}</h2>
        <p className="placeholder-text">{t('notifications.preferences.kindsHint')}</p>
        <ul className="cpp-list">
          {prefs.availableKinds.map((kind) => (
            <li key={kind}>
              <label className="cpp-row">
                <span>{t(`notifications.preferences.kinds.${kind}`)}</span>
                <input type="checkbox" checked={isKindEnabled(kind)} onChange={() => toggleKind(kind)} />
              </label>
            </li>
          ))}
        </ul>
      </section>

      <div className="cpp-actions">
        <Button onClick={save} disabled={saving}>
          {saving ? t('notifications.preferences.saving') : t('notifications.preferences.save')}
        </Button>
        {saveState === 'saved' && <span role="status">{t('notifications.preferences.saved')}</span>}
        {saveState === 'error' && (
          <span role="alert" className="placeholder-text">
            {t('notifications.preferences.saveError')}
          </span>
        )}
      </div>
    </div>
  )
}
