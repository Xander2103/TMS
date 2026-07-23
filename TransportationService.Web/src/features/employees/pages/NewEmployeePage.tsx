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
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { EmployeeForm } from '../components/EmployeeForm'
import { PreparedDocumentsEditor } from '../components/PreparedDocumentsEditor'
import { PreparedIssuedItemsEditor } from '../components/PreparedIssuedItemsEditor'
import { CreateFollowUpDialog } from '../components/CreateFollowUpDialog'
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
  const navigate = useNavigate()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const mutations = useEmployeeMutations()

  const canCreateDriver = hasPermission('drivers.create')
  const canAddQualifications = hasPermission('employee_documents.create')
  const canCreateDocuments = hasPermission('employee_documents.create')
  const canManageIssuedItems = hasPermission('issued_items.manage')
  const { qualificationTypes } = useQualificationTypes()

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
      id: 'chauffeursprofiel',
      label: 'Chauffeursprofiel',
      optional: true,
      render: () =>
        canCreateDriver ? (
          <FormSection
            title="Chauffeursprofiel"
            columns={2}
            description="Een chauffeursprofiel wordt in dezelfde stap aangemaakt — persoonsgegevens worden nooit dubbel ingevoerd."
          >
            <div className="form-span-all">
              <label className="customer-form-checkbox">
                <input type="checkbox" checked={isDriver} onChange={(e) => setIsDriver(e.target.checked)} />
                Deze medewerker is chauffeur
              </label>
            </div>
            {isDriver && (
              <>
                <FormField label="Chauffeurcategorieën" htmlFor="ne-driver-categories" hint="Eén of meer categorieën aanvinken.">
                  <div id="ne-driver-categories" className="ne-driver-categories">
                    {driverCategories.isLoading && <span className="ne-driver-categories-empty">Categorieën laden…</span>}
                    {!driverCategories.isLoading && driverCategories.options.length === 0 && (
                      <span className="ne-driver-categories-empty">Geen categorieën beschikbaar.</span>
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
                <FormField label="Chauffeursnotities" htmlFor="ne-driver-notes">
                  <textarea id="ne-driver-notes" rows={2} value={driverNotes} onChange={(e) => setDriverNotes(e.target.value)} maxLength={2000} />
                </FormField>
              </>
            )}
          </FormSection>
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om hier een chauffeursprofiel aan te maken.</p>
        ),
    },
    {
      id: 'kwalificaties',
      label: 'Kwalificaties',
      optional: true,
      render: () =>
        canAddQualifications ? (
          <FormSection
            title="Kwalificaties (optioneel)"
            columns={1}
            collapsible
            defaultOpen={qualificationRows.length > 0}
            description="Rijbewijs, medische schifting, ADR, … — worden samen met de medewerker aangemaakt en zijn later beheerbaar op de detailpagina."
          >
            <div className="form-span-all">
              {qualificationRows.map((row, index) => (
                <div key={row.key} className="ne-qualification-row">
                  <FormField label={`Type ${index + 1}`} htmlFor={`ne-qual-type-${row.key}`}>
                    <SearchableSelect
                      id={`ne-qual-type-${row.key}`}
                      value={row.qualificationTypeId}
                      onChange={(v) => setRow(row.key, { qualificationTypeId: v })}
                      options={qualificationTypes.map((type) => ({ value: type.id, label: type.name, keywords: type.code }))}
                      placeholder="— Selecteer type —"
                    />
                  </FormField>
                  <FormField label="Documentnummer" htmlFor={`ne-qual-doc-${row.key}`}>
                    <input
                      id={`ne-qual-doc-${row.key}`}
                      value={row.documentNumber}
                      onChange={(e) => setRow(row.key, { documentNumber: e.target.value })}
                      maxLength={100}
                    />
                  </FormField>
                  <FormField label="Behaald op" htmlFor={`ne-qual-obtained-${row.key}`}>
                    <input
                      id={`ne-qual-obtained-${row.key}`}
                      type="date"
                      value={row.obtainedDate}
                      onChange={(e) => setRow(row.key, { obtainedDate: e.target.value })}
                    />
                  </FormField>
                  <FormField label="Vervalt op" htmlFor={`ne-qual-expiry-${row.key}`} hint="Leeg = vervalt niet.">
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
                    Verwijderen
                  </Button>
                </div>
              ))}
              <Button variant="secondary" onClick={addQualificationRow}>
                + Kwalificatie toevoegen
              </Button>
            </div>
          </FormSection>
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om kwalificaties toe te voegen.</p>
        ),
    },
    {
      id: 'documenten',
      label: 'Documenten',
      optional: true,
      render: () =>
        canCreateDocuments ? (
          <FormSection title="Documenten" columns={1}>
            <div className="form-span-all">
              <PreparedDocumentsEditor value={preparedDocs} onChange={setPreparedDocs} />
            </div>
          </FormSection>
        ) : (
          <p className="placeholder-text">Je hebt geen rechten om documenten toe te voegen.</p>
        ),
    },
    {
      id: 'verlofsaldo',
      label: 'Verlofsaldo',
      optional: true,
      render: () => (
        <p className="placeholder-text">Het verlofsaldo (jaarrecht per saldotype) is beschikbaar na het opslaan van de medewerker.</p>
      ),
    },
    {
      id: 'bedrijfsmiddelen',
      label: 'Bedrijfsmiddelen',
      optional: true,
      render: () =>
        canManageIssuedItems ? (
          <FormSection title="Bedrijfsmiddelen" columns={1}>
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
          <p className="placeholder-text">Je hebt geen rechten om bedrijfsmiddelen toe te wijzen.</p>
        ),
    },
  ]

  function goToEmployee(emp: EmployeeDetail) {
    navigate(emp.driverId ? `/employees/${emp.id}?tab=kwalificaties` : `/employees/${emp.id}`)
  }

  // Runs after employee creation succeeds. On full success it navigates; on partial failure
  // it keeps the employee + prepared files and surfaces the retry dialog.
  async function processFollowUps(emp: EmployeeDetail) {
    if (preparedDocs.length === 0 && preparedItems.length === 0) {
      toast.showSuccess(
        emp.driverId
          ? `Medewerker ${emp.employeeNumber} en chauffeursprofiel aangemaakt.`
          : `Medewerker ${emp.employeeNumber} aangemaakt.`,
      )
      goToEmployee(emp)
      return
    }
    const results = await runEmployeeCreateFollowUps(emp.id, preparedDocs, preparedItems)
    if (results.every((r) => r.ok)) {
      toast.showSuccess(`Medewerker ${emp.employeeNumber} aangemaakt; ${results.length} bijlage(n) verwerkt.`)
      goToEmployee(emp)
    } else {
      toast.showError(`Medewerker aangemaakt, maar enkele bijlagen zijn mislukt.`)
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
        ...(await uploadPreparedDocuments(createdEmployee.id, preparedDocs.filter((d) => failedKeys.has(d.key)))),
        ...(await createPreparedIssuedItems(createdEmployee.id, preparedItems.filter((i) => failedKeys.has(i.key)))),
      ]
      const merged = followUpResults.map(
        (r) => retried.find((n) => n.kind === r.kind && n.key === r.key) ?? r,
      )
      if (merged.every((r) => r.ok)) {
        const emp = createdEmployee
        toast.showSuccess('Alle bijlagen verwerkt.')
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
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: 'Nieuwe medewerker' }]} />
      <PageHeader title="Nieuwe medewerker" />
      <EmployeeForm
        mode="create"
        isSubmitting={mutations.isSubmitting || followUpBusy}
        submitError={mutations.error}
        serverFieldErrors={mutations.fieldErrors}
        onCancel={() => navigate('/employees')}
        onFunctionsChanged={handleFunctionsChanged}
        extraSections={extraSections}
        onSubmit={async (values) => {
          const created = await mutations.create({
            ...values,
            driverProfile: isDriver ? { driverCategoryIds, notes: driverNotes.trim() || null } : null,
            qualifications: buildQualifications(),
          })
          // Creation failed → nothing uploaded, no issuance/stock changes, no orphans.
          if (created) await processFollowUps(created)
        }}
      />
      {createdEmployee && followUpResults && (
        <CreateFollowUpDialog
          employeeLabel={`Medewerker ${createdEmployee.employeeNumber}`}
          results={followUpResults}
          busy={followUpBusy}
          onRetry={retryFailedFollowUps}
          onClose={dismissFollowUp}
        />
      )}
    </div>
  )
}
