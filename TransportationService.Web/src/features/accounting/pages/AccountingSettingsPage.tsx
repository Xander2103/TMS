import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { PageHeader } from '../../../components/layout/PageHeader'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { localizeApiError } from '../../../api/problemDetails'
import {
  createLedgerAccount,
  createSalesCategory,
  deleteLedgerAccount,
  listLedgerAccounts,
  listSalesCategories,
  updateLedgerAccount,
  updateSalesCategory,
  SYSTEM_ROLE_LABELS,
  type LedgerAccount,
  type SalesCategory,
  type SalesCategorySystemRole,
} from '../api/accountingApi'
import './accounting.css'

interface AccountDraft {
  account: LedgerAccount | null
  accountNumber: string
  name: string
  externalCode: string
  description: string
  isActive: boolean
}

interface CategoryDraft {
  category: SalesCategory | null
  code: string
  name: string
  systemRole: SalesCategorySystemRole
  isActive: boolean
  invoiceDescriptionNl: string
  defaultUnitCode: string
  vatCategoryOverride: string
}

/**
 * Bedrijfsinstellingen → Boekhouding (corrections wave §7): tenant-specific ledger accounts
 * and the sales-category → account mappings. The mapping here is live configuration only —
 * invoice lines snapshot the resolved account at finalization, so history never moves.
 */
