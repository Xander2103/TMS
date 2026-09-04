import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { formatDate } from '../../../utils/dates'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import type { SectionDef } from '../../../components/ui/SectionedForm'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { AbsencesTab } from '../../absences/components/AbsencesTab'
import { CompletenessCard } from '../components/CompletenessCard'
import { EmployeeHistoryPanel } from '../components/EmployeeHistoryPanel'
import { EmployeeNotesPanel } from '../components/EmployeeNotesPanel'
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
import { EmployeeForm } from '../components/EmployeeForm'
import { EmployeePlanningTab } from '../components/EmployeePlanningTab'
import { EmployeeTripsTab } from '../components/EmployeeTripsTab'
import { QualificationsTab } from '../components/QualificationsTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { contractEndBadge } from '../utils/employeeListBadges'
import { CIVIL_STATUS_LABELS, EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES } from '../types/employee'
import { fullYearsSince } from '../utils/fullYearsSince'
import './EmployeeDetailPage.css'

const TAB_IDS = ['profiel', 'planning', 'kwalificaties', 'documenten', 'verlof', 'uren', 'taken', 'ritten', 'bedrijfsmiddelen', 'historiek'] as const
type TabId = (typeof TAB_IDS)[number]

/**
 * Legacy `?tab=` values from before the navigation redesign (verlofsaldo/afwezigheden merged
 * into "verlof"; chauffeursprofiel moved into a profile section). Deep links using these ids
 * are redirected on load (`setSearchParams(..., { replace: true })`) so the URL never keeps
 * showing a retired id.
 */
const TAB_ALIASES: Partial<Record<string, { tab: TabId; section?: string }>> = {
  verlofsaldo: { tab: 'verlof' },
  afwezigheden: { tab: 'verlof' },
  chauffeursprofiel: { tab: 'profiel', section: 'chauffeursgegevens' },
}

