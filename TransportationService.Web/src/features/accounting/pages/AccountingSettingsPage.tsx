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
import { describeApiError } from '../../../api/problemDetails'
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
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('accounting.manage')
  const canView = hasPermission('accounting.view') || canManage

  const [accounts, setAccounts] = useState<LedgerAccount[] | null>(null)
  const [categories, setCategories] = useState<SalesCategory[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
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
        setLoadError(null)
      })
      .catch(() => setLoadError('De boekhoudinstellingen konden niet worden geladen.'))
  }, [canView])

  useEffect(() => {
    reload()
  }, [reload])

  if (!canView) return <p className="placeholder-text">Je hebt geen rechten om de boekhoudinstellingen te bekijken.</p>
  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (accounts === null || categories === null) return <p className="placeholder-text">Boekhouding laden…</p>

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
      showSuccess(`Grootboekrekening voor '${category.name}' bijgewerkt.`)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De mapping kon niet worden opgeslagen.').message)
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
        showSuccess('Grootboekrekening bijgewerkt.')
      } else {
        await createLedgerAccount(input)
        showSuccess('Grootboekrekening toegevoegd.')
      }
      setAccountDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De grootboekrekening kon niet worden opgeslagen.').message)
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
        showSuccess('Verkoopcategorie bijgewerkt.')
      } else {
        await createSalesCategory(input)
        showSuccess('Verkoopcategorie toegevoegd.')
      }
      setCategoryDraft(null)
      reload()
    } catch (err) {
      setDraftError(describeApiError(err, 'De verkoopcategorie kon niet worden opgeslagen.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Instellingen', to: '/settings' }, { label: 'Boekhouding' }]} />
      <PageHeader
        title="Boekhouding"
        subtitle="Grootboekrekeningen en de koppeling per verkoopcategorie. Factuurlijnen bevriezen de rekening bij het versturen — latere wijzigingen raken bestaande facturen nooit."
      />

      {unmapped.length > 0 && (
        <div className="accounting-warning" role="alert">
          Geen grootboekrekening ingesteld voor{' '}
          {unmapped.map((c) => `'${c.name}'`).join(', ')}. Kies hieronder een rekening per categorie —
          zonder mapping kan de boekhoudexport deze lijnen niet meenemen.
        </div>
      )}

      <section className="accounting-card">
        <div className="accounting-card-head">
          <h3>Verkoopcategorieën</h3>
          {canManage && (
            <Button variant="secondary" onClick={() => {
              setDraftError(null)
              setCategoryDraft({
                category: null, code: '', name: '', systemRole: 'None', isActive: true,
                invoiceDescriptionNl: '', defaultUnitCode: '', vatCategoryOverride: '',
              })
            }}>
              + Verkoopcategorie
            </Button>
          )}
        </div>
        <table className="issued-items-table">
          <thead>
            <tr>
              <th>Verkoopcategorie</th>
              <th>Gebruik</th>
              <th>Grootboekrekening</th>
              <th>Status</th>
              {canManage && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {categories.map((category) => (
              <tr key={category.id}>
                <td>{category.name}</td>
                <td className="customer-form-muted">{SYSTEM_ROLE_LABELS[category.systemRole]}</td>
                <td>
                  {canManage ? (
                    <select
                      aria-label={`Grootboekrekening voor ${category.name}`}
                      value={category.ledgerAccountId ?? ''}
                      onChange={(e) => void changeMapping(category, e.target.value || null)}
                    >
                      <option value="">— Geen —</option>
                      {activeAccounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.accountNumber} — {account.name}
                        </option>
                      ))}
                      {category.ledgerAccountId && !activeAccounts.some((a) => a.id === category.ledgerAccountId) && (
                        <option value={category.ledgerAccountId}>
                          {category.ledgerAccountNumber} — {category.ledgerAccountName} (inactief)
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
                      <Badge tone="success">Gekoppeld</Badge>
                    ) : (
                      <Badge tone="warning">Geen rekening</Badge>
                    )
                  ) : (
                    <Badge tone="neutral">Inactief</Badge>
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
                      Bewerken
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
          <h3>Grootboekrekeningen</h3>
          {canManage && (
            <Button variant="secondary" onClick={() => {
              setDraftError(null)
              setAccountDraft({ account: null, accountNumber: '', name: '', externalCode: '', description: '', isActive: true })
            }}>
              + Grootboekrekening
            </Button>
          )}
        </div>
        {accounts.length === 0 && (
          <p className="placeholder-text">
            Nog geen grootboekrekeningen. Voeg de rekeningen van jouw boekhoudpakket toe (bv. 700000 — Transportopbrengsten).
          </p>
        )}
        {accounts.length > 0 && (
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Nummer</th>
                <th>Naam</th>
                <th>Externe code</th>
                <th>Status</th>
                {canManage && <th aria-label="Acties" />}
              </tr>
            </thead>
            <tbody>
              {accounts.map((account) => (
                <tr key={account.id}>
                  <td>{account.accountNumber}</td>
                  <td>{account.name}</td>
                  <td>{account.externalCode ?? '—'}</td>
                  <td>
                    <Badge tone={account.isActive ? 'success' : 'neutral'}>{account.isActive ? 'Actief' : 'Inactief'}</Badge>
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
                        Bewerken
                      </button>
                      <button
                        type="button"
                        className="issued-items-link issued-items-link-danger"
                        onClick={() => setDeleteTarget(account)}
                      >
                        Verwijderen
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
          title={accountDraft.account ? `Grootboekrekening bewerken — ${accountDraft.account.accountNumber}` : 'Grootboekrekening toevoegen'}
          onClose={() => setAccountDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setAccountDraft(null)} disabled={busy}>Annuleren</Button>
              <Button type="submit" form="ledger-account-form" disabled={busy}>Opslaan</Button>
            </>
          }
        >
          <form id="ledger-account-form" onSubmit={submitAccount} noValidate>
            {draftError && <div role="alert" className="issued-items-form-error">{draftError}</div>}
            <FormField label="Rekeningnummer" htmlFor="la-number" required hint="bv. 700000">
              <input id="la-number" value={accountDraft.accountNumber} maxLength={30}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, accountNumber: e.target.value } : d))} />
            </FormField>
            <FormField label="Naam" htmlFor="la-name" required hint="bv. Transportopbrengsten">
              <input id="la-name" value={accountDraft.name} maxLength={200}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, name: e.target.value } : d))} />
            </FormField>
            <FormField label="Externe code (boekhoudpakket)" htmlFor="la-external">
              <input id="la-external" value={accountDraft.externalCode} maxLength={50}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, externalCode: e.target.value } : d))} />
            </FormField>
            <FormField label="Omschrijving" htmlFor="la-description">
              <input id="la-description" value={accountDraft.description} maxLength={1000}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, description: e.target.value } : d))} />
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={accountDraft.isActive}
                onChange={(e) => setAccountDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              Actief (toewijsbaar aan categorieën)
            </label>
          </form>
        </Modal>
      )}

      {categoryDraft && (
        <Modal
          title={categoryDraft.category ? `Verkoopcategorie bewerken — ${categoryDraft.category.name}` : 'Verkoopcategorie toevoegen'}
          onClose={() => setCategoryDraft(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCategoryDraft(null)} disabled={busy}>Annuleren</Button>
              <Button type="submit" form="sales-category-form" disabled={busy}>Opslaan</Button>
            </>
          }
        >
          <form id="sales-category-form" onSubmit={submitCategory} noValidate>
            {draftError && <div role="alert" className="issued-items-form-error">{draftError}</div>}
            <FormField label="Code" htmlFor="sc-code" required hint="bv. EUROPALLETS">
              <input id="sc-code" value={categoryDraft.code} maxLength={50}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, code: e.target.value } : d))} />
            </FormField>
            <FormField label="Naam" htmlFor="sc-name" required>
              <input id="sc-name" value={categoryDraft.name} maxLength={200}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, name: e.target.value } : d))} />
            </FormField>
            <FormField
              label="Gebruik"
              htmlFor="sc-role"
              hint="Automatische rollen bepalen welke factuurlijnen deze categorie vanzelf krijgen; per rol kan er maar één actieve categorie zijn."
            >
              <select id="sc-role" value={categoryDraft.systemRole}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, systemRole: e.target.value as SalesCategorySystemRole } : d))}>
                {Object.entries(SYSTEM_ROLE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </FormField>
            <FormField label="Factuuromschrijving" htmlFor="sc-invoice-desc" hint="Leeg = de naam van de categorie.">
              <input id="sc-invoice-desc" value={categoryDraft.invoiceDescriptionNl} maxLength={300}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, invoiceDescriptionNl: e.target.value } : d))} />
            </FormField>
            <FormField label="Standaard eenheid" htmlFor="sc-unit" hint="UN/ECE-code voor handmatige factuurlijnen met deze code, bv. C62, HUR, KGM.">
              <input id="sc-unit" value={categoryDraft.defaultUnitCode} maxLength={10}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, defaultUnitCode: e.target.value } : d))} />
            </FormField>
            <FormField
              label="Btw-categorie (afwijking)"
              htmlFor="sc-vat"
              hint="Dwingt de UNCL5305-btw-categorie voor lijnen met deze code af; leeg = de btw-regeling van de klant beslist (de norm)."
            >
              <select id="sc-vat" value={categoryDraft.vatCategoryOverride}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, vatCategoryOverride: e.target.value } : d))}>
                <option value="">— Geen (btw-regeling klant) —</option>
                <option value="S">S — Standaardtarief</option>
                <option value="Z">Z — Nultarief</option>
                <option value="E">E — Vrijgesteld</option>
                <option value="AE">AE — Btw verlegd</option>
                <option value="K">K — Intracommunautair</option>
                <option value="G">G — Export buiten EU</option>
              </select>
            </FormField>
            <label className="tof-checkbox">
              <input type="checkbox" checked={categoryDraft.isActive}
                onChange={(e) => setCategoryDraft((d) => (d ? { ...d, isActive: e.target.checked } : d))} />
              Actief
            </label>
          </form>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Grootboekrekening verwijderen"
          message={`Weet je zeker dat je '${deleteTarget.accountNumber} — ${deleteTarget.name}' wilt verwijderen? Een rekening die in gebruik is, kan alleen gedeactiveerd worden.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={async () => {
            const target = deleteTarget
            setDeleteTarget(null)
            try {
              await deleteLedgerAccount(target.id)
              showSuccess('Grootboekrekening verwijderd.')
              reload()
            } catch (err) {
              showError(describeApiError(err, 'De grootboekrekening kon niet worden verwijderd.').message)
            }
          }}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
