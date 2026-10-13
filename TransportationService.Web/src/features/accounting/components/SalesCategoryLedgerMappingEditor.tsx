import { useEffect, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import type { LedgerAccount, SalesCategoryLedgerMapping } from '../api/accountingApi'
import '../../transport-orders/components/commercialChange.css'

interface SalesCategoryLedgerMappingEditorProps {
  accounts: LedgerAccount[]
  value: SalesCategoryLedgerMapping[]
  onChange: (mappings: SalesCategoryLedgerMapping[]) => void
  disabled?: boolean
}

/**
 * Sprint 5: "Boekhoudkundige koppeling per entiteit" — one row per active invoicing entity with
 * an optional ledger account and cost centre. A row without an account means "use the sales
 * code's default account" and is not sent. Invoice lines snapshot the resolved account at
 * Send, so editing here never moves history.
 */
export function SalesCategoryLedgerMappingEditor({ accounts, value, onChange, disabled }: SalesCategoryLedgerMappingEditorProps) {
  const { t } = useLocale()
  const [entities, setEntities] = useState<LegalEntityOption[] | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let mounted = true
    getLegalEntityOptions()
      .then((options) => {
        if (mounted) setEntities(options.filter((o) => o.isActive))
      })
      .catch(() => {
        if (mounted) setFailed(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  function rowFor(entityId: string): SalesCategoryLedgerMapping | undefined {
    return value.find((m) => m.legalEntityId === entityId)
  }

  function update(entityId: string, patch: Partial<SalesCategoryLedgerMapping>) {
    const existing = rowFor(entityId)
    const next: SalesCategoryLedgerMapping = { legalEntityId: entityId, ledgerAccountId: '', costCentre: null, ...existing, ...patch }
    const others = value.filter((m) => m.legalEntityId !== entityId)
    // No account = no mapping for this entity (the default account applies).
    onChange(next.ledgerAccountId ? [...others, next] : others)
  }

  return (
    <fieldset className="sc-ledger-mappings">
      <legend>{t('accounting.ledgerMappings.title')}</legend>
      <p className="customer-form-muted">{t('accounting.ledgerMappings.intro')}</p>
      {failed && <p className="customer-form-muted">{t('accounting.ledgerMappings.loadFailed')}</p>}
      {entities && entities.length === 0 && <p className="customer-form-muted">{t('accounting.ledgerMappings.noEntities')}</p>}
      {entities && entities.length > 0 && (
        <table className="accounting-table sc-ledger-mappings-table">
          <thead>
            <tr>
              <th>{t('accounting.ledgerMappings.entity')}</th>
              <th>{t('accounting.ledgerMappings.account')}</th>
              <th>{t('accounting.ledgerMappings.costCentre')}</th>
            </tr>
          </thead>
          <tbody>
            {entities.map((entity) => {
              const row = rowFor(entity.id)
              return (
                <tr key={entity.id}>
                  <td>{entity.displayName}</td>
                  <td>
                    <select
                      aria-label={`${t('accounting.ledgerMappings.account')} ${entity.displayName}`}
                      value={row?.ledgerAccountId ?? ''}
                      onChange={(e) => update(entity.id, { ledgerAccountId: e.target.value })}
                      disabled={disabled}
                    >
                      <option value="">{t('accounting.ledgerMappings.useDefault')}</option>
                      {accounts
                        .filter((a) => a.isActive || a.id === row?.ledgerAccountId)
                        .map((account) => (
                          <option key={account.id} value={account.id}>
                            {account.accountNumber} — {account.name}
                          </option>
                        ))}
                    </select>
                  </td>
                  <td>
                    <input
                      aria-label={`${t('accounting.ledgerMappings.costCentre')} ${entity.displayName}`}
                      value={row?.costCentre ?? ''}
                      maxLength={40}
                      onChange={(e) => update(entity.id, { costCentre: e.target.value || null })}
                      disabled={disabled || !row?.ledgerAccountId}
                    />
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </fieldset>
  )
}
