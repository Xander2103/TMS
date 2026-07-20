import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiClient } from '../../api/apiClient'
import { listRecentItems } from '../../hooks/recentItems'
import './CommandPalette.css'

interface SearchHit {
  category: string
  id: string
  title: string
  subtitle: string | null
  route: string
}

const DEBOUNCE_MS = 250

/**
 * Ctrl+K / Cmd+K command palette over the global search endpoint. The backend already
 * filters every category on the caller's view permissions, so whatever comes back is safe
 * to show and navigate to.
 */
export function CommandPalette() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [loadedHits, setLoadedHits] = useState<{ key: string; hits: SearchHit[] } | null>(null)
  const [activeIndex, setActiveIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const term = query.trim()
  const hits = loadedHits?.key === term ? loadedHits.hits : null

  // Closing resets the palette state here (not in an effect) so no setState runs
  // synchronously inside an effect body.
  const close = useCallback(() => {
    setOpen(false)
    setQuery('')
    setLoadedHits(null)
    setActiveIndex(0)
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        if (open) {
          close()
        } else {
          setOpen(true)
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [open, close])

  useEffect(() => {
    if (open) {
      inputRef.current?.focus()
    }
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

  const openHit = useCallback(
    (hit: SearchHit) => {
      close()
      navigate(hit.route)
    },
    [navigate, close],
  )

  if (!open) return null

  const flat = hits ?? []
  const grouped = flat.reduce<Map<string, SearchHit[]>>((map, hit) => {
    const list = map.get(hit.category) ?? []
    list.push(hit)
    map.set(hit.category, list)
    return map
  }, new Map())

  function onInputKeyDown(event: React.KeyboardEvent) {
    if (event.key === 'Escape') {
      close()
    } else if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActiveIndex((index) => Math.min(index + 1, flat.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveIndex((index) => Math.max(index - 1, 0))
    } else if (event.key === 'Enter' && flat[activeIndex]) {
      event.preventDefault()
      openHit(flat[activeIndex])
    }
  }

  return (
    <div className="cmdk-overlay" onClick={close} role="presentation">
      <div className="cmdk-panel" onClick={(event) => event.stopPropagation()} role="dialog" aria-label="Globaal zoeken">
        <input
          ref={inputRef}
          className="cmdk-input"
          placeholder="Zoek opdrachten, klanten, dossiers, voertuigen... (Esc om te sluiten)"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onInputKeyDown}
          aria-label="Zoekterm"
        />
        <div className="cmdk-results">
          {term.length < 2 && (
            <>
              {listRecentItems().length > 0 && <div className="cmdk-category">Recent bezocht</div>}
              {listRecentItems().map((item) => (
                <button
                  key={item.route}
                  type="button"
                  className="cmdk-hit"
                  onClick={() => {
                    close()
                    navigate(item.route)
                  }}
                >
                  <span className="cmdk-hit-title">{item.title}</span>
                  <span className="cmdk-hit-subtitle">{item.category}</span>
                </button>
              ))}
              <p className="cmdk-hint">Typ minstens 2 tekens om te zoeken.</p>
            </>
          )}
          {term.length >= 2 && !hits && <p className="cmdk-hint">Zoeken...</p>}
          {hits && hits.length === 0 && <p className="cmdk-hint">Geen resultaten voor "{term}".</p>}
          {[...grouped.entries()].map(([category, categoryHits]) => (
            <div key={category}>
              <div className="cmdk-category">{category}</div>
              {categoryHits.map((hit) => {
                const index = flat.indexOf(hit)
                return (
                  <button
                    key={`${hit.category}-${hit.id}`}
                    type="button"
                    className={index === activeIndex ? 'cmdk-hit cmdk-hit-active' : 'cmdk-hit'}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => openHit(hit)}
                  >
                    <span className="cmdk-hit-title">{hit.title}</span>
                    {hit.subtitle && <span className="cmdk-hit-subtitle">{hit.subtitle}</span>}
                  </button>
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
