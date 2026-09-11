import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PanelHeader } from '../PanelHeader'

describe('PanelHeader', () => {
  it('renders title, description, filters and actions in one toolbar', () => {
    render(
      <PanelHeader title="Contactpersonen" description="Beheer contactpersonen." actions={<button>+ Contact toevoegen</button>}>
        <label>
          Type <select aria-label="Type" />
        </label>
      </PanelHeader>,
    )
    expect(screen.getByRole('heading', { level: 3, name: 'Contactpersonen' })).toBeInTheDocument()
    expect(screen.getByText('Beheer contactpersonen.')).toBeInTheDocument()
    const toolbar = screen.getByRole('button', { name: '+ Contact toevoegen' }).closest('.ui-panel-header-toolbar')
    expect(toolbar).not.toBeNull()
    expect(toolbar!.contains(screen.getByLabelText('Type'))).toBe(true)
  })

  it('renders no heading when the host section already provides the title', () => {
    render(<PanelHeader actions={<button>Actie</button>} />)
    expect(screen.queryByRole('heading')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Actie' })).toBeInTheDocument()
  })
})
