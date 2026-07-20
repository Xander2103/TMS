import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiClient } from '../../api/apiClient'
import {
  deleteResourceLink, listResourceLinks, touchResourceLink, type ResourceLink,
} from '../../api/resourceLinksApi'
import { filterCommands, type AppCommand } from '../../config/commands'
import { useAuth } from '../../features/auth/authContextValue'
import './CommandPalette.css'

interface SearchHit {
  category: string
  id: string
  title: string
  subtitle: string | null
  route: string
}

/** Backend search categories that map onto the server-side favorites/recents catalog. */
const CATEGORY_ENTITY_TYPES: Record<string, string> = {
  Transportopdrachten: 'TransportOrder',
  Klanten: 'Customer',
  Dossiers: 'TransportDossier',
  Incidenten: 'Incident',
  Chauffeurs: 'Driver',
  Voertuigen: 'Vehicle',
  Opleggers: 'Trailer',
  Locaties: 'Location',
}

const DEBOUNCE_MS = 250

type Row =
  | { kind: 'command'; command: AppCommand }
  | { kind: 'link'; link: ResourceLink }
  | { kind: 'hit'; hit: SearchHit }

interface CommandPaletteProps {
  open: boolean
  onClose: () => void
}

/**
 * Ctrl+K palette: permission-filtered commands, server-backed favorites and recents, and
 * the permission-filtered global search. Opening an entity records a server-side recent;
 * the star toggles a favorite. Commands never bypass permissions or confirmations — they
 * only navigate.
 */
