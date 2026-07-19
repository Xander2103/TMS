import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { TabPanel, Tabs } from '../Tabs'

function TabsHarness() {
  const [active, setActive] = useState('one')
  return (
    <>
      <Tabs
        tabs={[
          { id: 'one', label: 'Eén' },
          { id: 'two', label: 'Twee', badge: 4 },
        ]}
        activeId={active}
        onChange={setActive}
      />
      {active === 'one' && <TabPanel tabId="one">Inhoud één</TabPanel>}
      {active === 'two' && <TabPanel tabId="two">Inhoud twee</TabPanel>}
    </>
  )
}

describe('Tabs', () => {
  it('switches panels on click', async () => {
    const user = userEvent.setup()
    render(<TabsHarness />)

    expect(screen.getByText('Inhoud één')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: /Twee/ }))
    expect(screen.getByText('Inhoud twee')).toBeInTheDocument()
    expect(screen.queryByText('Inhoud één')).not.toBeInTheDocument()
  })

  it('supports arrow-key navigation', async () => {
    const user = userEvent.setup()
    render(<TabsHarness />)

    screen.getByRole('tab', { name: /Eén/ }).focus()
    await user.keyboard('{ArrowRight}')
    expect(screen.getByText('Inhoud twee')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Twee/ })).toHaveFocus()
  })
})
