import { useEffect, useRef, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { getCustomer } from '../../customers/api/customersApi'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import { changeDossierLegalEntity } from '../api/dossiersApi'
import { formatDate, operationalStatus, priceChip } from '../dossierDisplay'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierDetail } from '../types'

export interface DossierMenuAction {
  key: string
  label: string
  onSelect: () => void
  danger?: boolean
}

interface DossierHeaderProps {
  dossier: DossierDetail
  canManage: boolean
  onAddActivity: () => void
  /** Meer ▾ items: bewerken kop, sluiten/heropenen, relaties, historiek… supplied by the page. */
  menuActions: DossierMenuAction[]
  onUpdated: (dossier: DossierDetail) => void
  onConflict: (err: unknown) => boolean
}

/** §11 header: nummer + status, klant · ref · datum · entiteit, twee statuschips, primaire actie. */
export function DossierHeader({ dossier, canManage, onAddActivity, menuActions, onUpdated, onConflict }: DossierHeaderProps) {
  const { t } = useLocale()
  const [entityDialog, setEntityDialog] = useState(false)
  const [entities, setEntities] = useState<LegalEntityOption[]>([])
  const [entityId, setEntityId] = useState('')
  const [entityReason, setEntityReason] = useState('')
  // Sprint 6: the customer's allowed entities and default decide what is offered and whether
  // the choice is the privileged override path (right + reason).
  const [allowedIds, setAllowedIds] = useState<string[]>([])
  const [customerDefaultId, setCustomerDefaultId] = useState<string | null>(null)
  const { hasPermission } = useAuth()
  const mayOverride = hasPermission('dossiers.override_entity')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const menuRef = useRef<HTMLDetailsElement>(null)

  const operational = operationalStatus(dossier)
  const price = priceChip(dossier)

  useEffect(() => {
    if (!entityDialog) return
    Promise.all([
      getLegalEntityOptions(),
      dossier.customerId ? getCustomer(dossier.customerId) : Promise.resolve(null),
    ])
      .then(([options, customer]) => {
        setEntities(options.filter((o) => o.isActive))
        setAllowedIds(customer?.allowedLegalEntityIds ?? [])
        setCustomerDefaultId(customer?.defaultLegalEntityId ?? null)
      })
      .catch(() => setEntities([]))
  }, [entityDialog, dossier.customerId])

  const visibleEntities = allowedIds.length > 0
    ? entities.filter((e) => allowedIds.includes(e.id) || e.id === dossier.legalEntityId)
    : entities
  const entityDeviates = entityId !== '' && entityId !== dossier.legalEntityId
    && (customerDefaultId === null || entityId !== customerDefaultId)
  const lacksOverrideRight = entityDeviates && !mayOverride

  function openEntityDialog() {
    setEntityId(dossier.legalEntityId ?? '')
    setEntityReason('')
    setError(null)
    setEntityDialog(true)
  }

  async function saveEntity() {
    if (!entityId) {
      setError(t('dossiers.header.entityRequired'))
      return
    }
    if (entityDeviates && !entityReason.trim()) {
      setError(t('dossiers.header.entityReasonRequired'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await changeDossierLegalEntity(
        dossier.id, entityId, dossier.version, entityReason.trim() || undefined)
      onUpdated(updated)
      setEntityDialog(false)
    } catch (err) {
      if (onConflict(err)) {
        setEntityDialog(false)
        return
      }
      setError(describeApiError(err, t('dossiers.header.entityChangeFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  const metaParts = [
    dossier.customerName ?? t('dossiers.header.noCustomer'),
    dossier.customerReference ? t('dossiers.header.reference', { reference: dossier.customerReference }) : null,
    dossier.dossierDate ? formatDate(dossier.dossierDate) : null,
  ].filter((part): part is string => part !== null)

  return (
    <header className="dossier-header">
      <div className="dossier-header-title">
        <h1>
          {t('dossiers.header.title', { number: dossier.dossierNumber })}{' '}
          <Badge tone={DOSSIER_STATUS_TONE[dossier.status]}>{t(DOSSIER_STATUS_LABELS[dossier.status])}</Badge>
        </h1>
        <p className="dossier-header-meta">
          {metaParts.join(' · ')}
          {' · '}
          <span className="dossier-header-entity">
            {t('dossiers.header.entity', { name: dossier.legalEntityName ?? '—' })}
            {canManage && (
              <button
                type="button"
                className="link-button"
                onClick={openEntityDialog}
                aria-label={t('dossiers.header.changeEntityAria')}
              >
                {t('dossiers.header.change')}
              </button>
            )}
          </span>
        </p>
        <p className="dossier-header-chips">
          <span>
            {t('dossiers.header.operational')} <Badge tone="info">{operational ? t(operational) : '—'}</Badge>
          </span>
          <span>
            {t('dossiers.header.price')} <Badge tone={price.tone}>{price.labelKey ? t(price.labelKey) : '—'}</Badge>
          </span>
        </p>
      </div>

      <div className="dossier-header-actions">
        {canManage && dossier.status === 'Open' && <Button onClick={onAddActivity}>{t('dossiers.header.addActivity')}</Button>}
        {menuActions.length > 0 && (
          <details className="dossier-more" ref={menuRef}>
            <summary role="button" aria-haspopup="menu">
              {t('dossiers.header.more')}
            </summary>
            <div className="dossier-more-menu" role="menu">
              {menuActions.map((action) => (
                <button
                  key={action.key}
                  type="button"
                  role="menuitem"
                  className={action.danger ? 'dossier-more-danger' : undefined}
                  onClick={() => {
                    menuRef.current?.removeAttribute('open')
                    action.onSelect()
                  }}
                >
                  {action.label}
                </button>
              ))}
            </div>
          </details>
        )}
      </div>

      {entityDialog && (
        <Modal
          title={t('dossiers.header.entityDialogTitle')}
          onClose={() => setEntityDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEntityDialog(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={() => void saveEntity()} disabled={busy || !entityId || lacksOverrideRight}>
                {t('dossiers.header.changeAction')}
              </Button>
            </>
          }
        >
          <ValidationSummary message={error} />
          <FormField
            label={t('dossiers.header.entityField')}
            htmlFor="dh-entity"
            required
            hint={dossier.customerId ? t('dossiers.header.entityAllowedHint') : t('dossiers.header.entityFieldHint')}
          >
            <select id="dh-entity" value={entityId} onChange={(event) => setEntityId(event.target.value)} disabled={busy}>
              <option value="">{t('dossiers.header.chooseEntity')}</option>
              {visibleEntities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.displayName}
                  {(customerDefaultId ? entity.id === customerDefaultId : entity.isDefault) ? ` ${t('dossiers.header.defaultSuffix')}` : ''}
                </option>
              ))}
            </select>
          </FormField>
          {entityDeviates && (
            <p className={lacksOverrideRight ? 'customer-import-message customer-import-message-error' : 'customer-form-muted'} role={lacksOverrideRight ? 'alert' : undefined}>
              {lacksOverrideRight ? t('dossiers.header.entityNoOverride') : t('dossiers.header.entityDeviates')}
            </p>
          )}
          <FormField
            label={t('dossiers.header.reason')}
            htmlFor="dh-entity-reason"
            hint={t('dossiers.header.reasonHint')}
          >
            <input
              id="dh-entity-reason"
              value={entityReason}
              onChange={(event) => setEntityReason(event.target.value)}
              disabled={busy}
              maxLength={500}
            />
          </FormField>
        </Modal>
      )}
    </header>
  )
}
