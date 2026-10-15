import type { ReactNode } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DetailCard, DetailGrid } from '../../../components/ui/DetailCard'
import { FactList } from '../../../components/ui/FactList'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { contractEndBadge } from '../utils/employeeListBadges'
import { fullYearsSince } from '../utils/fullYearsSince'
import { CIVIL_STATUS_LABELS, EMPLOYMENT_STATUS_LABELS, EMPLOYMENT_STATUS_TONES, type EmployeeDetail } from '../types/employee'
import type { EmployeeFormBlock } from './EmployeeForm'
import { EmployeeNotesPanel } from './EmployeeNotesPanel'

/** Card ids double as `#anchors` for deep links (`?section=noodcontacten`) and attention jumps. */
export type EmployeeOverviewCard = EmployeeFormBlock | 'chauffeur' | 'notities'

interface EmployeeOverviewProps {
  employee: EmployeeDetail
  /** The block currently in edit mode (one card at a time), or null when reading. */
  editing: EmployeeFormBlock | null
  /** Renders the edit form for the card in edit mode. */
  editor: ReactNode
  /** Opens a card's edit mode; omitted for viewers without `employees.edit`. */
  onEdit?: (block: EmployeeFormBlock) => void
  /** Inline "✓ Opgeslagen · 14:32" of the last saved card, shown in that card's title row. */
  savedBlock: EmployeeFormBlock | null
  saveStatus: ReactNode
  canSeeConfidential: boolean
  canViewNotes: boolean
  /** Opens the Chauffeur section; only when there is a driver profile. */
  onOpenDriver?: () => void
  /** Completeness summary rendered above the cards. */
  completeness?: ReactNode
}

function regionName(code: string | null, locale: string): string | null {
  if (!code) return null
  try {
    return new Intl.DisplayNames([locale], { type: 'region' }).of(code.toUpperCase()) ?? code
  } catch {
    return code
  }
}

function languageName(code: string | null, locale: string): string | null {
  if (!code) return null
  try {
    return new Intl.DisplayNames([locale], { type: 'language' }).of(code.toLowerCase()) ?? code
  } catch {
    return code
  }
}

/**
 * B15: the read-first employee overview. Every logical block is a card of compact facts with a
 * quiet [Bewerken] that turns only THAT card into a form (same fields, validation and save API
 * as before) — the page is no longer a permanently open 10-section form.
 */
