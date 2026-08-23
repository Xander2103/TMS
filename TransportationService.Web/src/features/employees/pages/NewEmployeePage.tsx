import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import type { SectionDef } from '../../../components/ui/SectionedForm'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { EmployeeForm } from '../components/EmployeeForm'
import { PreparedDocumentsEditor } from '../components/PreparedDocumentsEditor'
import { PreparedIssuedItemsEditor } from '../components/PreparedIssuedItemsEditor'
import { CreateFollowUpDialog } from '../components/CreateFollowUpDialog'
import { searchEmployees } from '../api/employeesApi'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { useQualificationTypes } from '../hooks/useQualificationTypes'
import { listIssuedItemTemplates, type IssuedItemTemplate } from '../../issued-items/issuedItemsApi'
import {
  runEmployeeCreateFollowUps,
  uploadPreparedDocuments,
  createPreparedIssuedItems,
  type FollowUpResult,
  type PreparedEmployeeDocument,
  type PreparedIssuedItem,
} from '../utils/preparedFollowUp'
import type { CreateEmployeeQualificationInput, EmployeeDetail } from '../types/employee'

interface QualificationRow {
  key: string
  qualificationTypeId: string | null
  documentNumber: string
  obtainedDate: string
  expiryDate: string
}

export function NewEmployeePage() {
  // "Opslaan en nieuwe werknemer" resets the entire create flow (form fields, driver profile,
  // prepared documents/items) by remounting the inner page with a fresh key.
  const [formInstance, setFormInstance] = useState(0)
  return <NewEmployeePageContent key={formInstance} onSavedAndNew={() => setFormInstance((k) => k + 1)} />
}

