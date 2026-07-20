import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { getReportCatalog } from '../api/reportsApi'
import { REPORT_CATEGORY_META, REPORT_CATEGORY_ORDER, type ReportCatalogEntry } from '../types'
import './reports.css'

/**
 * Rapportcentrum: één catalogus over bestaande exports en rapportpagina's. De backend
 * levert alleen rapporten waarvoor de gebruiker de permissie heeft; deze pagina toont
 * uitsluitend metadata en laadt nooit rapportdata vooraf.
 */
export function ReportsPage() {
  const navigate = useNavigate()
  const [reports, setReports] = useState<ReportCatalogEntry[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [activeCategory, setActiveCategory] = useState<string | null>(null)
  const listRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let mounted = true
    getReportCatalog()
      .then((data) => {
        if (mounted) setReports(data)
      })
      .catch(() => {
        if (mounted) setLoadError('Het rapportcentrum kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (loadError) return <ErrorState message={loadError} />
  if (!reports) return <LoadingState message="Rapportcentrum laden..." />

  const categories = REPORT_CATEGORY_ORDER
    .filter((category) => reports.some((report) => report.category === category))
    .concat(
      [...new Set(reports.map((report) => report.category))].filter(
        (category) => !REPORT_CATEGORY_ORDER.includes(category),
      ),
    )
  const selected = activeCategory && categories.includes(activeCategory) ? activeCategory : null
  const visibleReports = selected ? reports.filter((report) => report.category === selected) : []

  function openReport(report: ReportCatalogEntry) {
    if (report.kind === 'Page' && report.route) {
      navigate(report.route)
    } else if (report.kind === 'Export') {
      navigate(`/reports/${report.id}`)
    }
  }

  return (
    <div>
      <PageHeader
        title="Rapportcentrum"
        subtitle="Alle rapporten en exports op één plek; je ziet alleen wat je mag openen."
      />

      <div className="reports-categories">
        {categories.map((category) => {
          const meta = REPORT_CATEGORY_META[category] ?? { icon: '📄', description: '' }
          const count = reports.filter((report) => report.category === category).length
          return (
            <div key={category} className={`reports-card ${selected === category ? 'reports-card-active' : ''}`}>
              <span className="reports-card-icon" aria-hidden="true">
                {meta.icon}
              </span>
              <h2>{category}</h2>
              <p>{meta.description}</p>
              <span className="reports-card-count">
                {count} {count === 1 ? 'rapport' : 'rapporten'}
              </span>
              <Button
                variant="secondary"
                onClick={() => {
                  setActiveCategory(category)
                  listRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
                }}
              >
                Openen
              </Button>
            </div>
          )
        })}
      </div>

      <div ref={listRef}>
        {selected && (
          <section className="reports-list">
            <h2>{selected}</h2>
            <ul>
              {visibleReports.map((report) => (
                <li key={report.id} className="reports-row">
                  <div className="reports-row-main">
                    <span className="reports-row-title">
                      {report.title}
                      {report.kind === 'ComingSoon' && <Badge tone="neutral">Binnenkort</Badge>}
                      {report.kind === 'Export' && report.fileType && (
                        <Badge tone="info">{report.fileType.toUpperCase()}</Badge>
                      )}
                    </span>
                    <span className="reports-row-description">{report.description}</span>
                  </div>
                  {report.kind !== 'ComingSoon' ? (
                    <Button variant="secondary" onClick={() => openReport(report)}>
                      Openen
                    </Button>
                  ) : (
                    <span className="reports-row-soon">Nog niet beschikbaar</span>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}
        {!selected && <p className="placeholder-text">Kies een categorie om de rapporten te bekijken.</p>}
      </div>
    </div>
  )
}
