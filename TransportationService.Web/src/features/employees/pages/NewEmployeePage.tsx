import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { FormSection } from '../../../components/ui/FormSection'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { EmployeeForm } from '../components/EmployeeForm'
import { useEmployeeMutations } from '../hooks/useEmployeeMutations'
import { useQualificationTypes } from '../hooks/useQualificationTypes'
import type { CreateEmployeeQualificationInput } from '../types/employee'

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
  const { qualificationTypes } = useQualificationTypes()

  const [isDriver, setIsDriver] = useState(false)
  const [driverSuggested, setDriverSuggested] = useState(false)
  const [driverCategoryIds, setDriverCategoryIds] = useState<string[]>([])
  const [driverNotes, setDriverNotes] = useState('')
  const driverCategories = useLookupOptions('/api/driver-categories', { enabled: canCreateDriver })
  const [qualificationRows, setQualificationRows] = useState<QualificationRow[]>([])

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

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel', to: '/employees' }, { label: 'Nieuwe medewerker' }]} />
      <PageHeader title="Nieuwe medewerker" />
      <EmployeeForm
        mode="create"
        isSubmitting={mutations.isSubmitting}
        submitError={mutations.error}
        serverFieldErrors={mutations.fieldErrors}
        onCancel={() => navigate('/employees')}
        onFunctionsChanged={handleFunctionsChanged}
        extraSections={
          <>
            {canCreateDriver && (
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
            )}

            {canAddQualifications && (
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
            )}
          </>
        }
        onSubmit={async (values) => {
          const created = await mutations.create({
            ...values,
            driverProfile: isDriver ? { driverCategoryIds, notes: driverNotes.trim() || null } : null,
            qualifications: buildQualifications(),
          })
          if (created) {
            if (created.driverId) {
              toast.showSuccess(`Medewerker ${created.employeeNumber} en chauffeursprofiel aangemaakt.`)
              navigate(`/employees/${created.id}?tab=kwalificaties`)
            } else {
              toast.showSuccess(`Medewerker ${created.employeeNumber} aangemaakt.`)
              navigate(`/employees/${created.id}`)
            }
          }
        }}
      />
    </div>
  )
}
