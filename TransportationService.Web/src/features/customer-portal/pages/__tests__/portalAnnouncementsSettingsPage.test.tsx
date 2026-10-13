import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PortalAnnouncementsSettingsPage } from '../PortalAnnouncementsSettingsPage'
import type { PortalAnnouncement } from '../../api/portalAnnouncementsApi'

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const announcements = vi.hoisted(() => ({ value: [] as PortalAnnouncement[] }))
const createSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/portalAnnouncementsApi', () => ({
  listPortalAnnouncementsAdmin: () => Promise.resolve(announcements.value),
  createPortalAnnouncement: createSpy,
  updatePortalAnnouncement: vi.fn(),
  deletePortalAnnouncement: vi.fn(),
}))

describe('PortalAnnouncementsSettingsPage', () => {
  beforeEach(() => {
    announcements.value = []
    createSpy.mockReset().mockResolvedValue({
      id: 'a1', title: 'Onderhoud', body: 'Vannacht onderhoud.', activeFrom: null, activeUntil: null, isActive: true,
    })
    toast.showSuccess.mockReset()
  })

  it('lists existing announcements', async () => {
    announcements.value = [
      { id: 'a1', title: 'Bestaande mededeling', body: 'Body', activeFrom: null, activeUntil: null, isActive: true },
    ]
    render(<PortalAnnouncementsSettingsPage />)

    expect(await screen.findByText('Bestaande mededeling')).toBeInTheDocument()
  })

  it('creates a new announcement via the modal form', async () => {
    const user = userEvent.setup()
    render(<PortalAnnouncementsSettingsPage />)

    await waitFor(() => expect(screen.getByText('Nog geen mededelingen.')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Nieuwe mededeling' }))
    await user.type(screen.getByLabelText(/^Titel/), 'Onderhoud')
    await user.type(screen.getByLabelText(/^Inhoud/), 'Vannacht onderhoud.')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(createSpy).toHaveBeenCalledWith({
        title: 'Onderhoud',
        body: 'Vannacht onderhoud.',
        activeFrom: null,
        activeUntil: null,
        isActive: true,
      }),
    )
    expect(toast.showSuccess).toHaveBeenCalledWith('Mededeling aangemaakt.')
  })
})
