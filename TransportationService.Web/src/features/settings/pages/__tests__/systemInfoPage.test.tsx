import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { SystemInfoPage } from '../SystemInfoPage'
import { formatDateTime } from '../../../../utils/dates'
import type { BackupDto, BackupListResult, SystemInfo } from '../../api/systemInfoApi'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => auth.permissions.has(code) }),
}))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  systemInfo: null as unknown as SystemInfo,
  backupList: null as unknown as BackupListResult,
  create: vi.fn(),
  restore: vi.fn(),
  remove: vi.fn(),
  download: vi.fn(),
}))

vi.mock('../../api/systemInfoApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/systemInfoApi')>()),
  getSystemInfo: () => Promise.resolve(api.systemInfo),
  listBackups: () => Promise.resolve(api.backupList),
  createBackup: api.create,
  restoreBackup: api.restore,
  deleteBackup: api.remove,
  downloadBackup: api.download,
}))

function systemInfo(overrides: Partial<SystemInfo> = {}): SystemInfo {
  return {
    version: '2.4.1',
    buildCommit: 'a1b2c3d4e5f6',
    environment: 'Production',
    lastDeployedAtUtc: '2026-08-10T09:30:00Z',
    apiStatus: 'Healthy',
    databaseStatus: 'Healthy',
    databaseLatencyMs: 12,
    schemaVersion: '20260810_AddInventoryStock',
    pendingMigrations: 0,
    deploymentRef: 'deploy-2026-08-10',
    ...overrides,
  }
}

function backup(overrides: Partial<BackupDto> = {}): BackupDto {
  return {
    id: 'bak-1',
    fileName: 'backup-20260812-101500.dump',
    createdAtUtc: '2026-08-12T10:15:00Z',
    sizeBytes: 5 * 1024 * 1024,
    source: 'Manual',
    status: 'Completed',
    schemaVersion: '20260810_AddInventoryStock',
    note: 'Voor de upgrade',
    restoredAtUtc: null,
    protected: false,
    readOnly: false,
    ...overrides,
  }
}

function backupList(overrides: Partial<BackupListResult> = {}): BackupListResult {
  return {
    backups: [backup()],
    automaticRetentionDays: 14,
    preRestoreRetentionDays: 7,
    automaticEnabled: true,
    storageStatus: 'Beschikbaar',
    storageAvailable: true,
    ...overrides,
  }
}

function renderPage(initialEntry = '/settings/system') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <SystemInfoPage />
    </MemoryRouter>,
  )
}

