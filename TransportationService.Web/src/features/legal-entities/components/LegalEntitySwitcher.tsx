import { useEffect, useState, type ChangeEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/authContextValue'
import { getActiveLegalEntity, getLegalEntityOptions, setActiveLegalEntity } from '../api/legalEntitiesApi'
import type { LegalEntityOption } from '../types'
import './LegalEntitySwitcher.css'

const NEW_ENTITY_VALUE = '__new'

/**
 * Compact entity picker in the sidebar: switches the user's own active (facturing)
 * legal entity. Purely cosmetic preference — the backend never authorizes on it.
 */
export function LegalEntitySwitcher() {
  const { hasPermission } = useAuth()
  const canManage = hasPermission('legal_entities.manage')
  const navigate = useNavigate()

  const [options, setOptions] = useState<LegalEntityOption[] | null>(null)
  const [activeId, setActiveId] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    Promise.all([getLegalEntityOptions(), getActiveLegalEntity()])
      .then(([loadedOptions, active]) => {
        if (!mounted) return
        setOptions(loadedOptions.filter((option) => option.isActive))
        setActiveId(active.legalEntityId)
      })
      .catch((error: unknown) => {
        // Silent: the switcher is a convenience, the sidebar must never break on it.
        console.warn('Actief bedrijf kon niet worden geladen.', error)
        if (mounted) setOptions([])
      })
    return () => {
      mounted = false
    }
  }, [])

  if (!options || options.length === 0) return null
  // A single option without manage rights offers no choice — render nothing.
  if (options.length === 1 && !canManage) return null

  const selectedId = activeId ?? options.find((option) => option.isDefault)?.id ?? options[0].id

  function handleChange(event: ChangeEvent<HTMLSelectElement>) {
    const value = event.target.value
    if (value === NEW_ENTITY_VALUE) {
      // Controlled select: state is untouched, so it snaps back to the previous value.
      void navigate('/settings/legal-entities')
      return
    }
    const previous = activeId
    setActiveId(value)
    setActiveLegalEntity(value).catch((error: unknown) => {
      console.warn('Actief bedrijf kon niet worden gewijzigd.', error)
      setActiveId(previous)
    })
  }

  return (
    <div className="le-switcher">
      <label className="le-switcher-label" htmlFor="le-switcher-select">
        Actief bedrijf
      </label>
      <select id="le-switcher-select" className="le-switcher-select" value={selectedId} onChange={handleChange}>
        {options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.displayName}
            {option.isDefault ? ' (standaard)' : ''}
          </option>
        ))}
        {canManage && <option value={NEW_ENTITY_VALUE}>+ Nieuw bedrijf…</option>}
      </select>
    </div>
  )
}
