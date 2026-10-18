import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { formatDate } from '../../../utils/dates'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { AccessDeniedState } from '../../../components/feedback/AccessDeniedState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { DetailCard } from '../../../components/ui/DetailCard'
import { useDetailTab, type DetailTabAlias } from '../../../hooks/useDetailTab'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { SaveStatus } from '../../../components/ui/SaveStatus'
import { useSaveStatus } from '../../../components/ui/useSaveStatus'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { AbsencesTab } from '../../absences/components/AbsencesTab'
import { getEmployeeAttention } from '../api/employeesApi'
import { CompletenessCard } from '../components/CompletenessCard'
import { EmployeeAttentionStrip } from '../components/EmployeeAttentionStrip'
import { EmployeeHistoryPanel } from '../components/EmployeeHistoryPanel'
import { EmployeeLifecycleDialog } from '../components/EmployeeLifecycleDialog'
import { getDriver, updateDriver } from '../../drivers/api/driversApi'
import { DriverProfilePanel } from '../../drivers/components/DriverProfilePanel'
import { IssuedItemsTab } from '../../issued-items/IssuedItemsTab'
import { EmployeeTankCardsSection } from '../components/EmployeeTankCardsSection'
import { LeaveBalanceTab } from '../../leave-balance/components/LeaveBalanceTab'
import { AttendanceTab } from '../../time-attendance/components/AttendanceTab'
import { EmployeeTasksTab } from '../../tasks/components/EmployeeTasksTab'
import { RedistributeTasksDialog } from '../../tasks/components/RedistributeTasksDialog'
import { getEmployeeOpenTaskSummary } from '../../tasks/api/tasksApi'
import { CreateUserAccountDialog } from '../components/CreateUserAccountDialog'
import { EmployeeDocumentsTab } from '../components/EmployeeDocumentsTab'
import { EmployeeForm, type EmployeeFormBlock } from '../components/EmployeeForm'
import { EmployeeOverview } from '../components/EmployeeOverview'
import { EmployeePlanningTab } from '../components/EmployeePlanningTab'
import { EmployeeTripsTab } from '../components/EmployeeTripsTab'
import { QualificationsTab } from '../components/QualificationsTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { contractEndBadge } from '../utils/employeeListBadges'
import { EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES, type EmployeeAttention, type EmployeeCompletenessItem, type EmployeeDetail, type EmployeeInput } from '../types/employee'
import { fullYearsSince } from '../utils/fullYearsSince'
import './EmployeeDetailPage.css'
import { ActionMenu } from '../../../components/ui/ActionMenu'

/**
 * B15: ONE section navigation layer. The overview is read-first (cards with their own
 * [Bewerken]); the old second navigation (a 10-item form rail duplicating Kwalificaties,
 * Documenten, Verlofsaldo and Bedrijfsmiddelen) is gone. Chauffeur combines the driver profile
 * with the trips; "Verlof & tijd" combines leave balance, absences and time registration.
 */
