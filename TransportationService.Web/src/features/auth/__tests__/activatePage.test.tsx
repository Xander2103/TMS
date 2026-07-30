import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ActivatePage } from '../PasswordFlowPages'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

// jsdom does not implement scrollIntoView, which ValidationSummary calls when it has content.
Element.prototype.scrollIntoView = vi.fn()

const postJson = vi.hoisted(() => vi.fn(() => Promise.resolve(undefined)))

vi.mock('../../../api/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../../api/apiClient')>('../../../api/apiClient')
  return {
    ...actual,
    apiClient: { ...actual.apiClient, postJson },
  }
})

function renderActivatePage(path = '/activeren?token=abc123&email=klant%40haven.be') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/activeren" element={<ActivatePage />} />
        <Route path="/login" element={<div>Loginpagina</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ActivatePage', () => {
  beforeEach(() => {
    postJson.mockClear()
  })

  it('rejects mismatched passwords without calling the API', async () => {
    const user = userEvent.setup()
    renderActivatePage()

    await user.type(screen.getByLabelText(/^Wachtwoord/), 'Passw0rd!')
    await user.type(screen.getByLabelText(/Bevestig wachtwoord/), 'Anders123!')
    await user.click(screen.getByRole('button', { name: 'Account activeren' }))

    expect(await screen.findByText('De wachtwoorden komen niet overeen.')).toBeInTheDocument()
    expect(postJson).not.toHaveBeenCalled()
  })

  it('submits the token and new password, then redirects to /login on success', async () => {
    const user = userEvent.setup()
    renderActivatePage()

    await user.type(screen.getByLabelText(/^Wachtwoord/), 'Passw0rd!')
    await user.type(screen.getByLabelText(/Bevestig wachtwoord/), 'Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Account activeren' }))

    await waitFor(() =>
      expect(postJson).toHaveBeenCalledWith('/api/auth/reset-password', {
        token: 'abc123',
        newPassword: 'Passw0rd!',
      }),
    )
    await waitFor(() => expect(screen.getByText('Loginpagina')).toBeInTheDocument())
  })

  it('refuses to submit when the link has no token', async () => {
    const user = userEvent.setup()
    renderActivatePage('/activeren')

    await user.type(screen.getByLabelText(/^Wachtwoord/), 'Passw0rd!')
    await user.type(screen.getByLabelText(/Bevestig wachtwoord/), 'Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Account activeren' }))

    expect(await screen.findByText('Deze activeringslink is ongeldig.')).toBeInTheDocument()
    expect(postJson).not.toHaveBeenCalled()
  })
})
