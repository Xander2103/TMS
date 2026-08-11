import { useEffect, useRef, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { describeApiError } from '../../../api/problemDetails'
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
  const [entityDialog, setEntityDialog] = useState(false)
  const [entities, setEntities] = useState<LegalEntityOption[]>([])
  const [entityId, setEntityId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const menuRef = useRef<HTMLDetailsElement>(null)

  const operational = operationalStatus(dossier)
  const price = priceChip(dossier)

  useEffect(() => {
    if (!entityDialog) return
    getLegalEntityOptions()
      .then((options) => setEntities(options.filter((o) => o.isActive)))
      .catch(() => setEntities([]))
  }, [entityDialog])

  function openEntityDialog() {
    setEntityId(dossier.legalEntityId ?? '')
    setError(null)
    setEntityDialog(true)
  }

  async function saveEntity() {
    if (!entityId) {
      setError('Kies een entiteit.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await changeDossierLegalEntity(dossier.id, entityId, dossier.version)
      onUpdated(updated)
      setEntityDialog(false)
    } catch (err) {
      if (onConflict(err)) {
        setEntityDialog(false)
        return
      }
      setError(describeApiError(err, 'De entiteit kon niet worden gewijzigd.').message)
    } finally {
      setBusy(false)
    }
  }

  const metaParts = [
    dossier.customerName ?? 'Geen klant',
    dossier.customerReference ? `ref. ${dossier.customerReference}` : null,
    dossier.dossierDate ? formatDate(dossier.dossierDate) : null,
  ].filter((part): part is string => part !== null)

  return (
    <header className="dossier-header">
      <div className="dossier-header-title">
        <h1>
          Dossier {dossier.dossierNumber}{' '}
          <Badge tone={DOSSIER_STATUS_TONE[dossier.status]}>{DOSSIER_STATUS_LABELS[dossier.status]}</Badge>
        </h1>
        <p className="dossier-header-meta">
          {metaParts.join(' · ')}
          {' · '}
          <span className="dossier-header-entity">
            Entiteit: {dossier.legalEntityName ?? '—'}
            {canManage && (
              <button
                type="button"
                className="link-button"
                onClick={openEntityDialog}
                aria-label="Entiteit wijzigen"
              >
                wijzigen
              </button>
            )}
          </span>
        </p>
        <p className="dossier-header-chips">
          <span>
            Operationeel: <Badge tone="info">{operational ?? '—'}</Badge>
          </span>
          <span>
            Prijs: <Badge tone={price.tone}>{price.label}</Badge>
          </span>
        </p>
      </div>

      <div className="dossier-header-actions">
        {canManage && dossier.status === 'Open' && <Button onClick={onAddActivity}>+ Activiteit</Button>}
        {menuActions.length > 0 && (
          <details className="dossier-more" ref={menuRef}>
            <summary role="button" aria-haspopup="menu">
              Meer ▾
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
          title="Facturerende entiteit wijzigen"
          onClose={() => setEntityDialog(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEntityDialog(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button onClick={() => void saveEntity()} disabled={busy || !entityId}>
                Wijzigen
              </Button>
            </>
          }
        >
          <ValidationSummary message={error} />
          <FormField label="Entiteit" htmlFor="dh-entity" required hint="De wijziging wordt gelogd (oud → nieuw).">
            <select id="dh-entity" value={entityId} onChange={(event) => setEntityId(event.target.value)} disabled={busy}>
              <option value="">— Kies een entiteit —</option>
              {entities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.displayName}
                  {entity.isDefault ? ' (standaard)' : ''}
                </option>
              ))}
            </select>
          </FormField>
        </Modal>
      )}
    </header>
  )
}