function NewEmployeePageContent({ onSavedAndNew }: { onSavedAndNew: () => void }) {
  const navigate = useNavigate()
  const toast = useToast()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const mutations = useEmployeeMutations()

  const canCreateDriver = hasPermission('drivers.create')
  const canAddQualifications = hasPermission('employee_documents.create')
  const canCreateDocuments = hasPermission('employee_documents.create')
  const canManageIssuedItems = hasPermission('issued_items.manage')
  const { qualificationTypes } = useQualificationTypes()

  // Non-blocking duplicate-name hint (task 10): debounced check against the existing employees
  // search API for an exact (trimmed, case-insensitive) first+last name match.
  const [nameCandidate, setNameCandidate] = useState({ firstName: '', lastName: '' })
  const [duplicateNameFound, setDuplicateNameFound] = useState(false)

  useEffect(() => {
    const firstName = nameCandidate.firstName.trim()
    const lastName = nameCandidate.lastName.trim()
    if (!firstName || !lastName) {
      setDuplicateNameFound(false)
      return
    }
    let isMounted = true
    const timeoutId = window.setTimeout(() => {
      searchEmployees({ search: lastName, page: 1, pageSize: 25 })
        .then((result) => {
          if (!isMounted) return
          const match = result.items.some(
            (item) =>
              item.firstName.trim().toLowerCase() === firstName.toLowerCase() &&
              item.lastName.trim().toLowerCase() === lastName.toLowerCase(),
          )
          setDuplicateNameFound(match)
        })
        .catch(() => {
          // Non-blocking hint — a failed lookup simply shows no warning.
        })
    }, 400)
    return () => {
      isMounted = false
      window.clearTimeout(timeoutId)
    }
  }, [nameCandidate])

  const [isDriver, setIsDriver] = useState(false)
  const [driverSuggested, setDriverSuggested] = useState(false)
  const [driverCategoryIds, setDriverCategoryIds] = useState<string[]>([])
  const [driverNotes, setDriverNotes] = useState('')
  const driverCategories = useLookupOptions('/api/driver-categories', { enabled: canCreateDriver })
  const [qualificationRows, setQualificationRows] = useState<QualificationRow[]>([])

  // Prepared follow-ups: held in form state until the employee exists, then persisted.
  const [preparedDocs, setPreparedDocs] = useState<PreparedEmployeeDocument[]>([])
  const [preparedItems, setPreparedItems] = useState<PreparedIssuedItem[]>([])
  const [itemTemplates, setItemTemplates] = useState<IssuedItemTemplate[]>([])
  const [templatesLoading, setTemplatesLoading] = useState(canManageIssuedItems)
  // After creation: the created employee + any follow-up results awaiting retry/dismissal.
  const [createdEmployee, setCreatedEmployee] = useState<EmployeeDetail | null>(null)
  const [followUpResults, setFollowUpResults] = useState<FollowUpResult[] | null>(null)
  const [followUpBusy, setFollowUpBusy] = useState(false)

  useEffect(() => {
    if (!canManageIssuedItems) return
    let mounted = true
    listIssuedItemTemplates()
      .then((data) => {
        if (mounted) setItemTemplates(data.filter((t) => t.isActive))
      })
      .catch(() => {
        /* templates optional; editor shows "none available" */
      })
      .finally(() => {
        if (mounted) setTemplatesLoading(false)
      })
    return () => {
      mounted = false
    }
  }, [canManageIssuedItems])

  // Suggest (once) enabling the driver profile when a driver-type function is chosen;
  // the user stays in control of the final choice.
  function handleFunctionsChanged(codes: string[]) {
    if (!canCreateDriver || driverSuggested) return
    if (codes.some((code) => code.toUpperCase().startsWith('CHAUF'))) {
      setIsDriver(true)
      setDriverSuggested(true)
    }
  }

  function addQualificationRow() {
    setQualificationRows((rows) => [
      ...rows,
      {
        key: crypto.randomUUID(),
        qualificationTypeId: null,
        documentNumber: '',
        obtainedDate: new Date().toISOString().slice(0, 10),
        expiryDate: '',
      },
    ])
  }

  function setRow(key: string, patch: Partial<QualificationRow>) {
    setQualificationRows((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function buildQualifications(): CreateEmployeeQualificationInput[] {
    // Rows without a chosen type are treated as empty and skipped.
    return qualificationRows
      .filter((row) => row.qualificationTypeId)
      .map((row) => ({
        qualificationTypeId: row.qualificationTypeId!,
        documentNumber: row.documentNumber.trim() || null,
        obtainedDate: row.obtainedDate,
        expiryDate: row.expiryDate || null,
        notes: null,
      }))
  }

  const extraSections: SectionDef[] = [
    {
      // Same id/label as the edit page so deep links and muscle memory carry over.
      id: 'chauffeursgegevens',
      label: t('employees.sections.chauffeursgegevens'),
      optional: true,
      render: () =>
        canCreateDriver ? (
          <FormSection
            title={t('employees.create.driverTitle')}
            columns={2}
            description={t('employees.create.driverDescription')}
          >
            <div className="form-span-all">
              <label className="customer-form-checkbox">
                <input type="checkbox" checked={isDriver} onChange={(e) => setIsDriver(e.target.checked)} />
                {t('employees.create.isDriver')}
              </label>
            </div>
            {isDriver && (
              <>
                <FormField label={t('employees.create.driverCategories')} htmlFor="ne-driver-categories" hint={t('employees.create.driverCategoriesHint')}>
                  <div id="ne-driver-categories" className="ne-driver-categories">
                    {driverCategories.isLoading && <span className="ne-driver-categories-empty">{t('employees.create.driverCategoriesLoading')}</span>}
                    {!driverCategories.isLoading && driverCategories.options.length === 0 && (
                      <span className="ne-driver-categories-empty">{t('employees.create.driverCategoriesEmpty')}</span>
                    )}
                    {driverCategories.options.map((category) => (
                      <label key={category.id} className="customer-form-checkbox">
                        <input
                          type="checkbox"
                          checked={driverCategoryIds.includes(category.id)}
                          onChange={(e) =>
                            setDriverCategoryIds((ids) =>
                              e.target.checked ? [...ids, category.id] : ids.filter((id) => id !== category.id),
                            )
                          }
                        />
                        {category.name}
                      </label>
                    ))}
                  </div>
                </FormField>
                <FormField label={t('employees.create.driverNotes')} htmlFor="ne-driver-notes">
                  <textarea id="ne-driver-notes" rows={2} value={driverNotes} onChange={(e) => setDriverNotes(e.target.value)} maxLength={2000} />
                </FormField>
              </>
            )}
          </FormSection>
        ) : (
          <p className="placeholder-text">{t('employees.create.noDriverCreatePermission')}</p>
        ),
    },
    {
      id: 'kwalificaties',
      label: t('employees.sections.kwalificaties'),
      optional: true,
      render: () =>
        canAddQualifications ? (
          <FormSection
            title={t('employees.create.qualificationsTitle')}
            columns={1}
            description={t('employees.create.qualificationsDescription')}
          >
            <div className="form-span-all">
              {qualificationRows.map((row, index) => (
                <div key={row.key} className="ne-qualification-row">
                  <FormField label={t('employees.create.qualificationType', { index: index + 1 })} htmlFor={`ne-qual-type-${row.key}`}>
                    <SearchableSelect
                      id={`ne-qual-type-${row.key}`}
                      value={row.qualificationTypeId}
                      onChange={(v) => setRow(row.key, { qualificationTypeId: v })}
                      options={qualificationTypes.map((type) => ({ value: type.id, label: type.name, keywords: type.code }))}
                      placeholder={t('employees.create.qualificationTypePlaceholder')}
                    />
                  </FormField>
                  <FormField label={t('employees.create.documentNumber')} htmlFor={`ne-qual-doc-${row.key}`}>
                    <input
                      id={`ne-qual-doc-${row.key}`}
                      value={row.documentNumber}
                      onChange={(e) => setRow(row.key, { documentNumber: e.target.value })}
                      maxLength={100}
                    />
                  </FormField>
                  <FormField label={t('employees.create.obtainedOn')} htmlFor={`ne-qual-obtained-${row.key}`}>
                    <input
                      id={`ne-qual-obtained-${row.key}`}
                      type="date"
                      value={row.obtainedDate}
                      onChange={(e) => setRow(row.key, { obtainedDate: e.target.value })}
                    />
                  </FormField>
                  <FormField label={t('employees.create.expiresOn')} htmlFor={`ne-qual-expiry-${row.key}`} hint={t('employees.create.expiresHint')}>
                    <input
                      id={`ne-qual-expiry-${row.key}`}
                      type="date"
                      value={row.expiryDate}
                      onChange={(e) => setRow(row.key, { expiryDate: e.target.value })}
                    />
                  </FormField>
                  <Button
                    variant="ghost"
                    onClick={() => setQualificationRows((rows) => rows.filter((r) => r.key !== row.key))}
                  >
                    {t('employees.form.remove')}
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={addQualificationRow}>
                {t('employees.create.addQualification')}
              </Button>
            </div>
          </FormSection>
        ) : (
          <p className="placeholder-text">{t('employees.create.noQualificationsPermission')}</p>
        ),
    },
    {
      id: 'documenten',
      label: t('employees.sections.documenten'),
      optional: true,
      render: () =>
        canCreateDocuments ? (
          <FormSection title={t('employees.create.documentsTitle')} columns={1}>
            <div className="form-span-all">
              <PreparedDocumentsEditor value={preparedDocs} onChange={setPreparedDocs} />
            </div>
          </FormSection>
        ) : (
          <p className="placeholder-text">{t('employees.create.noDocumentsPermission')}</p>
        ),
    },
    {
      id: 'verlofsaldo',
      label: t('employees.sections.verlofsaldo'),
      optional: true,
      render: () => (
        <p className="placeholder-text">{t('employees.create.leaveBalancePlaceholder')}</p>
      ),
    },
    {
      id: 'bedrijfsmiddelen',
      label: t('employees.sections.bedrijfsmiddelen'),
      optional: true,
      render: () =>
        canManageIssuedItems ? (
          <FormSection title={t('employees.create.issuedItemsTitle')} columns={1}>
            <div className="form-span-all">
              <PreparedIssuedItemsEditor
                value={preparedItems}
                onChange={setPreparedItems}
                templates={itemTemplates}
                isLoading={templatesLoading}
              />
            </div>
          </FormSection>
        ) : (
          <p className="placeholder-text">{t('employees.create.noIssuedItemsPermission')}</p>
        ),
    },
  ]

  function goToEmployee(emp: EmployeeDetail) {
    navigate(emp.driverId ? `/employees/${emp.id}?tab=kwalificaties` : `/employees/${emp.id}`)
  }

  // Runs after employee creation succeeds. On full success it navigates to the detail page —
  // or, for "Opslaan en nieuwe werknemer", resets the create page for the next entry. On
  // partial failure it keeps the employee + prepared files and surfaces the retry dialog.
  async function processFollowUps(emp: EmployeeDetail, startNextEntry: boolean) {
    const finish = startNextEntry ? onSavedAndNew : () => goToEmployee(emp)
    if (preparedDocs.length === 0 && preparedItems.length === 0) {
      toast.showSuccess(
        emp.driverId
          ? t('employees.create.createdWithDriver', { number: emp.employeeNumber })
          : t('employees.create.created', { number: emp.employeeNumber }),
      )
      finish()
      return
    }
    const results = await runEmployeeCreateFollowUps(emp.id, preparedDocs, preparedItems, t)
    if (results.every((r) => r.ok)) {
      toast.showSuccess(
        t('employees.create.createdWithAttachments', { number: emp.employeeNumber, count: results.length }),
      )
      finish()
    } else {
      toast.showError(t('employees.create.attachmentsPartlyFailed'))
      setCreatedEmployee(emp)
      setFollowUpResults(results)
    }
  }

  async function retryFailedFollowUps() {
    if (!createdEmployee || !followUpResults) return
    setFollowUpBusy(true)
    try {
      const failedKeys = new Set(followUpResults.filter((r) => !r.ok).map((r) => r.key))
      const retried = [
        ...(await uploadPreparedDocuments(createdEmployee.id, preparedDocs.filter((d) => failedKeys.has(d.key)), t)),
        ...(await createPreparedIssuedItems(createdEmployee.id, preparedItems.filter((i) => failedKeys.has(i.key)), t)),
      ]
      const merged = followUpResults.map(
        (r) => retried.find((n) => n.kind === r.kind && n.key === r.key) ?? r,
      )
      if (merged.every((r) => r.ok)) {
        const emp = createdEmployee
        toast.showSuccess(t('employees.create.allAttachmentsProcessed'))
        setFollowUpResults(null)
        setCreatedEmployee(null)
        goToEmployee(emp)
      } else {
        setFollowUpResults(merged)
      }
    } finally {
      setFollowUpBusy(false)
    }
  }

  function dismissFollowUp() {
    const emp = createdEmployee
    setFollowUpResults(null)
    setCreatedEmployee(null)
    if (emp) goToEmployee(emp)
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.modules.personeel'), to: '/employees' }, { label: t('employees.create.breadcrumb') }]} />
      <PageHeader title={t('employees.create.title')} />
      <EmployeeForm
        mode="create"
        isSubmitting={mutations.isSubmitting || followUpBusy}
        submitError={mutations.error}
        serverFieldErrors={mutations.fieldErrors}
        onCancel={() => navigate('/employees')}
        onFunctionsChanged={handleFunctionsChanged}
        onNameChanged={(firstName, lastName) => setNameCandidate({ firstName, lastName })}
        duplicateNameHint={
          duplicateNameFound ? <span role="status">{t('employees.create.duplicateNameHint')}</span> : undefined
        }
        extraSections={extraSections}
        onSubmit={async (values, intent) => {
          const created = await mutations.create({
            ...values,
            driverProfile: isDriver ? { driverCategoryIds, notes: driverNotes.trim() || null } : null,
            qualifications: buildQualifications(),
          })
          // Creation failed → nothing uploaded, no issuance/stock changes, no orphans.
          if (created) await processFollowUps(created, intent === 'saveAndNew')
        }}
      />
      {createdEmployee && followUpResults && (
        <CreateFollowUpDialog
          employeeLabel={t('employees.create.employeeLabel', { number: createdEmployee.employeeNumber })}
          results={followUpResults}
          busy={followUpBusy}
          onRetry={retryFailedFollowUps}
          onClose={dismissFollowUp}
        />
      )}
    </div>
  )
}