export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const navigate = useNavigate()
  const { hasAnyPermission } = useAuth()
  const [query, setQuery] = useState('')
  const [loadedHits, setLoadedHits] = useState<{ key: string; hits: SearchHit[] } | null>(null)
  const [favorites, setFavorites] = useState<ResourceLink[]>([])
  const [recents, setRecents] = useState<ResourceLink[]>([])
  const [activeIndex, setActiveIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const term = query.trim()
  const hits = loadedHits?.key === term ? loadedHits.hits : null

  const close = useCallback(() => {
    setQuery('')
    setLoadedHits(null)
    setActiveIndex(0)
    onClose()
  }, [onClose])

  useEffect(() => {
    if (!open) return
    inputRef.current?.focus()
    listResourceLinks('Favorite')
      .then(setFavorites)
      .catch(() => undefined)
    listResourceLinks('Recent')
      .then(setRecents)
      .catch(() => undefined)
  }, [open])

  useEffect(() => {
    if (!open || term.length < 2) return
    let mounted = true
    const timer = window.setTimeout(() => {
      apiClient
        .getJson<SearchHit[]>(`/api/search?q=${encodeURIComponent(term)}`)
        .then((data) => {
          if (mounted) {
            setLoadedHits({ key: term, hits: data })
            setActiveIndex(0)
          }
        })
        .catch(() => {
          if (mounted) setLoadedHits({ key: term, hits: [] })
        })
    }, DEBOUNCE_MS)
    return () => {
      mounted = false
      window.clearTimeout(timer)
    }
  }, [open, term])

  const commands = useMemo(
    () => filterCommands(term, hasAnyPermission),
    [term, hasAnyPermission])

  const rows: Row[] = useMemo(() => {
    const list: Row[] = commands.map((command) => ({ kind: 'command', command }))
    if (term.length < 2) {
      list.push(...favorites.map((link): Row => ({ kind: 'link', link })))
      list.push(...recents.slice(0, 8).map((link): Row => ({ kind: 'link', link })))
    } else {
      list.push(...(hits ?? []).map((hit): Row => ({ kind: 'hit', hit })))
    }
    return list
  }, [commands, favorites, recents, hits, term])

  const openRow = useCallback((row: Row) => {
    close()
    if (row.kind === 'command') {
      navigate(row.command.route)
      return
    }
    if (row.kind === 'link') {
      navigate(row.link.route)
      return
    }
    // Entity hit: record a server-side recent for known entity types (fire-and-forget).
    const entityType = CATEGORY_ENTITY_TYPES[row.hit.category]
    if (entityType) {
      void touchResourceLink({
        kind: 'Recent', entityType, entityId: row.hit.id,
        label: row.hit.title, subtitle: row.hit.subtitle, route: row.hit.route,
      }).catch(() => undefined)
    }
    navigate(row.hit.route)
  }, [close, navigate])

  function toggleFavorite(hit: SearchHit) {
    const entityType = CATEGORY_ENTITY_TYPES[hit.category]
    if (!entityType) return
    const existing = favorites.find((f) => f.entityType === entityType && f.entityId === hit.id)
    if (existing) {
      setFavorites((current) => current.filter((f) => f.id !== existing.id))
      void deleteResourceLink(existing.id).catch(() => undefined)
    } else {
      void touchResourceLink({
        kind: 'Favorite', entityType, entityId: hit.id,
        label: hit.title, subtitle: hit.subtitle, route: hit.route,
      })
        .then((link) => setFavorites((current) => [...current, link]))
        .catch(() => undefined)
    }
  }

  if (!open) return null

  function onInputKeyDown(event: React.KeyboardEvent) {
    if (event.key === 'Escape') {
      close()
    } else if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActiveIndex((index) => Math.min(index + 1, rows.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveIndex((index) => Math.max(index - 1, 0))
    } else if (event.key === 'Enter' && rows[activeIndex]) {
      event.preventDefault()
      openRow(rows[activeIndex])
    }
  }

  const commandRows = rows.filter((r) => r.kind === 'command')
  const favoriteRows = term.length < 2 ? rows.filter((r) => r.kind === 'link' && r.link.kind === 'Favorite') : []
  const recentRows = term.length < 2 ? rows.filter((r) => r.kind === 'link' && r.link.kind === 'Recent') : []
  const hitRows = rows.filter((r) => r.kind === 'hit')
  const groupedHits = hitRows.reduce<Map<string, Row[]>>((map, row) => {
    const category = (row as { hit: SearchHit }).hit.category
    const list = map.get(category) ?? []
    list.push(row)
    map.set(category, list)
    return map
  }, new Map())

  function renderRow(row: Row) {
    const index = rows.indexOf(row)
    const active = index === activeIndex
    if (row.kind === 'command') {
      return (
        <button key={`cmd-${row.command.id}`} type="button"
                className={active ? 'cmdk-hit cmdk-hit-active' : 'cmdk-hit'}
                onMouseEnter={() => setActiveIndex(index)} onClick={() => openRow(row)}>
          <span className="cmdk-hit-title">⌘ {row.command.label}</span>
          <span className="cmdk-hit-subtitle">{row.command.group}</span>
        </button>
      )
    }
    if (row.kind === 'link') {
      return (
        <button key={`link-${row.link.id}`} type="button"
                className={active ? 'cmdk-hit cmdk-hit-active' : 'cmdk-hit'}
                onMouseEnter={() => setActiveIndex(index)} onClick={() => openRow(row)}>
          <span className="cmdk-hit-title">{row.link.kind === 'Favorite' ? '★ ' : ''}{row.link.label}</span>
          {row.link.subtitle && <span className="cmdk-hit-subtitle">{row.link.subtitle}</span>}
        </button>
      )
    }
    const favoritable = CATEGORY_ENTITY_TYPES[row.hit.category] !== undefined
    const isFavorite = favorites.some((f) =>
      f.entityType === CATEGORY_ENTITY_TYPES[row.hit.category] && f.entityId === row.hit.id)
    return (
      <div key={`hit-${row.hit.category}-${row.hit.id}`}
           className={active ? 'cmdk-hit cmdk-hit-active cmdk-hit-row' : 'cmdk-hit cmdk-hit-row'}
           onMouseEnter={() => setActiveIndex(index)}>
        <button type="button" className="cmdk-hit-main" onClick={() => openRow(row)}>
          <span className="cmdk-hit-title">{row.hit.title}</span>
          {row.hit.subtitle && <span className="cmdk-hit-subtitle">{row.hit.subtitle}</span>}
        </button>
        {favoritable && (
          <button type="button" className={`cmdk-star${isFavorite ? ' cmdk-star-active' : ''}`}
                  onClick={() => toggleFavorite(row.hit)}
                  aria-label={isFavorite ? 'Favoriet verwijderen' : 'Aan favorieten toevoegen'}>
            ★
          </button>
        )}
      </div>
    )
  }

  return (
    <div className="cmdk-overlay" onClick={close} role="presentation">
      <div className="cmdk-panel" onClick={(event) => event.stopPropagation()} role="dialog" aria-label="Globaal zoeken">
        <input
          ref={inputRef}
          className="cmdk-input"
          placeholder="Zoek of typ een commando… (Esc om te sluiten)"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onInputKeyDown}
          aria-label="Zoekterm"
        />
        <div className="cmdk-results">
          {commandRows.length > 0 && <div className="cmdk-category">Commando’s</div>}
          {commandRows.map(renderRow)}

          {favoriteRows.length > 0 && <div className="cmdk-category">Favorieten</div>}
          {favoriteRows.map(renderRow)}

          {recentRows.length > 0 && <div className="cmdk-category">Recent bezocht</div>}
          {recentRows.map(renderRow)}

          {term.length < 2 && <p className="cmdk-hint">Typ minstens 2 tekens om te zoeken.</p>}
          {term.length >= 2 && !hits && <p className="cmdk-hint">Zoeken...</p>}
          {hits && hits.length === 0 && <p className="cmdk-hint">Geen resultaten voor "{term}".</p>}
          {[...groupedHits.entries()].map(([category, categoryRows]) => (
            <div key={category}>
              <div className="cmdk-category">{category}</div>
              {categoryRows.map(renderRow)}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
