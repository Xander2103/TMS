import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerImportDialog } from '../components/CustomerImportDialog'
import { commitCustomerImport, previewCustomerImport } from '../api/customerImportApi'
import type { CustomerImportCommit, CustomerImportPreview } from '../types'

vi.mock('../api/customerImportApi', () => ({
  downloadCustomerImportTemplate: vi.fn(),
  previewCustomerImport: vi.fn(),
  commitCustomerImport: vi.fn(),
  downloadCustomerImportErrorWorkbook: vi.fn(),
}))

const preview: CustomerImportPreview = {
  totalRows: 3,
  creates: 1,
  updates: 1,
  errors: 1,
  rows: [
    { rowNumber: 2, action: 'Create', customerNumber: 'K-0001', name: 'Acme BV', messages: [] },
    { rowNumber: 3, action: 'Update', customerNumber: 'K-0002', name: 'Beta NV', messages: [] },
    { rowNumber: 4, action: 'Error', customerNumber: null, name: 'Gamma', messages: ['Naam is verplicht.', 'Ongeldig BTW-nummer.'] },
  ],
}

const commit: CustomerImportCommit = {
  created: 1,
  updated: 1,
  failed: 0,
  committed: true,
  rows: preview.rows.slice(0, 2),
  errorWorkbookBase64: null,
}

function renderDialog() {
  const onClose = vi.fn()
  const onImported = vi.fn()
  render(<CustomerImportDialog onClose={onClose} onImported={onImported} />)
  return { onClose, onImported }
}

async function chooseFileAndPreview(): Promise<File> {
  const file = new File(['dummy'], 'klanten.xlsx', {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  await userEvent.upload(screen.getByLabelText('Bestand (.xlsx)'), file)
  await userEvent.click(screen.getByRole('button', { name: 'Voorbeeld' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Importeren' })).toBeEnabled())
  return file
}

describe('CustomerImportDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('disables the import button until a file was chosen and a preview ran', async () => {
    vi.mocked(previewCustomerImport).mockResolvedValue(preview)

    renderDialog()

    const importButton = screen.getByRole('button', { name: 'Importeren' })
    expect(importButton).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Voorbeeld' })).toBeDisabled()

    await chooseFileAndPreview()

    await waitFor(() => expect(importButton).toBeEnabled())
  })

  it('renders the preview summary counts and error row messages', async () => {
    vi.mocked(previewCustomerImport).mockResolvedValue(preview)

    renderDialog()
    const file = await chooseFileAndPreview()

    expect(previewCustomerImport).toHaveBeenCalledWith(file)
    expect(await screen.findByText('Naam is verplicht.; Ongeldig BTW-nummer.')).toBeInTheDocument()
    const summary = document.querySelector('.customer-import-summary')
    expect(summary).toHaveTextContent('3 rijen · 1 nieuw · 1 bijwerken · 1 fouten')
    expect(screen.getByText('Fout')).toBeInTheDocument()
    expect(screen.getByText('Acme BV')).toBeInTheDocument()
  })

  it('commits with the chosen flags and reports the result', async () => {
    vi.mocked(previewCustomerImport).mockResolvedValue(preview)
    vi.mocked(commitCustomerImport).mockResolvedValue(commit)

    const { onImported } = renderDialog()
    const file = await chooseFileAndPreview()

    await userEvent.click(screen.getByLabelText('Bestaande klanten bijwerken'))
    await userEvent.click(screen.getByLabelText('Alles-of-niets'))
    await userEvent.click(screen.getByRole('button', { name: 'Importeren' }))

    expect(commitCustomerImport).toHaveBeenCalledWith(file, { allOrNothing: false, allowUpdates: true })
    expect(await screen.findByText('Import klaar: 1 aangemaakt, 1 bijgewerkt, 0 fouten.')).toBeInTheDocument()
    expect(onImported).toHaveBeenCalledTimes(1)
  })

  it('offers the error workbook and keeps the list untouched when all-or-nothing aborts', async () => {
    vi.mocked(previewCustomerImport).mockResolvedValue(preview)
    vi.mocked(commitCustomerImport).mockResolvedValue({
      created: 0,
      updated: 0,
      failed: 1,
      committed: false,
      rows: [preview.rows[2]],
      errorWorkbookBase64: 'QUJD',
    })

    const { onImported } = renderDialog()
    await chooseFileAndPreview()
    await userEvent.click(screen.getByRole('button', { name: 'Importeren' }))

    expect(await screen.findByText('Import afgebroken: het bestand bevat fouten (alles-of-niets).')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Foutenbestand downloaden' })).toBeInTheDocument()
    expect(onImported).not.toHaveBeenCalled()
  })
})