export function EmployeeDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const { employee, isLoading, error, reload } = useEmployee(id)
  const mutations = useEmployeeMutations()
  const [confirmLifecycle, setConfirmLifecycle] = useState<'deactivate' | 'reactivate' | null>(null)
  // Codes of the functions currently chosen in the edit form (null until the user touches them).
  const [editedFunctionCodes, setEditedFunctionCodes] = useState<string[] | null>(null)
  const [offerDriverDeactivation, setOfferDriverDeactivation] = useState(false)
  const [driverBusy, setDriverBusy] = useState(false)
  const [showAccountDialog, setShowAccountDialog] = useState(false)
  // Offered (never forced) after a successful deactivation when the employee still has open tasks.
  const [offerTaskRedistribution, setOfferTaskRedistribution] = useState(false)

  const requestedTab = searchParams.get('tab')
  const alias = requestedTab ? TAB_ALIASES[requestedTab] : undefined
  const tab: TabId = alias ? alias.tab : TAB_IDS.includes(requestedTab as TabId) ? (requestedTab as TabId) : 'profiel'
  const requestedSection = alias?.section ?? searchParams.get('section') ?? undefined

  const canEdit = hasPermission('employees.edit')
  const canDeactivate = hasPermission('employees.deactivate')
  const canViewPlanning = hasPermission('employee_planning.view') || hasPermission('employee_planning.manage')
  const canViewTrips = hasPermission('planning.view')
  const canViewDocuments = hasPermission('employee_documents.view')
  const canViewIssuedItems = hasPermission('issued_items.view') || hasPermission('issued_items.manage')
  const canViewLeaveBalance = hasPermission('leave_balances.view')
  const canViewAbsences = hasPermission('absences.view')
  const canViewAttendance = hasPermission('attendance.view')
  const canViewTasks =
    hasPermission('tasks.view_own') || hasPermission('tasks.view_team') || hasPermission('tasks.view_all')
  const canAssignTasks = hasPermission('tasks.assign')

  // Redirect a legacy `?tab=` alias to its new home so the URL never keeps a retired id.
  useEffect(() => {
    if (!alias) return
    const next = new URLSearchParams(searchParams)
    if (alias.tab === 'profiel') next.delete('tab')
    else next.set('tab', alias.tab)
    if (alias.section) next.set('section', alias.section)
    setSearchParams(next, { replace: true })
    // Only re-run when the raw ?tab= value changes; `alias`/`searchParams` are derived from it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestedTab])

  // Scroll the read-only profile straight to the driver block for the "chauffeursgegevens"
  // deep link — the edit-mode form handles it via EmployeeForm's `initialSectionId` instead.
  useEffect(() => {
    if (!canEdit && requestedSection === 'chauffeursgegevens') {
      document.getElementById('chauffeursgegevens')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [canEdit, requestedSection])

  if (isLoading) return <LoadingState message={t('employees.detail.loading')} />
  if (error || !employee) return <ErrorState message={error ?? t('employees.errors.employeeNotFound')} />

  function setTab(next: string) {
    setSearchParams(next === 'profiel' ? {} : { tab: next }, { replace: true })
  }

  /** Jumps to the profile tab with the "Chauffeursgegevens" section/block pre-selected. */
  function goToDriverSection() {
    const next = new URLSearchParams(searchParams)
    next.delete('tab')
    next.set('section', 'chauffeursgegevens')
    setSearchParams(next, { replace: true })
  }

  /** Missing-item chip → its dossier home: "documenten" is a page-level tab, the rest are
   * profile-form sections reached via `?section=` (same mechanism as `goToDriverSection`). */
  function goToCompletenessSection(section: string) {
    if (section === 'documenten') {
      // Guards against a stray call; the chip itself is already non-clickable for this case
      // (see `canNavigateCompleteness` passed to CompletenessCard below).
      if (!canViewDocuments) return
      setTab('documenten')
      return
    }
    const next = new URLSearchParams(searchParams)
    next.delete('tab')
    next.set('section', section)
    setSearchParams(next, { replace: true })
  }

  /** A "documenten" missing item can only be a real link when the viewer can actually open that
   * tab; every other section only needs `employees.edit` (already gated by the caller). */
  function canNavigateCompleteness(section: string) {
    return section !== 'documenten' || canViewDocuments
  }

  async function copyEmployeeNumber() {
    try {
      await navigator.clipboard.writeText(employee!.employeeNumber)
      toast.showSuccess(t('employees.detail.numberCopied'))
    } catch {
      toast.showError(t('employees.detail.copyFailed'))
    }
  }

  // Edit-only self-saving panels, embedded as sections of the profile form (they remain
  // reachable as page tabs too). `panel: true` hides the form's shared Save — each panel
  // saves through its own existing API.
  const editExtraSections: SectionDef[] = [
    {
      id: 'chauffeursgegevens',
      label: t('employees.sections.chauffeursgegevens'),
      optional: true,
      panel: true,
      render: () =>
        employee.driverId ? (
          <DriverProfilePanel driverId={employee.driverId} onChanged={reload} onDeleted={reload} />
        ) : hasPermission('drivers.create') && employee.isActive ? (
          <p className="placeholder-text">
            <Link to={`/drivers/new?employeeId=${employee.id}`}>{t('employees.detail.createDriverProfile')}</Link>
          </p>
        ) : (
          <p className="placeholder-text">{t('employees.detail.noDriverProfile')}</p>
        ),
    },
    {
      id: 'kwalificaties',
      label: t('employees.sections.kwalificaties'),
      optional: true,
      panel: true,
      render: () => <QualificationsTab employeeId={employee.id} />,
    },
    {
      id: 'documenten',
      label: t('employees.sections.documenten'),
      optional: true,
      panel: true,
      render: () =>
        canViewDocuments ? (
          <EmployeeDocumentsTab employeeId={employee.id} />
        ) : (
          <p className="placeholder-text">{t('employees.detail.noDocumentsPermission')}</p>
        ),
    },
    {
      id: 'verlofsaldo',
      label: t('employees.sections.verlofsaldo'),
      optional: true,
      panel: true,
      render: () =>
        canViewLeaveBalance ? (
          <LeaveBalanceTab employeeId={employee.id} />
        ) : (
          <p className="placeholder-text">{t('employees.detail.noLeaveBalancePermission')}</p>
        ),
    },
    {
      id: 'bedrijfsmiddelen',
      label: t('employees.sections.bedrijfsmiddelen'),
      optional: true,
      panel: true,
      render: () =>
        canViewIssuedItems ? (
          <>
            <IssuedItemsTab employeeId={employee.id} employeeName={`${employee.firstName} ${employee.lastName}`} />
            <EmployeeTankCardsSection employeeId={employee.id} />
          </>
        ) : (
          <p className="placeholder-text">{t('employees.detail.noIssuedItemsPermission')}</p>
        ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.personeel'), to: '/employees' }, { label: `${employee.firstName} ${employee.lastName}` }]} />
      <PageHeader
        title={`${employee.firstName} ${employee.lastName}`}
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
                {(() => {
                  const years = fullYearsSince(employee.employmentStartDate)
                  return years !== null && years >= 1
                    ? ` · ${t('employees.detail.yearsOfService', { count: years })}`
                    : ''
                })()}
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
            {canDeactivate && (
              <Button
                variant={employee.isActive ? 'danger' : 'secondary'}
                onClick={() => setConfirmLifecycle(employee.isActive ? 'deactivate' : 'reactivate')}
                disabled={mutations.isSubmitting}
              >
                {employee.isActive ? t('employees.detail.deactivate') : t('employees.detail.reactivate')}
              </Button>
            )}
          </>
        }
      />

      {employee.completeness && (
        <CompletenessCard
          completeness={employee.completeness}
          onNavigate={canEdit ? goToCompletenessSection : undefined}
          canNavigate={canNavigateCompleteness}
        />
      )}

      <div className="employee-detail-status">
        <StatusBadges
          active={employee.isActive}
          operational={{
            label: t(EMPLOYMENT_STATUS_LABELS[employee.employmentStatus]),
            tone: EMPLOYMENT_STATUS_TONES[employee.employmentStatus],
          }}
        />
        {(() => {
          const endBadge = contractEndBadge(employee)
          return endBadge && <Badge tone={endBadge.tone}>{t(endBadge.key, endBadge.params)}</Badge>
        })()}
        {employee.driverId ? (
          <button type="button" className="employee-driver-link employee-driver-link-button" onClick={goToDriverSection}>
            {t('employees.detail.viewDriverData')}
          </button>
        ) : (
          hasPermission('drivers.create') &&
          employee.isActive && (
            <Link to={`/drivers/new?employeeId=${employee.id}`} className="employee-driver-link">
              {t('employees.detail.createDriverProfile')}
            </Link>
          )
        )}
      </div>

      <Tabs
        tabs={[
          { id: 'profiel', label: t('employees.detail.tabOverview') },
          ...(canViewPlanning ? [{ id: 'planning', label: t('employees.detail.tabPlanning') }] : []),
          { id: 'kwalificaties', label: t('employees.detail.tabQualifications') },
          ...(canViewDocuments ? [{ id: 'documenten', label: t('employees.detail.tabDocuments') }] : []),
          ...(canViewLeaveBalance || canViewAbsences ? [{ id: 'verlof', label: t('employees.detail.tabLeave') }] : []),
          ...(canViewAttendance ? [{ id: 'uren', label: t('employees.detail.tabAttendance') }] : []),
          ...(canViewTasks ? [{ id: 'taken', label: t('employees.detail.tabTasks') }] : []),
          ...(employee.driverId && canViewTrips ? [{ id: 'ritten', label: t('employees.detail.tabTrips') }] : []),
          ...(canViewIssuedItems ? [{ id: 'bedrijfsmiddelen', label: t('employees.detail.tabIssuedItems') }] : []),
          { id: 'historiek', label: t('employees.detail.tabHistory') },
        ]}
        activeId={tab}
        onChange={setTab}
      />

      {tab === 'profiel' && (
        <TabPanel tabId="profiel">
          {!canEdit && hasPermission('employee_notes.view') && (
            <section className="employee-notes-card">
              <h3>{t('employees.detail.notesHeading')}</h3>
              <EmployeeNotesPanel employeeId={employee.id} />
            </section>
          )}
          {canEdit ? (
            <EmployeeForm
              key={requestedSection ?? 'default'}
              mode="edit"
              initial={employee}
              isSubmitting={mutations.isSubmitting}
              submitError={mutations.error}
              serverFieldErrors={mutations.fieldErrors}
              onCancel={() => navigate('/employees')}
              onFunctionsChanged={setEditedFunctionCodes}
              extraSections={editExtraSections}
              initialSectionId={requestedSection}
              onSubmit={async (values) => {
                const updated = await mutations.update(employee.id, values)
                if (updated) {
                  toast.showSuccess(t('employees.detail.updated'))
                  // Driver functions removed while a driver profile exists → offer (never force)
                  // deactivating that profile. Historical driver data is always preserved.
                  const removedDriverFunctions =
                    employee.driverId !== null &&
                    editedFunctionCodes !== null &&
                    !editedFunctionCodes.some((code) => code.toUpperCase().startsWith('CHAUF'))
                  if (removedDriverFunctions) {
                    setOfferDriverDeactivation(true)
                  }
                  reload()
                }
              }}
            />
          ) : (
            <div className="employee-readonly-profile">
              <p className="placeholder-text">{t('employees.detail.readOnlyProfile')}</p>
              <dl className="employee-readonly-grid">
                <div>
                  <dt>{t('employees.detail.dateOfBirth')}</dt>
                  <dd>
                    {employee.dateOfBirth
                      ? `${formatDate(employee.dateOfBirth)}${(() => {
                          const age = fullYearsSince(employee.dateOfBirth)
                          return age !== null ? ` ${t('employees.detail.ageSuffix', { age })}` : ''
                        })()}`
                      : '—'}
                  </dd>
                </div>
                <div>
                  <dt>{t('employees.detail.email')}</dt>
                  <dd>{employee.email ? <a href={`mailto:${employee.email}`}>{employee.email}</a> : '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.phone')}</dt>
                  <dd>{employee.phoneNumber ? <a href={`tel:${employee.phoneNumber}`}>{employee.phoneNumber}</a> : '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.mobile')}</dt>
                  <dd>{employee.mobilePhone ? <a href={`tel:${employee.mobilePhone}`}>{employee.mobilePhone}</a> : '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.civilStatus')}</dt>
                  <dd>{employee.civilStatus ? t(CIVIL_STATUS_LABELS[employee.civilStatus]) : '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.dependentChildren')}</dt>
                  <dd>{employee.dependentChildren ?? '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.dimonaNumber')}</dt>
                  <dd>{employee.dimonaNumber ?? '—'}</dd>
                </div>
                <div>
                  <dt>{t('employees.detail.employmentEndDate')}</dt>
                  <dd>{formatDate(employee.employmentEndDate) || '—'}</dd>
                </div>
              </dl>
              <h3 className="employee-readonly-subtitle">{t('employees.detail.emergencyContactsHeading')}</h3>
              {employee.emergencyContacts.length === 0 ? (
                <p className="placeholder-text">{t('employees.detail.noEmergencyContacts')}</p>
              ) : (
                <ul className="employee-readonly-contacts">
                  {[...employee.emergencyContacts]
                    .sort((a, b) => a.priority - b.priority)
                    .map((contact) => (
                      <li key={contact.id}>
                        <span className="employee-readonly-contact-name">{contact.name}</span>
                        {contact.relationship && <span> · {contact.relationship}</span>}
                        {(contact.phone || contact.mobilePhone) && (
                          <span> · {[contact.phone, contact.mobilePhone].filter(Boolean).join(' / ')}</span>
                        )}
                      </li>
                    ))}
                </ul>
              )}
            </div>
          )}
          {!canEdit && employee.driverId && (
            <section className="employee-readonly-driver">
              <h3 id="chauffeursgegevens" className="employee-readonly-subtitle">
                {t('employees.detail.driverDataHeading')}
              </h3>
              <DriverProfilePanel driverId={employee.driverId} onChanged={reload} onDeleted={reload} />
            </section>
          )}
        </TabPanel>
      )}

      {tab === 'planning' && canViewPlanning && (
        <TabPanel tabId="planning">
          <EmployeePlanningTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'kwalificaties' && (
        <TabPanel tabId="kwalificaties">
          <QualificationsTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'documenten' && canViewDocuments && (
        <TabPanel tabId="documenten">
          <EmployeeDocumentsTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'verlof' && (canViewLeaveBalance || canViewAbsences) && (
        <TabPanel tabId="verlof">
          {canViewLeaveBalance ? (
            <LeaveBalanceTab employeeId={employee.id} />
          ) : (
            <p className="placeholder-text">{t('employees.detail.noLeaveBalancePermission')}</p>
          )}
          {canViewAbsences ? (
            <AbsencesTab employeeId={employee.id} highlightAbsenceId={searchParams.get('absenceId')} />
          ) : (
            <p className="placeholder-text">{t('employees.detail.noAbsencesPermission')}</p>
          )}
        </TabPanel>
      )}

      {tab === 'uren' && canViewAttendance && (
        <TabPanel tabId="uren">
          <AttendanceTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'taken' && canViewTasks && (
        <TabPanel tabId="taken">
          <EmployeeTasksTab employeeId={employee.id} />
        </TabPanel>
      )}

      {tab === 'ritten' && employee.driverId && canViewTrips && (
        <TabPanel tabId="ritten">
          <EmployeeTripsTab driverId={employee.driverId} />
        </TabPanel>
      )}

      {tab === 'bedrijfsmiddelen' && canViewIssuedItems && (
        <TabPanel tabId="bedrijfsmiddelen">
          <IssuedItemsTab employeeId={employee.id} employeeName={`${employee.firstName} ${employee.lastName}`} />
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
        <ConfirmDialog
          title={t('employees.detail.deactivateTitle')}
          message={t('employees.detail.deactivateMessage', { name: `${employee.firstName} ${employee.lastName}` })}
          confirmLabel={t('employees.detail.deactivateConfirm')}
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.deactivate(employee.id)
            if (ok) {
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
        <ConfirmDialog
          title={t('employees.detail.reactivateTitle')}
          message={t('employees.detail.reactivateMessage', { name: `${employee.firstName} ${employee.lastName}` })}
          confirmLabel={t('employees.detail.reactivate')}
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.reactivate(employee.id)
            if (ok) {
              toast.showSuccess(t('employees.detail.reactivated'))
              setConfirmLifecycle(null)
              reload()
            }
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
