import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { Breadcrumbs } from '../Breadcrumbs'
import breadcrumbsCss from '../Breadcrumbs.css?raw'
import pageHeaderCss from '../PageHeader.css?raw'

// Both stylesheets are listed under `test.css.include` in vitest.config.ts (else `?raw` is empty).

const LONG_NAME = 'Internationale Transport- en Expeditieonderneming Van Den Berghe & Zonen Logistics Group NV'

function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '')
  return source.match(new RegExp(`(?:^|\\}|\\{)\\s*${escaped}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

function renderTrail() {
  return render(
    <MemoryRouter>
      <Breadcrumbs
        items={[
          { label: 'Dossiers', to: '/dossiers' },
          { label: 'DOS-0022', to: '/dossiers/d-22' },
          { label: LONG_NAME },
        ]}
      />
    </MemoryRouter>,
  )
}

describe('Breadcrumbs — B54 long crumbs', () => {
  it('keeps the full label in the DOM and exposes it via title on a long crumb', () => {
    expect(LONG_NAME.length).toBeGreaterThanOrEqual(90)
    renderTrail()
    const current = screen.getByText(LONG_NAME)
    expect(current).toHaveAttribute('aria-current', 'page')
    expect(current.textContent).toBe(LONG_NAME)
    expect(current).toHaveAttribute('title', LONG_NAME)
    expect(current).toHaveClass('ui-breadcrumbs-label')
    expect(current.closest('li')).toHaveClass('ui-breadcrumbs-item--current')
  })

  it('never marks an identifier crumb as truncatable and gives it no tooltip', () => {
    renderTrail()
    const id = screen.getByRole('link', { name: 'DOS-0022' })
    expect(id.closest('li')).toHaveClass('ui-breadcrumbs-item--id')
    expect(id).not.toHaveAttribute('title')
    expect(screen.getByText(LONG_NAME).closest('li')).not.toHaveClass('ui-breadcrumbs-item--id')
  })

  it('honours an explicit identifier flag over the heuristic', () => {
    render(
      <MemoryRouter>
        <Breadcrumbs items={[{ label: 'Rit 2026 week 41 nummer 0007', identifier: true }, { label: 'X', identifier: false }]} />
      </MemoryRouter>,
    )
    expect(screen.getByText('Rit 2026 week 41 nummer 0007').closest('li')).toHaveClass('ui-breadcrumbs-item--id')
    expect(screen.getByText('X').closest('li')).not.toHaveClass('ui-breadcrumbs-item--id')
  })

  it('stylesheet contract: one line, ellipsis per crumb, last shrinks first, identifiers never', () => {
    // jsdom cannot measure layout, so the CSS contract is guarded instead.
    const list = ruleBody(breadcrumbsCss, '.ui-breadcrumbs ol')
    expect(list).toMatch(/flex-wrap:\s*nowrap/)
    expect(list).toMatch(/white-space:\s*nowrap/)
    const label = ruleBody(breadcrumbsCss, '.ui-breadcrumbs-label')
    expect(label).toMatch(/max-width:\s*28ch/)
    expect(label).toMatch(/text-overflow:\s*ellipsis/)
    expect(label).toMatch(/overflow:\s*hidden/)
    expect(ruleBody(breadcrumbsCss, '.ui-breadcrumbs li.ui-breadcrumbs-item--current')).toMatch(/flex-shrink:\s*[2-9]/)
    expect(ruleBody(breadcrumbsCss, '.ui-breadcrumbs li.ui-breadcrumbs-item--id')).toMatch(/flex-shrink:\s*0/)
    expect(ruleBody(breadcrumbsCss, '.ui-breadcrumbs li.ui-breadcrumbs-item--id .ui-breadcrumbs-label')).toMatch(/max-width:\s*none/)
  })

  it('stylesheet contract: the page title wraps (breaks unbroken strings) and is never ellipsized', () => {
    const title = ruleBody(pageHeaderCss, '.page-header h2')
    expect(title).toMatch(/overflow-wrap:\s*anywhere/)
    expect(title).not.toMatch(/text-overflow|white-space:\s*nowrap/)
  })
})
