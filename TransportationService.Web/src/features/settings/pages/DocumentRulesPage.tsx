import { useEffect, useState } from 'react'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { listActivityTypes, type ActivityType } from '../../dossiers/api/activityTypesApi'
import {
  DOCUMENT_RULE_KIND_KEYS,
  listDocumentRules,
  saveDocumentRules,
  type DocumentRuleInput,
  type DocumentRuleKind,
} from '../api/documentRulesApi'
import './settings.css'

/** Bewerkbare rij; tri-state selects bewaren '' = "maakt niet uit" (null in de payload). */
interface RuleRow {
  key: string
  priority: string
  crossBorder: '' | 'ja' | 'nee'
  adr: '' | 'ja' | 'nee'
  activityTypeId: string
  documentKind: DocumentRuleKind
}

let rowCounter = 0
function newRowKey(): string {
  rowCounter += 1
  return `rule-${rowCounter}`
}

function triState(value: boolean | null): '' | 'ja' | 'nee' {
  if (value === null) return ''
  return value ? 'ja' : 'nee'
}

function fromTriState(value: '' | 'ja' | 'nee'): boolean | null {
  if (value === '') return null
  return value === 'ja'
}

/**
 * Parameters → Beheer → Documentregels: tenant-regels die bepalen welk transportdocument
 * (leveringsbon/CMR/geen) een opdracht standaard krijgt. PUT vervangt de volledige set; de
 * eerste passende regel (laagste prioriteit) wint, zonder regels gelden de standaardregels.
 */
