import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { useCanViewPrices } from '../../auth/commercialVisibility'
import { createOrderForActivity, deleteDossierActivity, updateDossierActivity } from '../api/dossiersApi'
import type { DossierActivity, DossierDetail } from '../types'
import { DossierNotesPanel } from '../notes/DossierNotesPanel'
import type { ActivityDetailFocus } from './ActivityCard'
import type { DossierTab } from '../dossierSections'
import { ActivityOrderContext } from './ActivityOrderContext'
import { ActivityPlanningBlock } from './ActivityPlanningBlock'
import { SectionDrawer } from './SectionDrawer'
import { StandaloneExecutionBlock } from './StandaloneExecutionBlock'

interface ActivityDrawerProps {
  dossier: DossierDetail
  activity: DossierActivity
  canManage: boolean
  onClose: () => void
  /** Receives the refreshed dossier after save/delete/create-order. */
  onUpdated: (dossier: DossierDetail) => void
  /** 409: the dossier changed elsewhere — the page shows the rebase banner. Returns true when handled. */
  onConflict: (err: unknown) => boolean
  /** Silent apply of a refreshed dossier (planning block): no toast, the drawer stays open. */
  onApply?: (dossier: DossierDetail) => void
  /** Re-reads the dossier after a note change, so the card's note count/preview follow. */
  onReload?: () => void
  /** Part of the drawer a card action asked for; scrolled into view on open. */
  initialFocus?: ActivityDetailFocus
  /** B12: edit a slice of the activity's order on its dossier tab (route/goods/price/documents). */
  onOpenTab?: (tab: DossierTab, activity: DossierActivity) => void
}

/**
 * §11 activity drawer: label / planning / duur / notities / begeleiding voor standalone
 * activiteiten, plus "Transportopdracht aanmaken" voor transportactiviteiten zonder opdracht.
 * Sprint 2026-09-21: the Planning block (trip assignment, D1) and the activity's own notes.
 */