const TAB_IDS = ['profiel', 'planning', 'chauffeur', 'kwalificaties', 'documenten', 'verlof', 'taken', 'bedrijfsmiddelen', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

/**
 * Retired `?tab=` values (bookmarks, notification links) → their new home. useDetailTab rewrites
 * the URL once (replace) so it never keeps showing a retired id; every other param
 * (absenceId, documentId…) is kept.
 */
const TAB_ALIASES: Partial<Record<string, DetailTabAlias<TabId>>> = {
  verlofsaldo: { tab: 'verlof' },
  afwezigheden: { tab: 'verlof' },
  uren: { tab: 'verlof' },
  ritten: { tab: 'chauffeur' },
  chauffeursprofiel: { tab: 'chauffeur' },
}

/**
 * Legacy `?section=` deep links (completeness chips, HR reminders, old bookmarks) pointed at a
 * section of the old profile form. They now open the matching section tab, or the overview card
 * in edit mode for the core blocks.
 */
const SECTION_TARGETS: Record<string, { tab: TabId } | { block: EmployeeFormBlock[] } | { card: string }> = {
  chauffeursgegevens: { tab: 'chauffeur' },
  kwalificaties: { tab: 'kwalificaties' },
  documenten: { tab: 'documenten' },
  verlofsaldo: { tab: 'verlof' },
  bedrijfsmiddelen: { tab: 'bedrijfsmiddelen' },
  algemeen: { block: ['personal', 'contact'] },
  dienstverband: { block: ['dienstverband'] },
  hr: { block: ['hr'] },
  noodcontacten: { block: ['noodcontacten'] },
  notities: { card: 'employee-card-notities' },
}

/** Completeness codes of the "algemeen" section, split over the Persoonsgegevens/Contact cards. */
function completenessBlock(item: EmployeeCompletenessItem): EmployeeFormBlock | null {
  if (item.section === 'algemeen') return item.code === 'date_of_birth' ? 'personal' : 'contact'
  if (item.section === 'dienstverband' || item.section === 'hr' || item.section === 'noodcontacten') return item.section
  return null
}

const ALL_BLOCKS: EmployeeFormBlock[] = ['personal', 'contact', 'dienstverband', 'hr', 'noodcontacten']

export function EmployeeDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission, user } = useAuth()
  const { employee, isLoading, error, errorStatus, reload } = useEmployee(id)
  const mutations = useEmployeeMutations()
  // B31: a card save keeps the user on the overview — its state shows in that card's title row
  // ("✓ Opgeslagen · 14:22"); a failure keeps the card in edit mode with "Opnieuw proberen".
  const profileSave = useSaveStatus()
  const [lastProfileValues, setLastProfileValues] = useState<EmployeeInput | null>(null)
  // The overview card(s) in edit mode: one card, or every core block from the header [Bewerken].
  const [editingBlocks, setEditingBlocks] = useState<EmployeeFormBlock[] | null>(null)
  const [savedBlock, setSavedBlock] = useState<EmployeeFormBlock | null>(null)
  const [confirmLifecycle, setConfirmLifecycle] = useState<'deactivate' | 'reactivate' | null>(null)
  // Codes of the functions currently chosen in the edit form (null until the user touches them).
  const [editedFunctionCodes, setEditedFunctionCodes] = useState<string[] | null>(null)
  const [offerDriverDeactivation, setOfferDriverDeactivation] = useState(false)
  const [driverBusy, setDriverBusy] = useState(false)
  const [showAccountDialog, setShowAccountDialog] = useState(false)
  // Offered (never forced) after a successful deactivation when the employee still has open tasks.
  const [offerTaskRedistribution, setOfferTaskRedistribution] = useState(false)
  // Expiry warnings come with the detail load; after a document/qualification mutation they are
  // re-fetched on their own (cheaper than reloading the whole dossier). The refreshed value is
  // keyed on the employee object it was fetched for, so a later full reload supersedes it.
  const [attentionOverride, setAttentionOverride] = useState<{ source: unknown; value: EmployeeAttention | null } | null>(null)
  const attention: EmployeeAttention | null =
    attentionOverride && attentionOverride.source === employee ? attentionOverride.value : (employee?.attention ?? null)

  const refreshAttention = useCallback(() => {
    getEmployeeAttention(id)
      .then((value) => setAttentionOverride({ source: employee, value }))
      .catch(() => {
        /* de strip is afgeleide info; de mutatie zelf is al gelukt */
      })
  }, [id, employee])

  const canEdit = hasPermission('employees.edit')
  const canDeactivate = hasPermission('employees.deactivate')
  const canSeeConfidential = hasPermission('employees.view_confidential')
  const canViewPlanning = hasPermission('employee_planning.view') || hasPermission('employee_planning.manage')
  const canViewTrips = hasPermission('planning.view')
  const canViewDocuments = hasPermission('employee_documents.view')
  const [leaveReloadKey, setLeaveReloadKey] = useState(0)
  const canViewIssuedItems = hasPermission('issued_items.view') || hasPermission('issued_items.manage')
  const canViewLeaveBalance = hasPermission('leave_balances.view')
  const canViewAbsences = hasPermission('absences.view')
  const canViewAttendance = hasPermission('attendance.view')
  // tasks.view_own only covers the signed-in user's own employee record: on anyone else's page
  // the API answers 404, so the tab would only ever show a load error.
  const canViewTasks =
    hasPermission('tasks.view_team') ||
    hasPermission('tasks.view_all') ||
    (hasPermission('tasks.view_own') && !!user?.employeeId && user.employeeId === id)
  const canAssignTasks = hasPermission('tasks.assign')
  // Notes were reachable for editors through the profile form and for others through a card.
  const canViewNotes = canEdit || hasPermission('employee_notes.view')

  const hasDriver = Boolean(employee?.driverId)
  const visibleTabs: TabId[] = [
    'profiel',
    ...(canViewPlanning ? (['planning'] as const) : []),
    ...(hasDriver ? (['chauffeur'] as const) : []),
    'kwalificaties',
    ...(canViewDocuments ? (['documenten'] as const) : []),
    ...(canViewLeaveBalance || canViewAbsences || canViewAttendance ? (['verlof'] as const) : []),
    ...(canViewTasks ? (['taken'] as const) : []),
    ...(canViewIssuedItems ? (['bedrijfsmiddelen'] as const) : []),
    'historiek',
  ]
  // While the employee loads, every tab id is accepted so a deep link is not reset to Overzicht.
  const { tab, setTab, searchParams, setSearchParams } = useDetailTab<TabId>({
    ids: employee ? visibleTabs : TAB_IDS,
    defaultId: 'profiel',
    aliases: TAB_ALIASES,
  })

  const highlightDocumentId = searchParams.get('documentId')
  const highlightQualificationId = searchParams.get('qualificationId')
  const requestedSection = searchParams.get('section')

  // Legacy `?section=` deep link → its new home, once the employee is there. Opening a card in
  // edit mode is derived during render (once per requested section); the effect below only
  // rewrites the URL (the param is consumed so a refresh does not reopen an edit form) and scrolls.
  const [handledSection, setHandledSection] = useState<string | null>(null)
  if (employee && requestedSection && requestedSection !== handledSection) {
    setHandledSection(requestedSection)
    const target = SECTION_TARGETS[requestedSection]
    if (target && 'block' in target && canEdit) {
      setEditingBlocks(target.block)
      setSavedBlock(null)
    }
  }

  useEffect(() => {
    if (!employee || !requestedSection) return
    const target = SECTION_TARGETS[requestedSection]
    if (target && 'tab' in target) {
      setTab(target.tab)
      return
    }
    const next = new URLSearchParams(searchParams)
    next.delete('section')
    next.delete('tab')
    setSearchParams(next, { replace: true })
    const anchor = target && 'card' in target ? target.card : target && 'block' in target ? `employee-card-${target.block[0]}` : null
    if (anchor) window.setTimeout(() => document.getElementById(anchor)?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }), 0)
    // Only when the requested section or the loaded employee changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestedSection, employee?.id])

  if (isLoading) return <LoadingState message={t('employees.detail.loading')} />
  // A missing employee is not a hiccup: no retry, just the way back. 403 = no rights (defence in
  // depth behind the route guard).
  if (errorStatus === 404) {
    return <ErrorState message={t('employees.errors.employeeNotFound')} backTo="/employees" />
  }
  if (errorStatus === 403) return <AccessDeniedState />
  if (error || !employee) return <ErrorState message={error ?? t('employees.errors.employeeNotFound')} onRetry={reload} backTo="/employees" />

  const fullName = `${employee.firstName} ${employee.lastName}`

  function openBlocks(blocks: EmployeeFormBlock[]) {
    setEditingBlocks(blocks)
    setSavedBlock(null)
    profileSave.setState({ kind: 'idle' })
    if (tab !== 'profiel') setTab('profiel')
    window.setTimeout(() => document.getElementById(`employee-card-${blocks[0]}`)?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }), 0)
  }

  /** Missing-item action → the card that owns it (edit mode) or the Documenten section. */
  function goToCompleteness(section: string, item: EmployeeCompletenessItem) {
    if (section === 'documenten') {
      if (canViewDocuments) setTab('documenten')
      return
    }
    const block = completenessBlock(item)
    if (block) openBlocks([block])
  }

  /** A "documenten" missing item only gets an action when the viewer can open that section. */
  function canNavigateCompleteness(section: string) {
    if (section === 'documenten') return canViewDocuments
    // Rijksregisternummer/IBAN live in the confidential block: without the permission there is
    // no field to land on, so the gap stays informational instead of a dead "Aanvullen".
    if (section === 'hr') return hasPermission('employees.view_confidential')
    return true
  }

  async function saveProfile(current: EmployeeDetail, values: EmployeeInput) {
    setLastProfileValues(values)
    profileSave.setState({ kind: 'saving' })
    const updated = await mutations.update(current.id, values)
    if (!updated) {
      // The validation summary inside the card carries the details; this line offers the retry.
      profileSave.setState({ kind: 'error' })
      return
    }
    profileSave.setState({ kind: 'saved', at: new Date().toISOString() })
    setSavedBlock(editingBlocks && editingBlocks.length === 1 ? editingBlocks[0] : 'personal')
    setEditingBlocks(null)
    // Driver functions removed while a driver profile exists → offer (never force)
    // deactivating that profile. Historical driver data is always preserved.
    const removedDriverFunctions =
      current.driverId !== null &&
      editedFunctionCodes !== null &&
      !editedFunctionCodes.some((code) => code.toUpperCase().startsWith('CHAUF'))
    if (removedDriverFunctions) {
      setOfferDriverDeactivation(true)
    }
    reload()
  }

  async function copyEmployeeNumber() {
    try {
      await navigator.clipboard.writeText(employee!.employeeNumber)
      toast.showSuccess(t('employees.detail.numberCopied'))
    } catch {
      toast.showError(t('employees.detail.copyFailed'))
    }
  }

  const editingAll = editingBlocks !== null && editingBlocks.length > 1
  const editor =
    editingBlocks && canEdit ? (
      <EmployeeForm
        key={editingBlocks.join('-')}
        mode="edit"
        initial={employee}
        blocks={editingBlocks.filter((block) => block !== 'hr' || canSeeConfidential)}
        isSubmitting={mutations.isSubmitting}
        submitError={mutations.error}
        serverFieldErrors={mutations.fieldErrors}
        onCancel={() => setEditingBlocks(null)}
        onFunctionsChanged={setEditedFunctionCodes}
        onSubmit={(values) => void saveProfile(employee, values)}
        saveStatus={
          <SaveStatus
            state={profileSave.state}
            onRetry={lastProfileValues ? () => void saveProfile(employee, lastProfileValues) : undefined}
          />
        }
      />
    ) : null

  const endBadge = contractEndBadge(employee)
  const years = fullYearsSince(employee.employmentStartDate)
  const canCreateDriver = !employee.driverId && hasPermission('drivers.create') && employee.isActive
  const menuItems = [
    ...(canCreateDriver
      ? [{ key: 'driver', label: t('employees.detail.createDriverProfileAction'), onSelect: () => navigate(`/drivers/new?employeeId=${employee.id}`) }]
      : []),
    ...(canDeactivate
      ? [
          employee.isActive
            ? { key: 'deactivate', label: t('employees.detail.deactivate'), onSelect: () => setConfirmLifecycle('deactivate'), danger: true }
            : { key: 'reactivate', label: t('employees.detail.reactivate'), onSelect: () => setConfirmLifecycle('reactivate') },
        ]
      : []),
  ]

  const tabLabels: Record<TabId, string> = {
    profiel: t('employees.detail.tabOverview'),
    planning: t('employees.detail.tabPlanning'),
    chauffeur: t('employees.detail.tabDriver'),
    kwalificaties: t('employees.detail.tabQualifications'),
    documenten: t('employees.detail.tabDocuments'),
    verlof: t('employees.detail.tabLeaveAndTime'),
    taken: t('employees.detail.tabTasks'),
    bedrijfsmiddelen: t('employees.detail.tabIssuedItems'),
    historiek: t('employees.detail.tabHistory'),
  }

  return (
    <div className="employee-detail">
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.personeel'), to: '/employees' }, { label: fullName }]} />
      <PageHeader
        title={fullName}
        badges={
          <>
            <StatusBadges
              active={employee.isActive}
              operational={{
                label: t(EMPLOYMENT_STATUS_LABELS[employee.employmentStatus]),
                tone: EMPLOYMENT_STATUS_TONES[employee.employmentStatus],
              }}
            />
            {endBadge && <Badge tone={endBadge.tone}>{t(endBadge.key, endBadge.params)}</Badge>}
          </>
        }
        subtitle={
          <span className="employee-header-subtitle">
            <button
              type="button"
              className="employee-number-copy"
              onClick={copyEmployeeNumber}
              title={t('employees.detail.copyNumberTitle')}
            >
              {employee.employeeNumber}
            </button>
            {employee.functionNames.length > 0 && <> · {employee.functionNames.join(', ')}</>}
            {employee.employmentStartDate && (
              <>
                {' '}
                · {t('employees.detail.inServiceSince', { date: formatDate(employee.employmentStartDate) })}
                {years !== null && years >= 1 ? ` · ${t('employees.detail.yearsOfService', { count: years })}` : ''}
              </>
            )}
            {(employee.email || employee.phoneNumber) && (
              <>
                {' '}
                ·{' '}
                {employee.email && <a href={`mailto:${employee.email}`}>{employee.email}</a>}
                {employee.email && employee.phoneNumber && ' · '}
                {employee.phoneNumber && <a href={`tel:${employee.phoneNumber}`}>{employee.phoneNumber}</a>}
              </>
            )}
          </span>
        }
        action={
          <>
            {hasPermission('users.create') && (
              <Button variant="secondary" onClick={() => setShowAccountDialog(true)} disabled={mutations.isSubmitting}>
                {t('employees.detail.createAccount')}
              </Button>
            )}
            {menuItems.length > 0 && <ActionMenu disabled={mutations.isSubmitting} items={menuItems} />}
            {canEdit && (
              <Button onClick={() => openBlocks(ALL_BLOCKS)} disabled={editingAll}>
                {t('ui.actions.edit')}
              </Button>
            )}
          </>
        }
      />

      <EmployeeAttentionStrip attention={attention} />

      <Tabs
        tabs={visibleTabs.map((tabId) => ({ id: tabId, label: tabLabels[tabId] }))}
        activeId={tab}
        onChange={(next) => setTab(next as TabId)}
      />

      {tab === 'profiel' && (
        <TabPanel tabId="profiel">
          {editingAll ? (
            <DetailCard id="employee-card-personal" title={t('employees.overview.editAll')}>
              {editor}
            </DetailCard>
          ) : (
            <EmployeeOverview
              employee={employee}
              editing={editingBlocks ? editingBlocks[0] : null}
              editor={editor}
              onEdit={canEdit ? (block) => openBlocks([block]) : undefined}
              savedBlock={savedBlock}
              saveStatus={<SaveStatus state={profileSave.state} />}
              canSeeConfidential={canSeeConfidential}
              canViewNotes={canViewNotes}
              onOpenDriver={hasDriver ? () => setTab('chauffeur') : undefined}
              completeness={
                employee.completeness && (
                  <CompletenessCard
                    completeness={employee.completeness}
                    onNavigate={canEdit ? goToCompleteness : undefined}
                    canNavigate={canNavigateCompleteness}
                  />
                )
              }
            />
          )}
        </TabPanel>
      )}

      {tab === 'planning' && canViewPlanning && (
        <TabPanel tabId="planning">
          <EmployeePlanningTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'chauffeur' && employee.driverId && (
        <TabPanel tabId="chauffeur">
          <section id="chauffeursgegevens" className="employee-section-block">
            <DriverProfilePanel driverId={employee.driverId} onChanged={reload} onDeleted={reload} />
          </section>
          {canViewTrips && (
            <section className="employee-section-block">
              <h3 className="employee-section-title">{t('employees.detail.tabTrips')}</h3>
              <EmployeeTripsTab driverId={employee.driverId} />
            </section>
          )}
        </TabPanel>
      )}

      {tab === 'kwalificaties' && (
        <TabPanel tabId="kwalificaties">
          <QualificationsTab employeeId={employee.id} highlightQualificationId={highlightQualificationId} onChanged={refreshAttention} />
        </TabPanel>
      )}

      {tab === 'documenten' && canViewDocuments && (
        <TabPanel tabId="documenten">
          <EmployeeDocumentsTab employeeId={employee.id} highlightDocumentId={highlightDocumentId} onChanged={refreshAttention} />
        </TabPanel>
      )}

      {tab === 'verlof' && (
        <TabPanel tabId="verlof">
          {canViewLeaveBalance && (
            <section className="employee-section-block">
              <LeaveBalanceTab employeeId={employee.id} reloadKey={leaveReloadKey} />
            </section>
          )}
          {canViewAbsences && (
            <section className="employee-section-block">
              <AbsencesTab
                employeeId={employee.id}
                highlightAbsenceId={searchParams.get('absenceId')}
                onChanged={() => setLeaveReloadKey((key) => key + 1)}
              />
            </section>
          )}
          {canViewAttendance && (
            <section className="employee-section-block">
              <h3 className="employee-section-title">{t('employees.detail.tabAttendance')}</h3>
              <AttendanceTab employeeId={employee.id} />
            </section>
          )}
        </TabPanel>
      )}

      {tab === 'taken' && canViewTasks && (
        <TabPanel tabId="taken">
          <EmployeeTasksTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'bedrijfsmiddelen' && canViewIssuedItems && (
        <TabPanel tabId="bedrijfsmiddelen">
          <IssuedItemsTab employeeId={employee.id} employeeName={fullName} />
          <EmployeeTankCardsSection employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'historiek' && (
        <TabPanel tabId="historiek">
          <EmployeeHistoryPanel employeeId={employee.id} />
        </TabPanel>
      )}

      {offerDriverDeactivation && employee.driverId && (
        <ConfirmDialog
          title={t('employees.detail.deactivateDriverTitle')}
          message={t('employees.detail.deactivateDriverMessage')}
          confirmLabel={t('employees.detail.deactivateDriverConfirm')}
          cancelLabel={t('employees.detail.deactivateDriverCancel')}
          busy={driverBusy}
          onConfirm={async () => {
            setDriverBusy(true)
            try {
              const driver = await getDriver(employee.driverId!)
              await updateDriver(employee.driverId!, {
                // Both category fields null = leave the category set untouched; this action
                // only deactivates the profile.
                driverCategoryId: null,
                driverCategoryIds: null,
                availabilityStatus: driver.availabilityStatus,
                isActive: false,
                fixedTrailerId: driver.fixedTrailerId,
                notes: driver.notes,
              })
              toast.showSuccess(t('employees.detail.driverDeactivated'))
            } catch {
              toast.showError(t('employees.detail.driverDeactivateFailed'))
            } finally {
              setDriverBusy(false)
              setOfferDriverDeactivation(false)
            }
          }}
          onCancel={() => setOfferDriverDeactivation(false)}
        />
      )}

      {confirmLifecycle === 'deactivate' && (
        <EmployeeLifecycleDialog
          mode="deactivate"
          employeeId={employee.id}
          employeeName={fullName}
          onDone={() => {
            toast.showSuccess(t('employees.detail.deactivated'))
            setConfirmLifecycle(null)
            reload()
            // Offer (never force) redistributing any open tasks left behind — only meaningful
            // for users who may reassign them.
            if (canAssignTasks) {
              getEmployeeOpenTaskSummary(employee.id)
                .then((summary) => {
                  const openCount = summary.todo + summary.inProgress + summary.blocked + summary.waitingForReview
                  if (openCount > 0) setOfferTaskRedistribution(true)
                })
                .catch(() => {
                  /* takenoverzicht is optioneel; deactivatie is al gelukt */
                })
            }
          }}
          onCancel={() => setConfirmLifecycle(null)}
        />
      )}

      {offerTaskRedistribution && (
        <RedistributeTasksDialog
          employeeId={employee.id}
          employeeName={`${employee.firstName} ${employee.lastName}`}
          onClose={() => setOfferTaskRedistribution(false)}
        />
      )}

      {confirmLifecycle === 'reactivate' && (
        <EmployeeLifecycleDialog
          mode="reactivate"
          employeeId={employee.id}
          employeeName={`${employee.firstName} ${employee.lastName}`}
          onDone={() => {
            // The login and the driver profile stay inactive on purpose — say so after the fact too.
            toast.showSuccess(`${t('employees.detail.reactivated')} ${t('employees.detail.reactivateHint')}`)
            setConfirmLifecycle(null)
            reload()
          }}
          onCancel={() => setConfirmLifecycle(null)}
        />
      )}

      {showAccountDialog && (
        <CreateUserAccountDialog
          employeeId={employee.id}
          firstName={employee.firstName}
          lastName={employee.lastName}
          email={employee.email}
          onClose={(created) => {
            setShowAccountDialog(false)
            if (created) toast.showSuccess(t('employees.detail.accountCreated'))
          }}
        />
      )}
    </div>
  )
}