export function DocumentRulesPage() {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('company_settings.manage')
  const canView = hasPermission('company_settings.view') || canManage

  const [rows, setRows] = useState<RuleRow[] | null>(null)
  const [activityTypes, setActivityTypes] = useState<ActivityType[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!canView) return
    let mounted = true
    listDocumentRules()
      .then((data) => {
        if (!mounted) return
        setRows(
          data.map((rule) => ({
            key: newRowKey(),
            priority: String(rule.priority),
            crossBorder: triState(rule.matchCrossBorder),
            adr: triState(rule.matchAdr),
            activityTypeId: rule.matchActivityTypeId ?? '',
            documentKind: rule.documentKind,
          })),
        )
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('settingsPages.documentRules.loadFailed'))
      })
    listActivityTypes()
      .then((data) => {
        if (mounted) setActivityTypes(data)
      })
      .catch(() => {
        /* zonder types blijft alleen de optie "Alle" beschikbaar */
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canView])

  if (!canView) return <p className="placeholder-text">{t('settingsPages.documentRules.noPermission')}</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (rows === null) return <p className="placeholder-text">{t('settingsPages.documentRules.loading')}</p>

  function patchRow(key: string, patch: Partial<Omit<RuleRow, 'key'>>) {
    setRows((current) => (current ? current.map((row) => (row.key === key ? { ...row, ...patch } : row)) : current))
  }

  function addRow() {
    setRows((current) => {
      const next = current ?? []
      const maxPriority = next.reduce((max, row) => Math.max(max, Number(row.priority) || 0), 0)
      return [
        ...next,
        { key: newRowKey(), priority: String(maxPriority + 10), crossBorder: '', adr: '', activityTypeId: '', documentKind: 'DeliveryNote' },
      ]
    })
  }

  async function handleSave() {
    if (!rows) return
    const inputs: DocumentRuleInput[] = []
    for (const row of rows) {
      const priority = Number(row.priority)
      if (!Number.isInteger(priority)) {
        showError(t('settingsPages.documentRules.priorityInteger'))
        return
      }
      inputs.push({
        priority,
        matchCrossBorder: fromTriState(row.crossBorder),
        matchAdr: fromTriState(row.adr),
        matchActivityTypeId: row.activityTypeId || null,
        documentKind: row.documentKind,
      })
    }
    setBusy(true)
    try {
      await saveDocumentRules(inputs)
      showSuccess(t('settingsPages.documentRules.saved'))
      const reloaded = await listDocumentRules()
      setRows(
        reloaded.map((rule) => ({
          key: newRowKey(),
          priority: String(rule.priority),
          crossBorder: triState(rule.matchCrossBorder),
          adr: triState(rule.matchAdr),
          activityTypeId: rule.matchActivityTypeId ?? '',
          documentKind: rule.documentKind,
        })),
      )
    } catch (err) {
      showError(describeApiError(err, t('settingsPages.documentRules.saveFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs
        items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('navigation.menu.documentRules') }]}
      />
      <PageHeader
        title={t('settingsPages.documentRules.title')}
        subtitle={t('settingsPages.documentRules.subtitle')}
        action={
          canManage && (
            <span className="settings-actions">
              <Button variant="secondary" onClick={addRow} disabled={busy}>
                {t('settingsPages.documentRules.add')}
              </Button>
              <Button onClick={() => void handleSave()} disabled={busy}>
                {busy ? t('settingsPages.common.saving') : t('ui.actions.save')}
              </Button>
            </span>
          )
        }
      />

      <p className="customer-form-muted">{t('settingsPages.documentRules.explanation')}</p>

      {rows.length === 0 ? (
        <p className="placeholder-text">{t('settingsPages.documentRules.empty')}</p>
      ) : (
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('settingsPages.documentRules.columnPriority')}</th>
              <th>{t('settingsPages.documentRules.columnCrossBorder')}</th>
              <th>{t('settingsPages.documentRules.columnAdr')}</th>
              <th>{t('settingsPages.documentRules.columnActivityType')}</th>
              <th>{t('settingsPages.documentRules.columnDocument')}</th>
              {canManage && <th aria-label={t('settingsPages.common.actions')} />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={row.key}>
                <td>
                  <input
                    type="number"
                    className="settings-doc-rules-priority"
                    aria-label={t('settingsPages.documentRules.priorityAria', { index: index + 1 })}
                    value={row.priority}
                    onChange={(e) => patchRow(row.key, { priority: e.target.value })}
                    disabled={!canManage}
                  />
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.documentRules.crossBorderAria', { index: index + 1 })}
                    value={row.crossBorder}
                    onChange={(e) => patchRow(row.key, { crossBorder: e.target.value as RuleRow['crossBorder'] })}
                    disabled={!canManage}
                  >
                    <option value="">{t('settingsPages.documentRules.any')}</option>
                    <option value="ja">{t('settingsPages.documentRules.yes')}</option>
                    <option value="nee">{t('settingsPages.documentRules.no')}</option>
                  </select>
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.documentRules.adrAria', { index: index + 1 })}
                    value={row.adr}
                    onChange={(e) => patchRow(row.key, { adr: e.target.value as RuleRow['adr'] })}
                    disabled={!canManage}
                  >
                    <option value="">{t('settingsPages.documentRules.any')}</option>
                    <option value="ja">{t('settingsPages.documentRules.yes')}</option>
                    <option value="nee">{t('settingsPages.documentRules.no')}</option>
                  </select>
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.documentRules.activityTypeAria', { index: index + 1 })}
                    value={row.activityTypeId}
                    onChange={(e) => patchRow(row.key, { activityTypeId: e.target.value })}
                    disabled={!canManage}
                  >
                    <option value="">{t('settingsPages.documentRules.all')}</option>
                    {activityTypes.map((type) => (
                      <option key={type.id} value={type.id}>
                        {type.name}
                      </option>
                    ))}
                    {row.activityTypeId && !activityTypes.some((type) => type.id === row.activityTypeId) && (
                      <option value={row.activityTypeId}>{t('settingsPages.documentRules.unknownActivityType')}</option>
                    )}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={t('settingsPages.documentRules.documentAria', { index: index + 1 })}
                    value={row.documentKind}
                    onChange={(e) => patchRow(row.key, { documentKind: e.target.value as DocumentRuleKind })}
                    disabled={!canManage}
                  >
                    {(Object.entries(DOCUMENT_RULE_KIND_KEYS) as [DocumentRuleKind, string][]).map(
                      ([value, labelKey]) => (
                        <option key={value} value={value}>
                          {t(labelKey)}
                        </option>
                      ),
                    )}
                  </select>
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link issued-items-link-danger"
                      onClick={() => setRows((current) => (current ? current.filter((r) => r.key !== row.key) : current))}
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
    </div>
  )
}
