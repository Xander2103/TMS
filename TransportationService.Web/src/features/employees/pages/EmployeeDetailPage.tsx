import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import type { SectionDef } from '../../../components/ui/SectionedForm'
import { TabPanel, Tabs } from '../../../components/ui/Tabs'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { AbsencesTab } from '../../absences/components/AbsencesTab'
import { EmployeeHistoryPanel } from '../components/EmployeeHistoryPanel'
import { EmployeeNotesPanel } from '../components/EmployeeNotesPanel'
import { getDriver, updateDriver } from '../../drivers/api/driversApi'
import { DriverProfilePanel } from '../../drivers/components/DriverProfilePanel'
import { IssuedItemsTab } from '../../issued-items/IssuedItemsTab'
import { LeaveBalanceTab } from '../../leave-balance/components/LeaveBalanceTab'
import { CreateUserAccountDialog } from '../components/CreateUserAccountDialog'
import { EmployeeDocumentsTab } from '../components/EmployeeDocumentsTab'
import { EmployeeForm } from '../components/EmployeeForm'
import { EmployeePlanningTab } from '../components/EmployeePlanningTab'
import { EmployeeTripsTab } from '../components/EmployeeTripsTab'
import { QualificationsTab } from '../components/QualificationsTab'
import { useEmployee } from '../hooks/useEmployee'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { CIVIL_STATUS_LABELS, EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES } from '../types/employee'
import './EmployeeDetailPage.css'