export function AccountingSettingsPage() {
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('accounting.manage')
  const canView = hasPermission('accounting.view') || canManage

  const [accounts, setAccounts] = useState<LedgerAccount[] | null>(null)
  const [categories, setCategories] = useState<SalesCategory[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [accountDraft, setAccountDraft] = useState<AccountDraft | null>(null)
  const [categoryDraft, setCategoryDraft] = useState<CategoryDraft | null>(null)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<LedgerAccount | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    if (!canView) return
    Promise.all([listLedgerAccounts(true), listSalesCategories(true)])
      .then(([accountData, categoryData]) => {
        setAccounts(accountData)
        setCategories(categoryData)
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('accounting.page.loadFailed'))
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">{t('accounting.page.noViewPermission')}</p>
  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>
  if (accounts === null || categories === null) return <p className="placeholder-text">{t('accounting.page.loading')}</p>

  const activeAccounts = accounts.filter((a) => a.isActive)
  const unmapped = categories.filter((c) => c.isActive && c.ledgerAccountId === null)

  async function changeMapping(category: SalesCategory, ledgerAccountId: string | null) {
    try {
      await updateSalesCategory(category.id, {
        code: category.code,
        name: category.name,
        systemRole: category.systemRole,
        ledgerAccountId,
        isActive: category.isActive,
        sortOrder: category.sortOrder,
      })
      showSuccess(t('accounting.categories.mappingUpdated', { name: category.name }))
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('accounting.categories.mappingSaveFailed')))
    }
  }

  async function submitAccount(event: FormEvent) {
    event.preventDefault()
    if (!accountDraft) return
    setBusy(true)
    setDraftError(null)
    try {
      const input = {
        accountNumber: accountDraft.accountNumber.trim(),
        name: accountDraft.name.trim(),
        externalCode: accountDraft.externalCode.trim() || null,
        description: accountDraft.description.trim() || null,
        isActive: accountDraft.isActive,
      }
      if (accountDraft.account) {
        await updateLedgerAccount(accountDraft.account.id, input)
        showSuccess(t('accounting.accounts.updated'))
      } else {
        await createLedgerAccount(input)
        showSuccess(t('accounting.accounts.created'))
      }
      setAccountDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('accounting.accounts.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  async function submitCategory(event: FormEvent) {
    event.preventDefault()
    if (!categoryDraft) return
    setBusy(true)
    setDraftError(null)
    try {
      const input = {
        code: categoryDraft.code.trim(),
        name: categoryDraft.name.trim(),
        systemRole: categoryDraft.systemRole,
        ledgerAccountId: categoryDraft.category?.ledgerAccountId ?? null,
        isActive: categoryDraft.isActive,
        sortOrder: categoryDraft.category?.sortOrder ?? categories!.length,
        invoiceDescriptionNl: categoryDraft.invoiceDescriptionNl.trim() || null,
        defaultUnitCode: categoryDraft.defaultUnitCode.trim() || null,
        vatCategoryOverride: categoryDraft.vatCategoryOverride || null,
      }
      if (categoryDraft.category) {
        await updateSalesCategory(categoryDraft.category.id, input)
        showSuccess(t('accounting.categories.updated'))
      } else {
        await createSalesCategory(input)
        showSuccess(t('accounting.categories.created'))
      }
      setCategoryDraft(null)
      reload()
    } catch (err) {
      setDraftError(localizeApiError(t, err, t('accounting.categories.saveFailed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.settings'), to: '/settings' }, { label: t('navigation.menu.accounting') }]} />
      <PageHeader title={t('navigation.menu.accounting')} subtitle={t('accounting.page.subtitle')} />

      {unmapped.length > 0 && (
        <div className="accounting-warning" role="alert">
          {t('accounting.page.unmappedWarning', { names: unmapped.map((c) => `'${c.name}'`).join(', ') })}
        </div>
      )}

      <section className="accounting-card">
        <div className="accounting-card-head">
          <h3>{t('accounting.categories.title')}</h3>
          {canManage && (
            <Button variant="secondary" onClick={() => {
              setDraftError(null)
              setCategoryDraft({
                category: null, code: '', name: '', systemRole: 'None', isActive: true,
                invoiceDescriptionNl: '', defaultUnitCode: '', vatCategoryOverride: '',
              })
            }}>
              {t('accounting.categories.add')}
            </Button>
          )}
        </div>
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>{t('accounting.categories.colCategory')}</th>
              <th>{t('accounting.categories.colUsage')}</th>
              <th>{t('accounting.categories.colLedgerAccount')}</th>
              <th>{t('accounting.categories.colStatus')}</th>
              {canManage && <th aria-label={t('accounting.page.actionsColumn')} />}
            </tr>
          </thead>
          <tbody>
            {categories.map((category) => (
              <tr key={category.id}>
                <td>{category.name}</td>
                <td className="customer-form-muted">{t(SYSTEM_ROLE_LABELS[category.systemRole])}</td>
                <td>
                  {canManage ? (
                    <select
                      aria-label={t('accounting.categories.mappingSelectLabel', { name: category.name })}
                      value={category.ledgerAccountId ?? ''}
                      onChange={(e) => void changeMapping(category, e.target.value || null)}
                    >
                      <option value="">{t('accounting.categories.noAccountOption')}</option>
                      {activeAccounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.accountNumber} — {account.name}
                        </option>
                      ))}
                      {category.ledgerAccountId && !activeAccounts.some((a) => a.id === category.ledgerAccountId) && (
                        <option value={category.ledgerAccountId}>
                          {t('accounting.categories.inactiveAccountOption', {
                            number: category.ledgerAccountNumber ?? '',
                            name: category.ledgerAccountName ?? '',
                          })}
                        </option>
                      )}
                    </select>
                  ) : category.ledgerAccountNumber ? (
                    `${category.ledgerAccountNumber} — ${category.ledgerAccountName}`
                  ) : (
                    '—'
                  )}
                </td>
                <td>
                  {category.isActive ? (
                    category.ledgerAccountId ? (
                      <Badge tone="success">{t('accounting.categories.badgeMapped')}</Badge>
                    ) : (
                      <Badge tone="warning">{t('accounting.categories.badgeNoAccount')}</Badge>
                    )
                  ) : (
                    <Badge tone="neutral">{t('accounting.categories.badgeInactive')}</Badge>
                  )}
                </td>
                {canManage && (
                  <td className="issued-items-row-actions">
                    <button
                      type="button"
                      className="issued-items-link"
                      onClick={() => {
                        setDraftError(null)
                        setCategoryDraft({
                          category,
                          code: category.code,
                          name: category.name,
                          systemRole: category.systemRole,
                          isActive: category.isActive,
                          invoiceDescriptionNl: category.invoiceDescriptionNl ?? '',
                          defaultUnitCode: category.defaultUnitCode ?? '',
                          vatCategoryOverride: category.vatCategoryOverride ?? '',
                        })
                      }}
                    >
                      {t('ui.actions.edit')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="accounting-card">
        <div className="accounting-card-head">
          <h3>{t('accounting.accounts.title')}</h3>
          {canManage && (
            <Button variant="secondary" onClick={() => {
              setDraftError(null)
              setAccountDraft({ account: null, accountNumber: '', name: '', externalCode: '', description: '', isActive: true })
            }}>
              {t('accounting.accounts.add')}
            </Button>
          )}
        </div>
        {accounts.length === 0 && (
          <p className="placeholder-text">{t('accounting.accounts.empty')}</p>
        )}
        {accounts.length > 0 && (
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>{t('accounting.accounts.colNumber')}</th>
                <th>{t('accounting.accounts.colName')}</th>
                <th>{t('accounting.accounts.colExternalCode')}</th>
                <th>{t('accounting.accounts.colStatus')}</th>
                {canManage && <th aria-label={t('accounting.page.actionsColumn')} />}
              </tr>
            </thead>
            <tbody>
              {accounts.map((account) => (
                <tr key={account.id}>
                  <td>{account.accountNumber}</td>
                  <td>{account.name}</td>
                  <td>{account.externalCode ?? '—'}</td>
                  <td>
                    <Badge tone={account.isActive ? 'success' : 'neutral'}>
                      {account.isActive ? t('ui.statusBadges.active') : t('ui.statusBadges.inactive')}
                    </Badge>
                  </td>
                  {canManage && (
                    <td className="issued-items-row-actions">
                      <button
                        type="button"
                        className="issued-items-link"
                        onClick={() => {
                          setDraftError(null)
                          setAccountDraft({
                            account,
                            accountNumber: account.accountNumber,
                            name: account.name,
                            externalCode: account.externalCode ?? '',
                            description: account.description ?? '',
                            isActive: account.isActive,
                          })
                        }}
                      >
                        {t('ui.actions.edit')}
                      </button>
                      <button
                        type="button"
                        className="issued-items-link issued-items-link-danger"
                        onClick={() => setDeleteTarget(account)}
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
      </section>

      {accountDraft && (
        <Modal
          title={
            accountDraft.account
              ? t('accounting.accounts.editTitle', { number: accountDraft.account.accountNumber })
              : t('accounting.accounts.addTitle')
          }
          onClose={() => setAccountDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAccountDraft(null)} disabled={busy}>{t('ui.actions.cancel')}</Button>
              <Button type="submit" form="ledger-account-form" disabled={busy}>{t('ui.actions.save')}</Button>
            </>
          }
        >
          <form id="ledger-account-form" onSubmit={submitAccount} noValidate>
            {draftError && <div role="alert" className="issued-items-form-error">{draftError}</div>}
            <FormField label={t('accounting.accounts.numberLabel')} htmlFor="la-number" required hint={t('accounting.accounts.numberHint')}>
              <input id="la-number" value={accountDraft.accountNumber} maxLength={30}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, accountNumber: e.target.value } : d))} />
            </FormField>
            <FormField label={t('accounting.accounts.nameLabel')} htmlFor="la-name" required hint={t('accounting.accounts.nameHint')}>
              <input id="la-name" value={accountDraft.name} maxLength={200}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, name: e.target.value } : d))} />
            </FormField>
            <FormField label={t('accounting.accounts.externalCodeLabel')} htmlFor="la-external">
              <input id="la-external" value={accountDraft.externalCode} maxLength={50}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, externalCode: e.target.value } : d))} />
            </FormField>
            <FormField label={t('accounting.accounts.descriptionLabel')} htmlFor="la-description">
              <input id="la-description" value={accountDraft.description} maxLength={1000}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, description: e.target.value } : d))} />
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={accountDraft.isActive}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              {t('accounting.accounts.activeLabel')}
            </label>
          </form>
        </Modal>
      )}

      {categoryDraft && (
        <Modal
          title={
            categoryDraft.category
              ? t('accounting.categories.editTitle', { name: categoryDraft.category.name })
              : t('accounting.categories.addTitle')
          }
          onClose={() => setCategoryDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCategoryDraft(null)} disabled={busy}>{t('ui.actions.cancel')}</Button>
              <Button type="submit" form="sales-category-form" disabled={busy}>{t('ui.actions.save')}</Button>
            </>
          }
        >
          <form id="sales-category-form" onSubmit={submitCategory} noValidate>
            {draftError && <div role="alert" className="issued-items-form-error">{draftError}</div>}
            <FormField label={t('accounting.categories.codeLabel')} htmlFor="sc-code" required hint={t('accounting.categories.codeHint')}>
              <input id="sc-code" value={categoryDraft.code} maxLength={50}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, code: e.target.value } : d))} />
            </FormField>
            <FormField label={t('accounting.categories.nameLabel')} htmlFor="sc-name" required>
              <input id="sc-name" value={categoryDraft.name} maxLength={200}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, name: e.target.value } : d))} />
            </FormField>
            <FormField
              label={t('accounting.categories.usageLabel')}
              htmlFor="sc-role"
              hint={t('accounting.categories.usageHint')}
            >
              <select id="sc-role" value={categoryDraft.systemRole}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, systemRole: e.target.value as SalesCategorySystemRole } : d))}>
                {Object.entries(SYSTEM_ROLE_LABELS).map(([value, labelKey]) => (
                  <option key={value} value={value}>{t(labelKey)}</option>
                ))}
              </select>
            </FormField>
            <FormField label={t('accounting.categories.invoiceDescriptionLabel')} htmlFor="sc-invoice-desc" hint={t('accounting.categories.invoiceDescriptionHint')}>
              <input id="sc-invoice-desc" value={categoryDraft.invoiceDescriptionNl} maxLength={300}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, invoiceDescriptionNl: e.target.value } : d))} />
            </FormField>
            <FormField label={t('accounting.categories.defaultUnitLabel')} htmlFor="sc-unit" hint={t('accounting.categories.defaultUnitHint')}>
              <input id="sc-unit" value={categoryDraft.defaultUnitCode} maxLength={10}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, defaultUnitCode: e.target.value } : d))} />
            </FormField>
            <FormField
              label={t('accounting.categories.vatOverrideLabel')}
              htmlFor="sc-vat"
              hint={t('accounting.categories.vatOverrideHint')}
            >
              <select id="sc-vat" value={categoryDraft.vatCategoryOverride}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, vatCategoryOverride: e.target.value } : d))}>
                <option value="">{t('accounting.vatCategory.none')}</option>
                <option value="S">{t('accounting.vatCategory.S')}</option>
                <option value="Z">{t('accounting.vatCategory.Z')}</option>
                <option value="E">{t('accounting.vatCategory.E')}</option>
                <option value="AE">{t('accounting.vatCategory.AE')}</option>
                <option value="K">{t('accounting.vatCategory.K')}</option>
                <option value="G">{t('accounting.vatCategory.G')}</option>
              </select>
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={categoryDraft.isActive}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              {t('accounting.categories.activeLabel')}
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t('accounting.accounts.deleteTitle')}
          message={t('accounting.accounts.deleteMessage', { number: deleteTarget.accountNumber, name: deleteTarget.name })}
          confirmLabel={t('ui.actions.delete')}
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await deleteLedgerAccount(target.id)
              showSuccess(t('accounting.accounts.deleted'))
              reload()
            } catch (err) {
              showError(localizeApiError(t, err, t('accounting.accounts.deleteFailed')))
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
