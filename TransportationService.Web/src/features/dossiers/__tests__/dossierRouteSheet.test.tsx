import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouteSection } from '../../transport-orders/components/sections/RouteSection'
import { emptyStop, type StopFormRow } from '../../transport-orders/components/sections/orderFormState'
import routeEditorCss from '../components/route-editor.css?raw'

function renderSheet(stops: StopFormRow[], props: Partial<Parameters<typeof RouteSection>[0]> = {}) {
  const handlers = { moveStop: vi.fn(), onRemoveStop: vi.fn() }
  render(
    <RouteSection
      stops={stops}
      customerId="c-1"
      saving={false}
      locationHours={{}}
      errors={{}}
      onAddStop={vi.fn()}
      setStop={vi.fn()}
      onRequestRefresh={vi.fn()}
      sheet
      hideHeader
      {...handlers}
      {...props}
    />,
  )
  return handlers
}

const cards = () => Array.from(document.querySelectorAll<HTMLElement>('fieldset.tof-stop'))

describe('dossier route sheet: stop card', () => {
  it('names the stop by number and type and groups the fields under three sections', () => {
    renderSheet([emptyStop('Loading'), emptyStop('Unloading')])
    expect(cards().map((card) => card.querySelector('legend')?.textContent?.trim())).toEqual(['1. Laden', '2. Lossen'])
    const titles = Array.from(cards()[0].querySelectorAll('h4')).map((h) => h.textContent)
    expect(titles).toEqual(['Adres zoeken', 'Adresgegevens', 'Planning'])
    // Each field sits in its own section; the search field keeps its accessible name.
    const [search, address, planning] = Array.from(cards()[0].querySelectorAll<HTMLElement>('.tof-sheet-section'))
    expect(within(search).getByLabelText('Adres zoeken')).toBeInTheDocument()
    expect(within(address).getByLabelText('Straat + nr')).toBeInTheDocument()
    expect(within(planning).getByLabelText('Laaddatum')).toBeInTheDocument()
    expect(within(planning).getByLabelText('Tijdseis')).toBeInTheDocument()
  })

  it('keeps move and remove on the card and Geavanceerd collapsed', async () => {
    const user = userEvent.setup()
    const { moveStop, onRemoveStop } = renderSheet([emptyStop('Loading'), emptyStop('Unloading')])
    const first = within(cards()[0])
    expect(first.getByRole('button', { name: /omhoog/i })).toBeDisabled()
    await user.click(first.getByRole('button', { name: /omlaag/i }))
    expect(moveStop).toHaveBeenCalledWith(0, 1)
    await user.click(first.getByRole('button', { name: /verwijder/i }))
    expect(onRemoveStop).toHaveBeenCalledTimes(1)
    expect(cards()[0].querySelector('details.tof-stop-extended')).not.toHaveAttribute('open')
  })

  it('leaves the other hosts of the stop editor without the sheet markup', () => {
    renderSheet([emptyStop('Loading')], { sheet: false })
    expect(document.querySelector('.tof-sheet-section')).toBeNull()
    expect(cards()[0]).not.toHaveClass('tof-stop-sheet')
    expect(screen.getAllByText('Optioneel.').length).toBeGreaterThan(0)
  })
})

describe('dossier route sheet: action bar', () => {
  const css = routeEditorCss.replace(/\/\*[\s\S]*?\*\//g, '')

  it('sticks to the bottom, under the subnav (z-index 5), and fields scroll clear of it', () => {
    const bar = css.match(/\.dossier-route-actions \{([^}]*)\}/)?.[1] ?? ''
    expect(bar).toMatch(/position:\s*sticky/)
    expect(bar).toMatch(/bottom:\s*0/)
    expect(bar).toMatch(/z-index:\s*4/)
    expect(css).toMatch(/scroll-margin-bottom:\s*96px/)
  })
})
