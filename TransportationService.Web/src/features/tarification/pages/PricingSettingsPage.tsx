import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import {
  createPricingZone,
  createTenantHoliday,
  deletePricingZone,
  deleteTenantHoliday,
  listPricingZones,
  listTenantHolidays,
  updatePricingZone,
  type PricingZone,
  type TenantHoliday,
} from '../api/pricingApi'
import { ServiceOptionsEditor } from '../components/ServiceOptionsEditor'
import { UnitTypeMasterEditor } from '../components/UnitTypeMasterEditor'

type TabId = 'zones' | 'diensten' | 'eenheden' | 'feestdagen'

interface ZoneDraft {
  zone: PricingZone | null
  code: string
  name: string
  areas: { countryCode: string; from: string; to: string }[]
}

/**
 * Master data behind the pricing engine: delivery zones, delivery services/supplements and
 * which units may be used for order entry / price agreements. Customer-specific rules live
 * on the customer's "Tarieven & toeslagen" tab.
 */
export function PricingSettingsPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('tariffs.manage')

  const [tab, setTab] = useState<TabId>('zones')
  const [zones, setZones] = useState<PricingZone[]>([])
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)

  const [zoneDraft, setZoneDraft] = useState<ZoneDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteZone, setDeleteZone] = useState<PricingZone | null>(null)
  const [busy, setBusy] = useState(false)

  // Wave 3 §4: feestdagen die Feestdag-toeslagcondities voeden.
  const [holidays, setHolidays] = useState<TenantHoliday[]>([])
  const [holidayDate, setHolidayDate] = useState('')
  const [holidayName, setHolidayName] = useState('')

  const reload = useCallback(() => {
    listPricingZones()
      .then((zoneData) => {
        setZones(zoneData)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('tarification.settings.loadError'))
    listTenantHolidays()
      .then(setHolidays)
      .catch(() => {})
  }, [])

  async function addHoliday(event: FormEvent) {
    event.preventDefault()
    if (!holidayDate || !holidayName.trim()) return
    setBusy(true)
    try {
      await createTenantHoliday({ date: holidayDate, name: holidayName.trim() })
      showSuccess(t('tarification.settings.holidayAdded'))
      setHolidayDate('')
      setHolidayName('')
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.settings.holidayAddError')))
    } finally {
      setBusy(false)
    }
  }

  async function removeHoliday(holiday: TenantHoliday) {
    try {
      await deleteTenantHoliday(holiday.id)
      showSuccess(t('tarification.settings.holidayRemoved'))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('tarification.settings.holidayRemoveError')))
    }
  }

  useEffect(() => {
    reload()
  }, [reload])

  async function submitZone(event: FormEvent) {
    event.preventDefault()
    if (!zoneDraft) return
    setBusy(true)
    try {
      const input = {
        code: zoneDraft.code.trim(),
        name: zoneDraft.name.trim(),
        isActive: true,
        sortOrder: zoneDraft.zone?.sortOrder ?? zones.length,
        areas: zoneDraft.areas
          .filter((a) => a.from.trim() && a.to.trim())
          .map((a) => ({ countryCode: a.countryCode.trim() || 'BE', postalCodeFrom: a.from.trim(), postalCodeTo: a.to.trim() })),
      }
      if (zoneDraft.zone) {
        await updatePricingZone(zoneDraft.zone.id, input)
        showSuccess(t('tarification.settings.zoneUpdated'))
      } else {
        await createPricingZone(input)
        showSuccess(t('tarification.settings.zoneAdded'))
      }
      setZoneDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('tarification.settings.zoneSaveError')))
    } finally {
      setBusy(false)
    }
  }

  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>

  return (
    <div>
      <Breadcrumbs items={[{ label: t('tarification.settings.breadcrumbSettings'), to: '/settings' }, { label: t('tarification.settings.title') }]} />
      <PageHeader
        title={t('tarification.settings.title')}
        subtitle={t('tarification.settings.subtitle')}
      />
      <Tabs
        tabs={[
          { id: 'zones', label: t('tarification.settings.tabZones'), badge: zones.length || undefined },
          { id: 'diensten', label: t('tarification.settings.tabServices') },
          { id: 'eenheden', label: t('tarification.settings.tabUnits') },
          { id: 'feestdagen', label: t('tarification.settings.tabHolidays'), badge: holidays.length || undefined },
        ]}
        activeId={tab}
        onChange={(next) => setTab(next as TabId)}
      />

      {tab === 'zones' && (
        <TabPanel tabId="zones">
          {canManage && (
            <div className="tof-documents-toolbar">
              <Button onClick={() => { setDraftError(null); setZoneDraft({ zone: null, code: '', name: '', areas: [{ countryCode: 'BE', from: '', to: '' }] }) }}>
                {t('tarification.settings.addZone')}
              </Button>
            </div>
          )}
          {zones.length === 0 && <p className="placeholder-text">{t('tarification.settings.zonesEmpty')}</p>}
          {zones.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('tarification.unitMaster.colCode')}</th>
                  <th>{t('tarification.common.name')}</th>
                  <th>{t('tarification.settings.colPostal')}</th>
                  {canManage && <th aria-label={t('tarification.common.actions')} />}
                </tr>
              </thead>
              <tbody>
                {zones.map((zone) => (
                  <tr key={zone.id}>
                    <td>{zone.code}</td>
                    <td>{zone.name}</td>
                    <td>{zone.areas.map((a) => `${a.countryCode} ${a.postalCodeFrom}–${a.postalCodeTo}`).join(', ') || '—'}</td>
                    {canManage && (
                      <td className="issued-items-row-actions">
                        <button
                          type="button"
                          className="issued-items-link"
                          onClick={() => {
                            setDraftError(null)
                            setZoneDraft({
                              zone,
                              code: zone.code,
                              name: zone.name,
                              areas: zone.areas.map((a) => ({ countryCode: a.countryCode, from: a.postalCodeFrom, to: a.postalCodeTo })),
                            })
                          }}
                        >
                          {t('ui.actions.edit')}
                        </button>
                        <button type="button" className="issued-items-link issued-items-link-danger" onClick={() => setDeleteZone(zone)}>
                          {t('ui.actions.delete')}
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TabPanel>
      )}

      {tab === 'diensten' && (
        <TabPanel tabId="diensten">
          {/* Same editor as Stamgegevens → Services & toeslagen: one source of truth. */}
          <ServiceOptionsEditor />
        </TabPanel>
      )}

      {tab === 'eenheden' && (
        <TabPanel tabId="eenheden">
          {/* Same editor as Stamgegevens → Eenheden: one source of truth for unit master data. */}
          <UnitTypeMasterEditor />
        </TabPanel>
      )}

      {tab === 'feestdagen' && (
        <TabPanel tabId="feestdagen">
          <p className="ui-form-section-description">{t('tarification.settings.holidaysIntro')}</p>
          {canManage && (
            <form className="tof-documents-toolbar" onSubmit={(e) => void addHoliday(e)}>
              <input
                type="date"
                value={holidayDate}
                onChange={(e) => setHolidayDate(e.target.value)}
                aria-label={t('tarification.settings.holidayDateAria')}
                disabled={busy}
              />
              <input
                value={holidayName}
                onChange={(e) => setHolidayName(e.target.value)}
                placeholder={t('tarification.settings.holidayNamePlaceholder')}
                maxLength={200}
                aria-label={t('tarification.settings.holidayNameAria')}
                disabled={busy}
              />
              <Button type="submit" disabled={busy || !holidayDate || !holidayName.trim()}>{t('tarification.settings.addHoliday')}</Button>
            </form>
          )}
          {holidays.length === 0 && <p className="placeholder-text">{t('tarification.settings.holidaysEmpty')}</p>}
          {holidays.length > 0 && (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('tarification.settings.colDate')}</th>
                  <th>{t('tarification.common.name')}</th>
                  {canManage && <th aria-label={t('tarification.common.actions')} />}
                </tr>
              </thead>
              <tbody>
                {holidays.map((holiday) => (
                  <tr key={holiday.id}>
                    <td>{holiday.date}</td>
                    <td>{holiday.name}</td>
                    {canManage && (
                      <td className="issued-items-row-actions">
                        <button
                          type="button"
                          className="issued-items-link issued-items-link-danger"
                          onClick={() => void removeHoliday(holiday)}
                        >
                          {t('ui.actions.delete')}
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </TabPanel>
      )}

      {zoneDraft && (
        <Modal
          title={zoneDraft.zone ? t('tarification.settings.zoneEditTitle', { code: zoneDraft.zone.code }) : t('tarification.settings.zoneAddTitle')}
          onClose={() => setZoneDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setZoneDraft(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="zone-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="zone-form" className="issued-items-form" onSubmit={submitZone} noValidate>
            {draftError && (
              <div className="issued-items-form-error" role="alert">
                {draftError}
              </div>
            )}
            <div className="issued-items-form-row">
              <FormField label={t('tarification.unitMaster.codeLabel')} htmlFor="zone-code" required hint={t('tarification.settings.zoneCodeHint')}>
                <input id="zone-code" value={zoneDraft.code} onChange={(e) => setZoneDraft((d) => (d ? { ...d, code: e.target.value } : d))} maxLength={30} />
              </FormField>
              <FormField label={t('tarification.common.name')} htmlFor="zone-name" required>
                <input id="zone-name" value={zoneDraft.name} onChange={(e) => setZoneDraft((d) => (d ? { ...d, name: e.target.value } : d))} maxLength={150} />
              </FormField>
            </div>
            <fieldset className="issued-items-generate-dimension">
              <legend>{t('tarification.settings.postalLegend')}</legend>
              {zoneDraft.areas.map((area, index) => (
                <div key={index} className="issued-items-form-row customer-rule-bracket">
                  <input aria-label={t('tarification.settings.ariaRangeCountry', { index: index + 1 })} placeholder="BE" maxLength={2} value={area.countryCode}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, countryCode: e.target.value.toUpperCase() } : a)) } : d))} />
                  <input aria-label={t('tarification.settings.ariaRangeFrom', { index: index + 1 })} placeholder={t('tarification.settings.rangeFromPlaceholder')} value={area.from}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, from: e.target.value } : a)) } : d))} />
                  <input aria-label={t('tarification.settings.ariaRangeTo', { index: index + 1 })} placeholder={t('tarification.settings.rangeToPlaceholder')} value={area.to}
                    onChange={(e) => setZoneDraft((d) => (d ? { ...d, areas: d.areas.map((a, i) => (i === index ? { ...a, to: e.target.value } : a)) } : d))} />
                  <Button variant="ghost" onClick={() => setZoneDraft((d) => (d ? { ...d, areas: d.areas.filter((_, i) => i !== index) } : d))}>
                    {t('ui.actions.delete')}
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={() => setZoneDraft((d) => (d ? { ...d, areas: [...d.areas, { countryCode: 'BE', from: '', to: '' }] } : d))}>
                {t('tarification.settings.addRange')}
              </Button>
            </fieldset>
          </form>
        </Modal>
      )}

      {deleteZone && (
        <ConfirmDialog
          title={t('tarification.settings.zoneDeleteTitle')}
          message={t('tarification.settings.zoneDeleteMessage', { code: deleteZone.code })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={async () => {
            const target = deleteZone
            setDeleteZone(null)
            try {
              await deletePricingZone(target.id)
              showSuccess(t('tarification.settings.zoneDeleted'))
              reload()
            } catch (err) {
              showError(localizeApiError(t, err, t('tarification.settings.zoneDeleteError')))
            }
          }}
          onCancel={() => setDeleteZone(null)}
        />
      )}
    </div>
  )
}