const TAB_IDS = ['profiel', 'planning', 'kwalificaties', 'documenten', 'verlof', 'ritten', 'bedrijfsmiddelen', 'historiek'] as const
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

  if (isLoading) return <LoadingState message="Medewerker laden..." />
  if (error || !employee) return <ErrorState message={error ?? 'Medewerker niet gevonden.'} />

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

  // Edit-only self-saving panels, embedded as sections of the profile form (they remain
  // reachable as page tabs too). `panel: true` hides the form's shared Save — each panel
  // saves through its own existing API.
  const editExtraSections: SectionDef[] = [
    {
      id: 'chauffeursgegevens',
      label: 'Chauffeursgegevens',
      optional: true,
      panel: true,
      render: () =>
        employee.driverId ? (
          <DriverProfilePanel driverId={employee.driverId} onChanged={reload} onDeleted={reload} />
        ) : hasPermission('drivers.create') && employee.isActive ? (
          <p className="placeholder-text">
            <Link to={`/drivers/new?employeeId=${employee.id}`}>Chauffeursprofiel aanmaken →</Link>
          </p>
        ) : (
          <p className="placeholder-text">Deze medewerker heeft geen chauffeursprofiel.</p>
        ),
    },
    {
      id: 'kwalificaties',
      label: 'Kwalificaties',
      optional: true,
      panel: true,
      render: () => <QualificationsTab employeeId={employee.id} />,
    },
    {
      id: 'documenten',
      label: 'Documenten',
      optional: true,
      panel: true,
      render: () =>
        canViewDocuments ? (
          <EmployeeDocumentsTab employeeId={employee.id} />
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om documenten te bekijken.</p>
        ),
    },
    {
      id: 'verlofsaldo',
      label: 'Verlofsaldo',
      optional: true,
      panel: true,
      render: () =>
        canViewLeaveBalance ? (
          <LeaveBalanceTab employeeId={employee.id} />
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om het verlofsaldo te bekijken.</p>
        ),
    },
    {
      id: 'bedrijfsmiddelen',
      label: 'Bedrijfsmiddelen',
      optional: true,
      panel: true,
      render: () =>
        canViewIssuedItems ? (
          <IssuedItemsTab employeeId={employee.id} employeeName={`${employee.firstName} ${employee.lastName}`} />
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om bedrijfsmiddelen te bekijken.</p>
        ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: `${employee.firstName} ${employee.lastName}` }]} />
      <PageHeader
        title={`${employee.firstName} ${employee.lastName}`}
        subtitle={`${employee.employeeNumber}${employee.functionNames.length > 0 ? ` · ${employee.functionNames.join(', ')}` : ''}`}
        action={
          <>
            {hasPermission('users.create') && (
              <Button variant="secondary" onClick={() => setShowAccountDialog(true)} disabled={mutations.isSubmitting}>
                Account aanmaken
              </Button>
            )}
            {canDeactivate && (
              <Button
                variant={employee.isActive ? 'danger' : 'secondary'}
                onClick={() => setConfirmLifecycle(employee.isActive ? 'deactivate' : 'reactivate')}
                disabled={mutations.isSubmitting}
              >
                {employee.isActive ? 'Deactiveren' : 'Heractiveren'}
              </Button>
            )}
          </>
        }
      />

      <div className="employee-detail-status">
        <StatusBadges
          active={employee.isActive}
          operational={{
            label: EMPLOYMENT_STATUS_LABELS[employee.employmentStatus],
            tone: EMPLOYMENT_STATUS_TONES[employee.employmentStatus],
          }}
        />
        {employee.driverId ? (
          <button type="button" className="employee-driver-link employee-driver-link-button" onClick={goToDriverSection}>
            Chauffeursgegevens bekijken →
          </button>
        ) : (
          hasPermission('drivers.create') &&
          employee.isActive && (
            <Link to={`/drivers/new?employeeId=${employee.id}`} className="employee-driver-link">
              Chauffeursprofiel aanmaken →
            </Link>
          )
        )}
      </div>

      <Tabs
        tabs={[
          { id: 'profiel', label: 'Overzicht' },
          ...(canViewPlanning ? [{ id: 'planning', label: 'Planning' }] : []),
          { id: 'kwalificaties', label: 'Kwalificaties' },
          ...(canViewDocuments ? [{ id: 'documenten', label: 'Documenten' }] : []),
          ...(canViewLeaveBalance || canViewAbsences ? [{ id: 'verlof', label: 'Verlof & afwezigheden' }] : []),
          ...(employee.driverId && canViewTrips ? [{ id: 'ritten', label: 'Ritten' }] : []),
          ...(canViewIssuedItems ? [{ id: 'bedrijfsmiddelen', label: 'Bedrijfsmiddelen' }] : []),
          { id: 'historiek', label: 'Historiek' },
        ]}
        activeId={tab}
        onChange={setTab}
      />

      {tab === 'profiel' && (
        <TabPanel tabId="profiel">
          {!canEdit && hasPermission('employee_notes.view') && (
            <section className="employee-notes-card">
              <h3>Notities</h3>
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
                  toast.showSuccess('Medewerker bijgewerkt.')
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
              <p className="placeholder-text">Je hebt alleen leesrechten voor dit profiel.</p>
              <dl className="employee-readonly-grid">
                <div>
                  <dt>Burgerlijke staat</dt>
                  <dd>{employee.civilStatus ? CIVIL_STATUS_LABELS[employee.civilStatus] : '—'}</dd>
                </div>
                <div>
                  <dt>Aantal kinderen ten laste</dt>
                  <dd>{employee.dependentChildren ?? '—'}</dd>
                </div>
                <div>
                  <dt>DIMONA-nummer</dt>
                  <dd>{employee.dimonaNumber ?? '—'}</dd>
                </div>
                <div>
                  <dt>Einddatum tewerkstelling</dt>
                  <dd>{employee.employmentEndDate ?? '—'}</dd>
                </div>
              </dl>
              <h3 className="employee-readonly-subtitle">Noodcontacten</h3>
              {employee.emergencyContacts.length === 0 ? (
                <p className="placeholder-text">Geen noodcontacten geregistreerd.</p>
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
                Chauffeursgegevens
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
            <p className="placeholder-text">Je hebt geen rechten om het verlofsaldo te bekijken.</p>
          )}
          {canViewAbsences ? (
            <AbsencesTab employeeId={employee.id} highlightAbsenceId={searchParams.get('absenceId')} />
          ) : (
            <p className="placeholder-text">Je hebt geen rechten om afwezigheden te bekijken.</p>
          )}
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
        </TabPanel>
      )}

      {tab === 'historiek' && (
        <TabPanel tabId="historiek">
          <EmployeeHistoryPanel employeeId={employee.id} />
        </TabPanel>
      )}

      {offerDriverDeactivation && employee.driverId && (
        <ConfirmDialog
          title="Chauffeursprofiel deactiveren?"
          message="De chauffeursfuncties zijn verwijderd. Wil je het gekoppelde chauffeursprofiel deactiveren? De historiek en kwalificaties blijven bewaard."
          confirmLabel="Profiel deactiveren"
          cancelLabel="Profiel actief laten"
          busy={driverBusy}
          onConfirm={async () => {
            setDriverBusy(true)
            try {
              const driver = await getDriver(employee.driverId!)
              await updateDriver(employee.driverId!, {
                driverCategoryId: driver.categoryId,
                availabilityStatus: driver.availabilityStatus,
                isActive: false,
                fixedTrailerId: driver.fixedTrailerId,
                notes: driver.notes,
              })
              toast.showSuccess('Chauffeursprofiel gedeactiveerd.')
            } catch {
              toast.showError('Chauffeursprofiel kon niet worden gedeactiveerd.')
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
          title="Medewerker deactiveren"
          message={`${employee.firstName} ${employee.lastName} deactiveren? Het dienstverband wordt op beëindigd gezet; historiek blijft bewaard.`}
          confirmLabel="Deactiveren"
          destructive
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.deactivate(employee.id)
            if (ok) {
              toast.showSuccess('Medewerker gedeactiveerd.')
              setConfirmLifecycle(null)
              reload()
            }
          }}
          onCancel={() => setConfirmLifecycle(null)}
        />
      )}

      {confirmLifecycle === 'reactivate' && (
        <ConfirmDialog
          title="Medewerker heractiveren"
          message={`${employee.firstName} ${employee.lastName} opnieuw activeren?`}
          confirmLabel="Heractiveren"
          busy={mutations.isSubmitting}
          onConfirm={async () => {
            const ok = await mutations.reactivate(employee.id)
            if (ok) {
              toast.showSuccess('Medewerker geheractiveerd.')
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
            if (created) toast.showSuccess('Gebruikersaccount aangemaakt.')
          }}
        />
      )}
    </div>
  )
}