export function EmployeeOverview({
  employee,
  editing,
  editor,
  onEdit,
  savedBlock,
  saveStatus,
  canSeeConfidential,
  canViewNotes,
  onOpenDriver,
  completeness,
}: EmployeeOverviewProps) {
  const { t, locale } = useLocale()

  function editAction(block: EmployeeFormBlock) {
    if (!onEdit || editing === block) return undefined
    return (
      <Button variant="ghost" className="employee-card-edit" onClick={() => onEdit(block)} disabled={editing !== null}>
        {t('ui.actions.edit')}
      </Button>
    )
  }

  function card(block: EmployeeFormBlock, title: string, body: ReactNode, wide = false) {
    return (
      <DetailCard
        key={block}
        id={`employee-card-${block}`}
        title={title}
        className={wide || editing === block ? 'ui-detail-card-wide' : undefined}
        aside={savedBlock === block && editing !== block ? saveStatus : undefined}
        action={editAction(block)}
      >
        {editing === block ? editor : body}
      </DetailCard>
    )
  }

  const age = fullYearsSince(employee.dateOfBirth)
  const years = fullYearsSince(employee.employmentStartDate)
  const endBadge = contractEndBadge(employee)
  const address = [
    [employee.street, employee.houseNumber].filter(Boolean).join(' '),
    [employee.postalCode, employee.city].filter(Boolean).join(' '),
    employee.countryCode && employee.countryCode.toUpperCase() !== 'BE' ? regionName(employee.countryCode, locale) : null,
  ]
    .filter(Boolean)
    .join(', ')

  const contacts = [...employee.emergencyContacts].sort((a, b) => a.priority - b.priority)

  return (
    <>
      {completeness}
      <DetailGrid className="employee-overview">
        {card(
          'personal',
          t('employees.overview.personal'),
          <FactList
            facts={[
              {
                label: t('employees.detail.dateOfBirth'),
                value: employee.dateOfBirth
                  ? `${formatDate(employee.dateOfBirth)}${age !== null ? ` ${t('employees.detail.ageSuffix', { age })}` : ''}`
                  : null,
              },
              { label: t('employees.form.placeOfBirth'), value: employee.placeOfBirth },
              { label: t('employees.form.nationality'), value: regionName(employee.nationalityCode, locale) },
              { label: t('employees.form.preferredLanguage'), value: languageName(employee.preferredLanguageCode, locale) },
              { label: t('employees.detail.civilStatus'), value: employee.civilStatus ? t(CIVIL_STATUS_LABELS[employee.civilStatus]) : null },
              { label: t('employees.detail.dependentChildren'), value: employee.dependentChildren ?? null },
            ]}
          />,
        )}
        {card(
          'contact',
          t('employees.overview.contact'),
          <FactList
            facts={[
              { label: t('employees.detail.email'), value: employee.email ? <a href={`mailto:${employee.email}`}>{employee.email}</a> : null },
              { label: t('employees.detail.phone'), value: employee.phoneNumber ? <a href={`tel:${employee.phoneNumber}`}>{employee.phoneNumber}</a> : null },
              { label: t('employees.detail.mobile'), value: employee.mobilePhone ? <a href={`tel:${employee.mobilePhone}`}>{employee.mobilePhone}</a> : null },
              { label: t('employees.overview.address'), value: address || null },
            ]}
          />,
        )}
        {card(
          'dienstverband',
          t('employees.sections.dienstverband'),
          <FactList
            facts={[
              {
                label: t('employees.fields.employmentStatus'),
                value: (
                  <Badge tone={EMPLOYMENT_STATUS_TONES[employee.employmentStatus]}>
                    {t(EMPLOYMENT_STATUS_LABELS[employee.employmentStatus])}
                  </Badge>
                ),
              },
              {
                label: t('employees.fields.jobFunctionIds'),
                value: employee.functionNames.length > 0 ? employee.functionNames.join(', ') : null,
              },
              {
                label: t('employees.fields.employmentStartDate'),
                value: employee.employmentStartDate
                  ? `${formatDate(employee.employmentStartDate)}${years !== null && years >= 1 ? ` · ${t('employees.detail.yearsOfService', { count: years })}` : ''}`
                  : null,
              },
              {
                label: t('employees.detail.employmentEndDate'),
                value: employee.employmentEndDate ? (
                  <span className="employee-overview-inline">
                    {formatDate(employee.employmentEndDate)}
                    {endBadge && <Badge tone={endBadge.tone}>{t(endBadge.key, endBadge.params)}</Badge>}
                  </span>
                ) : null,
              },
              { label: t('employees.fields.contractTypeId'), value: employee.contractTypeName },
              { label: t('employees.fields.departmentId'), value: employee.departmentName },
              { label: t('employees.detail.dimonaNumber'), value: employee.dimonaNumber },
            ]}
          />,
        )}
        {canSeeConfidential &&
          card(
            'hr',
            t('employees.sections.hr'),
            <FactList
              facts={[
                { label: t('employees.fields.nationalRegisterNumber'), value: employee.nationalRegisterNumber },
                { label: t('employees.fields.identityCardNumber'), value: employee.identityCardNumber },
                { label: t('employees.fields.iban'), value: employee.iban },
                { label: t('employees.fields.bic'), value: employee.bic },
              ]}
            />,
          )}
        {card(
          'noodcontacten',
          t('employees.detail.emergencyContactsHeading'),
          contacts.length === 0 ? (
            <p className="ui-detail-empty">{t('employees.detail.noEmergencyContacts')}</p>
          ) : (
            <ul className="ui-detail-list">
              {contacts.map((contact) => (
                <li key={contact.id}>
                  <span className="ui-detail-list-main">
                    <strong>{contact.name}</strong>
                    {contact.relationship && <span className="employee-overview-muted"> · {contact.relationship}</span>}
                  </span>
                  {(contact.phone || contact.mobilePhone) && (
                    <span className="ui-detail-list-meta">{[contact.phone, contact.mobilePhone].filter(Boolean).join(' / ')}</span>
                  )}
                </li>
              ))}
            </ul>
          ),
        )}
        {employee.driverId && (
          <DetailCard
            id="employee-card-chauffeur"
            title={t('employees.overview.driver')}
            action={
              onOpenDriver && (
                <Button variant="ghost" className="employee-card-edit" onClick={onOpenDriver}>
                  {t('employees.overview.openDriver')}
                </Button>
              )
            }
          >
            <p className="ui-detail-empty">{t('employees.overview.driverProfileActive')}</p>
          </DetailCard>
        )}
        {canViewNotes && (
          <DetailCard id="employee-card-notities" title={t('employees.detail.notesHeading')} className="ui-detail-card-wide">
            <EmployeeNotesPanel employeeId={employee.id} />
          </DetailCard>
        )}
      </DetailGrid>
    </>
  )
}