describe('SystemInfoPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set()
    api.systemInfo = systemInfo()
    api.backupList = backupList()
  })

  it('toont een rechtenmelding zonder system_info.view en backups.view', () => {
    renderPage()
    expect(screen.getByText('Je hebt geen rechten om de systeeminformatie te bekijken.')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('rendert de systeeminformatie uit de api (omgeving, versie, build, statusbadges)', async () => {
    auth.permissions = new Set(['system_info.view'])
    api.systemInfo = systemInfo({ pendingMigrations: 3 })
    renderPage()

    expect(await screen.findByText('Production')).toBeInTheDocument()
    expect(screen.getByText('2.4.1')).toBeInTheDocument()
    expect(screen.getByText('a1b2c3d4e5f6')).toBeInTheDocument()
    expect(screen.getByText(formatDateTime('2026-08-10T09:30:00Z'))).toBeInTheDocument()
    expect(screen.getByText('deploy-2026-08-10')).toBeInTheDocument()
    // VITE_BUILD_COMMIT ontbreekt in de testomgeving → frontend toont "lokale build".
    expect(screen.getByText('lokale build')).toBeInTheDocument()
    // API + database gezond: twee badges met tekst (niet enkel kleur).
    expect(screen.getAllByText('In orde')).toHaveLength(2)
    expect(screen.getByText('12 ms')).toBeInTheDocument()
    expect(screen.getByText('20260810_AddInventoryStock')).toBeInTheDocument()
    expect(screen.getByText('3 openstaand')).toBeInTheDocument()
    // Zonder backups.view is er geen Back-ups-tab.
    expect(screen.queryByRole('tab', { name: 'Back-ups' })).not.toBeInTheDocument()
  })

  it('rendert de back-uplijst met retentiebanner en maakt een back-up aan via de knop', async () => {
    auth.permissions = new Set(['backups.view', 'backups.create'])
    api.create.mockResolvedValue(backup({ id: 'bak-2' }))
    renderPage()

    expect(
      await screen.findByText(
        'Automatische back-ups: 14 dagen · Veiligheidsback-ups (pre-restore): 7 dagen · Handmatige back-ups worden nooit automatisch verwijderd.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Manueel')).toBeInTheDocument()
    expect(screen.getByText('Voltooid')).toBeInTheDocument()
    expect(screen.getByText('5 MB')).toBeInTheDocument()
    expect(screen.getByText('Voor de upgrade')).toBeInTheDocument()
    expect(screen.getByText(formatDateTime('2026-08-12T10:15:00Z'))).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Nieuwe back-up maken' }))
    const dialog = screen.getByRole('dialog', { name: 'Nieuwe back-up maken' })
    await user.type(within(dialog).getByLabelText('Notitie (optioneel)'), 'Voor de migratie')
    await user.click(within(dialog).getByRole('button', { name: 'Back-up maken' }))

    expect(api.create).toHaveBeenCalledWith('Voor de migratie')
    // Na een geslaagde aanmaak sluit de modal en wordt de lijst opnieuw geladen.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('terugzetten: knop blijft uit tot de exacte bestandsnaam is getypt en post dan de bevestiging', async () => {
    auth.permissions = new Set(['backups.view', 'backups.restore'])
    api.restore.mockResolvedValue({
      safetyBackupId: 'safety-1',
      safetyBackupFileName: 'pre-restore-20260812.dump',
      databaseStatus: 'Healthy',
      advice: 'Controleer na het terugzetten de recentste dossiers.',
    })
    renderPage()

    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Terugzetten' }))

    const dialog = screen.getByRole('dialog', { name: 'Back-up terugzetten' })
    const confirmButton = within(dialog).getByRole('button', { name: 'Terugzetten' })
    const input = within(dialog).getByLabelText('Typ de exacte bestandsnaam om te bevestigen')

    expect(confirmButton).toBeDisabled()
    await user.type(input, 'verkeerde-naam.dump')
    expect(confirmButton).toBeDisabled()

    await user.clear(input)
    await user.type(input, 'backup-20260812-101500.dump')
    expect(confirmButton).toBeEnabled()

    await user.click(confirmButton)
    expect(api.restore).toHaveBeenCalledWith('bak-1', 'backup-20260812-101500.dump')

    // Resultaatscherm: veiligheidsback-up + advies.
    expect(await screen.findByText('pre-restore-20260812.dump')).toBeInTheDocument()
    expect(screen.getByText('Controleer na het terugzetten de recentste dossiers.')).toBeInTheDocument()
  })

  it('readOnly deploy-dumps krijgen geen bruikbare acties en protected blokkeert verwijderen', async () => {
    auth.permissions = new Set(['backups.view', 'backups.download', 'backups.delete', 'backups.restore'])
    api.backupList = backupList({
      backups: [
        backup({
          id: '00000000-0000-0000-0000-000000000000',
          fileName: 'deploy-dump.dump',
          source: 'PreDeployment',
          readOnly: true,
        }),
        backup({ id: 'bak-3', fileName: 'safety.dump', source: 'PreRestore', protected: true }),
      ],
    })
    renderPage()

    expect(await screen.findByText('Pre-deployment')).toBeInTheDocument()
    const rows = screen.getAllByRole('row').slice(1) // koprij overslaan

    // Rij 1: readOnly → alle acties uitgeschakeld.
    const readOnlyRow = within(rows[0])
    expect(readOnlyRow.getByRole('button', { name: 'Downloaden' })).toBeDisabled()
    expect(readOnlyRow.getByRole('button', { name: 'Terugzetten' })).toBeDisabled()
    expect(readOnlyRow.getByRole('button', { name: 'Verwijderen' })).toBeDisabled()

    // Rij 2: protected → enkel verwijderen geblokkeerd.
    const protectedRow = within(rows[1])
    expect(protectedRow.getByRole('button', { name: 'Downloaden' })).toBeEnabled()
    expect(protectedRow.getByRole('button', { name: 'Terugzetten' })).toBeEnabled()
    expect(protectedRow.getByRole('button', { name: 'Verwijderen' })).toBeDisabled()
  })
})