/** "Einde 10:30" from date + start + duration, or nothing while a part is missing. */
function endHint(t: TranslateFn, date: string, start: string, duration: string): string | undefined {
  const hours = Number(duration.replace(',', '.'))
  if (!date || !start || duration.trim() === '' || !Number.isFinite(hours) || hours < 0) return undefined
  const [h, m] = start.split(':').map(Number)
  const total = h * 60 + m + Math.round(hours * 60)
  const end = `${String(Math.floor(total / 60) % 24).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
  return total >= 24 * 60 ? t('dossierActivities.execution.endNextDay', { time: end }) : t('dossierActivities.execution.end', { time: end })
}

export function ActivityDrawer({
  dossier, activity, canManage, onClose, onUpdated, onConflict, onApply, onReload, initialFocus, onOpenTab,
}: ActivityDrawerProps) {
  const { t } = useLocale()
  const canViewPrices = useCanViewPrices()
  const [label, setLabel] = useState(activity.label ?? '')
  const [plannedDate, setPlannedDate] = useState(activity.plannedDate ?? '')
  const [durationHours, setDurationHours] = useState(activity.durationHours == null ? '' : String(activity.durationHours))
  // T-H7: execution facts of an operational standalone activity (crane work, plateau).
  const executable = !activity.hasStops && activity.isExecutable === true
  const [startTime, setStartTime] = useState(activity.startTime ? activity.startTime.slice(0, 5) : '')
  const [siteAddress, setSiteAddress] = useState(activity.siteAddress ?? '')
  const [workDescription, setWorkDescription] = useState(activity.workDescription ?? '')
  // A half-typed note counts as unsaved work: closing asks first, Opslaan refuses to drop it.
  const [noteDirty, setNoteDirty] = useState(false)
  const [planningDirty, setPlanningDirty] = useState(false)
  const planningRef = useRef<HTMLDivElement>(null)
  const notesRef = useRef<HTMLDivElement>(null)
  const [linkedActivityId, setLinkedActivityId] = useState(activity.linkedActivityId ?? '')
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  // A card action ("Planning", the note preview) lands on its part of the drawer.
  useEffect(() => {
    const target = initialFocus === 'planning' ? planningRef.current : initialFocus === 'notes' ? notesRef.current : null
    if (!target) return
    target.scrollIntoView?.({ block: 'start' })
    target.focus({ preventScroll: true })
  }, [initialFocus])

  const others = dossier.activities.filter((a) => a.id !== activity.id)
  const needsOrder = activity.hasStops && !activity.linkedTransportOrderId

  const touch = (setter: (value: string) => void) => (value: string) => {
    setter(value)
    setDirty(true)
  }

  async function run(action: () => Promise<DossierDetail>, fallback: string, closeAfter = true) {
    setBusy(true)
    setError(null)
    try {
      const updated = await action()
      onUpdated(updated)
      if (closeAfter) onClose()
    } catch (err) {
      if (onConflict(err)) {
        onClose()
        return
      }
      setError(describeApiError(err, fallback).message)
    } finally {
      setBusy(false)
    }
  }

  function save() {
    if (noteDirty) {
      // Opslaan closes the drawer; the note has its own Opslaan and would silently be lost.
      setError(t('dossierNotes.unsavedInDrawer'))
      return
    }
    if (planningDirty) {
      // Same for the planning block: it saves to the TRIP with its own button.
      setError(t('dossierActivities.planning.unsavedInDrawer'))
      return
    }
    const parsedDuration = durationHours.trim() === '' ? null : Number(durationHours.replace(',', '.'))
    if (parsedDuration !== null && (!Number.isFinite(parsedDuration) || parsedDuration < 0)) {
      setError(t('dossiers.drawer.durationInvalid'))
      return
    }
    void run(
      () =>
        updateDossierActivity(dossier.id, activity.id, {
          activityTypeId: activity.activityTypeId,
          label: label.trim() || null,
          plannedDate: plannedDate || null,
          durationHours: parsedDuration,
          linkedActivityId: linkedActivityId || null,
          // Legacy free text is no longer edited here (notes have their own panel): sent back unchanged.
          notes: activity.notes ?? null,
          version: dossier.version,
          ...(executable ? { startTime, siteAddress: siteAddress.trim(), workDescription: workDescription.trim() } : {}),
        }),
      t('dossiers.drawer.saveFailed'),
    )
  }

  // B12: an order-backed activity is read in context — the order's stops, goods, price and
  // documents sit in this drawer; the full order page is one secondary link away.
  const orderBacked = activity.hasStops && Boolean(activity.linkedTransportOrderId)
  const sections = [
    ...(orderBacked ? ['overzicht', 'stops', ...(activity.supportsGoods ? ['lading'] : []), ...(canViewPrices && activity.isBillable !== false ? ['prijs'] : [])] : []),
    ...(activity.hasStops ? ['planning'] : []),
    ...(orderBacked ? ['documenten'] : []),
    'notities',
  ]
  function jump(section: string) {
    const target = section === 'planning' ? planningRef.current : section === 'notities' ? notesRef.current : document.getElementById(`ad-sec-${section}`)
    target?.scrollIntoView?.({ block: 'start', behavior: 'smooth' })
  }
  const typeTitle = activity.label ? `${activity.activityTypeName} — ${activity.label}` : activity.activityTypeName

  return (
    <SectionDrawer
      title={activity.linkedOrderNumber ? `${activity.linkedOrderNumber} · ${typeTitle}` : typeTitle}
      context={
        <>
          <span>{t('dossiers.orderView.contextDossier')}</span>
          <code>{dossier.dossierNumber}</code>
          {dossier.customerName && <span>· {dossier.customerName}</span>}
        </>
      }
      headerAction={
        orderBacked ? (
          <Link to={`/transport-orders/${activity.linkedTransportOrderId}`} className="ad-full-order">
            {t('dossiers.orderView.openFull')}
          </Link>
        ) : undefined
      }
      dirty={dirty || noteDirty || planningDirty}
      busy={busy}
      onClose={onClose}
      onSave={canManage ? save : undefined}
      footerExtra={
        canManage ? (
          <Button variant="danger" onClick={() => setConfirmDelete(true)} disabled={busy}>
            {t('ui.actions.delete')}
          </Button>
        ) : undefined
      }
    >
      <ValidationSummary message={error} />

      {sections.length > 2 && (
        <nav className="ad-nav" aria-label={t('dossiers.orderView.navLabel')}>
          {sections.map((section) => (
            <button key={section} type="button" onClick={() => jump(section)}>
              {t(`dossiers.orderView.nav.${section}`)}
            </button>
          ))}
        </nav>
      )}

      {orderBacked && onOpenTab && (
        <ActivityOrderContext dossier={dossier} activity={activity} canEdit={canManage} onOpenTab={onOpenTab} />
      )}

      {orderBacked && <h4 className="ad-subhead">{t('dossiers.orderView.activityFields')}</h4>}

      {needsOrder && canManage && (
        <div className="dossier-activity-order-cta">
          <p>{t('dossiers.drawer.needsOrder')}</p>
          <Button
            onClick={() =>
              void run(
                () => createOrderForActivity(dossier.id, activity.id, dossier.version),
                t('dossiers.drawer.createOrderFailed'),
              )
            }
            disabled={busy}
          >
            {t('dossiers.drawer.createOrder')}
          </Button>
        </div>
      )}

      <FormField label={t('dossiers.drawer.label')} htmlFor="ad-label">
        <input
          id="ad-label"
          value={label}
          onChange={(event) => touch(setLabel)(event.target.value)}
          maxLength={200}
          disabled={busy || !canManage}
        />
      </FormField>
      {!activity.hasStops && (
        <FormField label={t('dossiers.drawer.plannedDate')} htmlFor="ad-date" hint={t('dossiers.drawer.plannedDateHint')}>
          <input
            id="ad-date"
            type="date"
            value={plannedDate}
            onChange={(event) => touch(setPlannedDate)(event.target.value)}
            disabled={busy || !canManage}
          />
        </FormField>
      )}
      {activity.allowsDuration && (
        <FormField label={t('dossiers.drawer.duration')} htmlFor="ad-duration">
          <input
            id="ad-duration"
            type="number"
            min={0}
            step="0.25"
            value={durationHours}
            onChange={(event) => touch(setDurationHours)(event.target.value)}
            disabled={busy || !canManage}
          />
        </FormField>
      )}
      {executable && (
        <>
          <FormField
            label={t('dossierActivities.execution.startTime')}
            htmlFor="ad-start"
            hint={endHint(t, plannedDate, startTime, durationHours)}
          >
            <input
              id="ad-start"
              type="time"
              value={startTime}
              onChange={(event) => touch(setStartTime)(event.target.value)}
              disabled={busy || !canManage}
            />
          </FormField>
          <FormField label={t('dossierActivities.execution.siteAddress')} htmlFor="ad-site">
            <input
              id="ad-site"
              value={siteAddress}
              onChange={(event) => touch(setSiteAddress)(event.target.value)}
              maxLength={500}
              placeholder={t('dossierActivities.execution.siteAddressPlaceholder')}
              disabled={busy || !canManage}
            />
          </FormField>
          <FormField label={t('dossierActivities.execution.workDescription')} htmlFor="ad-work">
            <textarea
              id="ad-work"
              rows={3}
              value={workDescription}
              onChange={(event) => touch(setWorkDescription)(event.target.value)}
              maxLength={2000}
              disabled={busy || !canManage}
            />
          </FormField>
        </>
      )}
      {others.length > 0 && (
        <FormField
          label={t('dossiers.drawer.linked')}
          htmlFor="ad-linked"
          hint={t('dossiers.drawer.linkedHint')}
        >
          <select
            id="ad-linked"
            value={linkedActivityId}
            onChange={(event) => touch(setLinkedActivityId)(event.target.value)}
            disabled={busy || !canManage}
          >
            <option value="">{t('dossiers.drawer.none')}</option>
            {others.map((other) => (
              <option key={other.id} value={other.id}>
                {other.label ? `${other.activityTypeName} — ${other.label}` : other.activityTypeName}
              </option>
            ))}
          </select>
        </FormField>
      )}

      <div ref={planningRef} tabIndex={-1} className="dossier-drawer-anchor">
        {activity.hasStops ? (
          <ActivityPlanningBlock
            dossier={dossier}
            activity={activity}
            isOpen={dossier.status === 'Open'}
            onApply={onApply ?? (() => {})}
            onConflict={onConflict}
            onDirtyChange={setPlanningDirty}
          />
        ) : (
          <StandaloneExecutionBlock
            dossier={dossier}
            activity={activity}
            canManage={canManage}
            drawerDirty={dirty}
            onApply={onApply ?? onUpdated}
            onConflict={onConflict}
            onDirtyChange={setPlanningDirty}
          />
        )}
      </div>

      {/* §19: notes of THIS activity only; self-saving, so independent of the drawer's Opslaan.
          The legacy free-text `activity.notes` is migrated into a first note server-side and only
          shown (read-only) when the notes cannot be loaded. */}
      <div ref={notesRef} tabIndex={-1} className="dossier-drawer-anchor">
        <DossierNotesPanel
          dossierId={dossier.id}
          activityId={activity.id}
          canWrite={canManage}
          title={t('dossierNotes.activityTitle')}
          compact
          legacyText={activity.notes}
          onDirtyChange={setNoteDirty}
          onChanged={onReload}
        />
      </div>

      {confirmDelete && (
        <ConfirmDialog
          title={t('dossiers.drawer.deleteTitle')}
          message={t('dossiers.drawer.deleteMessage')}
          confirmLabel={t('ui.actions.delete')}
          destructive
          busy={busy}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() =>
            void run(
              () => deleteDossierActivity(dossier.id, activity.id, dossier.version),
              t('dossiers.drawer.deleteFailed'),
            )
          }
        />
      )}
    </SectionDrawer>
  )
}
