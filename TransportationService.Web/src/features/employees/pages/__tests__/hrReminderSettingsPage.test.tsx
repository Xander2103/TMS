import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { HrReminderSettingsPage } from '../HrReminderSettingsPage'
import type { HrReminderSettings } from '../../api/hrReminderSettingsApi'
import * as api from '../../api/hrReminderSettingsApi'

const auth = vi.hoisted(() => ({ permissions: ['hr_settings.manage'] }))
const toasts = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.includes(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toasts.showSuccess, showError: toasts.showError }),
}))

class FakeApiError extends Error {
  fieldErrors: Record<string, string[]>
  constructor(message: string, fieldErrors: Record<string, string[]>) {
    super(message)
    this.fieldErrors = fieldErrors
  }
}

function settings(overrides: Partial<HrReminderSettings> = {}): HrReminderSettings {
  return {
    birthdayEnabled: true,
    birthdayDaysBefore: 0,
    birthdayEmailEnabled: false,
    birthdayRecipientRoleCodes: 'hr',
    seniorityEnabled: true,
    seniorityMilestoneYears: '1,10,15,20,25,30',
    seniorityWarningDays: 60,
    seniorityEmployeeEmailEnabled: true,
    employmentEndEnabled: true,
    employmentEndDaysBefore: 7,
    dossierRemindersEnabled: true,
    dossierReminderDays: 7,
    dossierEscalationDays: 30,
    ...overrides,
  }
}

function renderPage() {
  const router = createMemoryRouter(
    [{ path: '/settings/hr-reminders', element: <HrReminderSettingsPage /> }],
    { initialEntries: ['/settings/hr-reminders'] },
  )
  render(<RouterProvider router={router} />)
  return router
}

beforeEach(() => {
  vi.restoreAllMocks()
  toasts.showSuccess.mockClear()
  toasts.showError.mockClear()
  auth.permissions = ['hr_settings.manage']
  vi.spyOn(api, 'getHrReminderSettings').mockResolvedValue(settings())
  vi.spyOn(api, 'updateHrReminderSettings').mockImplementation((input) => Promise.resolve({ ...input }))
})

describe('HrReminderSettingsPage', () => {
  it('renders the dossier follow-up fields loaded from the API', async () => {
    renderPage()

    expect(await screen.findByLabelText('Opvolging onvolledige dossiers actief')).toBeChecked()
    expect(screen.getByLabelText('Eerste melding na (dagen)')).toHaveValue(7)
    expect(screen.getByLabelText('Escalatie na (dagen)')).toHaveValue(30)
  })

  it('sends the edited dossier fields in the PUT payload', async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByLabelText('Opvolging onvolledige dossiers actief')

    const reminderDaysInput = screen.getByLabelText('Eerste melding na (dagen)')
    await user.clear(reminderDaysInput)
    await user.type(reminderDaysInput, '10')

    const escalationDaysInput = screen.getByLabelText('Escalatie na (dagen)')
    await user.clear(escalationDaysInput)
    await user.type(escalationDaysInput, '45')

    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    await waitFor(() =>
      expect(api.updateHrReminderSettings).toHaveBeenCalledWith(
        expect.objectContaining({
          dossierRemindersEnabled: true,
          dossierReminderDays: 10,
          dossierEscalationDays: 45,
        }),
      ),
    )
    await waitFor(() => expect(toasts.showSuccess).toHaveBeenCalled())
  })

  it('disables the day inputs when the toggle is off', async () => {
    vi.spyOn(api, 'getHrReminderSettings').mockResolvedValue(settings({ dossierRemindersEnabled: false }))
    renderPage()

    expect(await screen.findByLabelText('Opvolging onvolledige dossiers actief')).not.toBeChecked()
    expect(screen.getByLabelText('Eerste melding na (dagen)')).toBeDisabled()
    expect(screen.getByLabelText('Escalatie na (dagen)')).toBeDisabled()
  })

  it('shows the backend field error on the escalation field', async () => {
    const user = userEvent.setup()
    const error = new FakeApiError('Escalatie moet later vallen dan de eerste melding.', {
      dossierEscalationDays: ['Escalatie moet later vallen dan de eerste melding.'],
    })
    vi.spyOn(api, 'updateHrReminderSettings').mockRejectedValue(error)
    renderPage()

    const escalationDaysInput = await screen.findByLabelText('Escalatie na (dagen)')
    await user.clear(escalationDaysInput)
    await user.type(escalationDaysInput, '3')
    await user.click(screen.getByRole('button', { name: 'Opslaan' }))

    expect(await screen.findByText('Escalatie moet later vallen dan de eerste melding.')).toBeInTheDocument()
  })

  it('disables all inputs without hr_settings.manage', async () => {
    auth.permissions = []
    renderPage()

    expect(await screen.findByLabelText('Opvolging onvolledige dossiers actief')).toBeDisabled()
    expect(screen.getByLabelText('Eerste melding na (dagen)')).toBeDisabled()
    expect(screen.getByLabelText('Escalatie na (dagen)')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })
})
