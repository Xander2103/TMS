import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { DossierDetailPage } from '../pages/DossierDetailPage'
import { ActivityCard } from '../components/ActivityCard'
import type { DossierDocument } from '../api/dossierDocumentsApi'
import { dossierActivity, dossierDetail, orderDetail } from './fixtures'

/**
 * Product acceptance sprint 2026-10-07 (spec §21/§22/§20) — the Documenten tab through the REAL
 * route: `/dossiers/:id/documenten` → DossierDocumentsSection → DossierDocumentsWorkspace → the
 * dossier documents API → the rendered levels. One list feeds every level; a general document is
 * shown on an order level read-only (never as that order's own document, never copied); every
 * write goes through the existing order-documents routes and is re-read from the server.
 */

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.has(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.has(code)),
  }),
}))
const toast = vi.hoisted(() => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

const api = vi.hoisted(() => ({
  getDossier: vi.fn(),
  updateDossier: vi.fn(),
  reopenDossier: vi.fn(),
  cancelDossier: vi.fn(),
  confirmDossier: vi.fn(),
  getDossierConfirmation: vi.fn(),
  linkDossierOrder: vi.fn(),
  unlinkDossierOrder: vi.fn(),
  addDossierRelation: vi.fn(),
  removeDossierRelation: vi.fn(),
  listDossiers: vi.fn(() => Promise.resolve([])),
  addDossierActivity: vi.fn(),
  updateDossierActivity: vi.fn(),
  deleteDossierActivity: vi.fn(),
  createOrderForActivity: vi.fn(),
  changeDossierLegalEntity: vi.fn(),
  setActivityPrice: vi.fn(),
}))
vi.mock('../api/dossiersApi', () => api)
vi.mock('../api/activityTypesApi', () => ({ listActivityTypes: () => Promise.resolve([]) }))
vi.mock('../api/dossierNotesApi', async () => {
  const actual = await vi.importActual<typeof import('../api/dossierNotesApi')>('../api/dossierNotesApi')
  return { ...actual, listDossierNotes: () => Promise.resolve([]) }
})

const documents = vi.hoisted(() => ({
  list: vi.fn<() => Promise<DossierDocument[]>>(),
  create: vi.fn(),
  move: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
}))
vi.mock('../api/dossierDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../api/dossierDocumentsApi')>('../api/dossierDocumentsApi')
  return {
    ...actual,
    listDossierDocuments: documents.list,
    createDossierDocument: documents.create,
    moveDossierDocument: documents.move,
    updateDossierDocument: documents.update,
    deleteDossierDocument: documents.remove,
    // The real helper calls the module-internal update; route it through the same mock.
    setDossierDocumentCustomerVisible: (document: DossierDocument, customerVisible: boolean) =>
      documents.update(document.id, {
        documentType: document.documentType, customTypeName: document.customTypeName, title: document.title,
        issueDate: document.issueDate, notes: document.notes, customerVisible,
      }),
  }
})
const upload = vi.hoisted(() => ({ file: vi.fn() }))
vi.mock('../../transport-orders/api/orderDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../../transport-orders/api/orderDocumentsApi')>('../../transport-orders/api/orderDocumentsApi')
  return { ...actual, uploadOrderDocumentFile: upload.file }
})
vi.mock('../../transport-orders/api/issuedDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../../transport-orders/api/issuedDocumentsApi')>('../../transport-orders/api/issuedDocumentsApi')
  return { ...actual, listIssuedTransportDocuments: () => Promise.resolve([]) }
})
vi.mock('../../transport-orders/api/transportDocumentsApi', async () => {
  const actual = await vi.importActual<typeof import('../../transport-orders/api/transportDocumentsApi')>('../../transport-orders/api/transportDocumentsApi')
  return { ...actual, getOrderDocumentStrategy: () => Promise.resolve({ kind: 'Cmr', usesCustomerDocument: false, noneRequired: false, undecided: false, source: 'BuiltInDefault', reason: '', orderPreference: null, customerStrategy: 'Own' }) }
})
vi.mock('../../transport-orders/api/transportOrdersApi', () => ({
  getTransportOrder: () => Promise.resolve(orderDetail({ status: 'Draft' })),
  updateTransportOrder: vi.fn(),
  saveOrderPriceLines: vi.fn(),
  setOrderOneOffPrice: vi.fn(),
  searchTransportOrders: () => Promise.resolve({ items: [], totalCount: 0 }),
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () => Promise.resolve({ items: [], totalCount: 0 }),
  getCustomer: () => Promise.resolve({ id: 'c-1', isBlocked: false }),
}))
vi.mock('../../users/api/usersApi', () => ({ getUsers: () => Promise.resolve([]) }))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: () => Promise.resolve([]) }))
vi.mock('../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))

const DOCUMENTS_TAB = '/dossiers/d-1/documenten'
const FULL_RIGHTS = ['dossiers.view', 'dossiers.manage', 'orders.view', 'orders.edit']

function document(overrides: Partial<DossierDocument>): DossierDocument {
  return {
    id: 'doc-x',
    dossierId: 'd-1',
    transportOrderId: null,
    orderNumber: null,
    dossierActivityId: null,
    activityLabel: null,
    scope: 'Dossier',
    documentType: 'Other',
    customTypeName: null,
    title: 'Document',
    fileName: 'document.pdf',
    contentType: 'application/pdf',
    issueDate: null,
    notes: null,
    customerVisible: false,
    hasFile: true,
    createdAt: '2026-10-06T09:00:00Z',
    createdByName: 'Piet Planner',
    ...overrides,
  }
}

const generalDoc = () => document({ id: 'doc-general', title: 'Vergunning uitzonderlijk vervoer', documentType: 'Other', customTypeName: 'Vergunning' })
const orderDoc = () => document({ id: 'doc-order', transportOrderId: 'o-1', orderNumber: 'ORD-0001', scope: 'Order', title: 'Getekende CMR', documentType: 'Cmr', fileName: 'cmr.pdf' })
const activityDoc = () => document({ id: 'doc-activity', dossierActivityId: 'a-2', activityLabel: 'Plateau', scope: 'Activity', title: 'Hijsplan plateau', fileName: 'hijsplan.pdf' })

function dossierWithOrder() {
  return dossierDetail({
    orders: [{ linkId: 'l-1', orderId: 'o-1', orderNumber: 'ORD-0001', orderDate: '2026-10-06', status: 'Draft', goodsDescription: 'Pallets', agreedPrice: null }],
    activities: [
      dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' }),
      dossierActivity({ id: 'a-2', sequence: 2, activityTypeCode: 'PLATEAU', activityTypeName: 'Plateau', hasStops: false, supportsGoods: false, allowsDuration: true }),
    ],
    documentCount: 2,
    documentTypes: ['Other', 'Cmr'],
  })
}

function renderPage(initialPath = DOCUMENTS_TAB) {
  const router = createMemoryRouter([{ path: '/dossiers/:id/:section?', element: <DossierDetailPage /> }], { initialEntries: [initialPath] })
  return { ...render(<RouterProvider router={router} />), router }
}

const section = () => document_('sectie-documenten')
function document_(id: string): HTMLElement {
  const element = globalThis.document.getElementById(id)
  if (!element) throw new Error(`#${id} not rendered`)
  return element
}
const tab = (name: RegExp) => screen.getByRole('tab', { name })
const list = (name: string) => within(section()).findByRole('region', { name })

describe('Documenten tab (active route)', () => {
  beforeEach(() => {
    auth.permissions = new Set(FULL_RIGHTS)
    api.getDossier.mockReset().mockResolvedValue(dossierWithOrder())
    documents.list.mockReset().mockResolvedValue([generalDoc(), orderDoc()])
    documents.create.mockReset()
    documents.move.mockReset()
    documents.update.mockReset()
    documents.remove.mockReset()
    upload.file.mockReset()
    toast.showSuccess.mockReset()
    toast.showError.mockReset()
  })

  it('opens on "Dossierdocumenten" with the dossier documents, and the order level shows its OWN documents plus the dossier ones read-only', async () => {
    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByRole('tab', { name: /Dossierdocumenten/ })).toHaveAttribute('aria-selected', 'true')
    expect(documents.list).toHaveBeenCalledWith('d-1')
    const general = await list('Dossierdocumenten')
    expect(await within(general).findByText('Vergunning uitzonderlijk vervoer')).toBeInTheDocument()
    // The order document is NOT a dossier document.
    expect(within(general).queryByText('Getekende CMR')).not.toBeInTheDocument()
    expect(within(section()).getByText('2 documenten in dit dossier')).toBeInTheDocument()

    await user.click(tab(/ORD-0001/))
    const own = await list('Documenten van deze opdracht')
    expect(within(own).getByText('Getekende CMR')).toBeInTheDocument()
    expect(within(own).queryByText('Vergunning uitzonderlijk vervoer')).not.toBeInTheDocument()
    // The dossier document is visible from the order — the same record, read-only, no copy.
    const shared = await list('Dossierdocumenten (gelden ook hier)')
    expect(within(shared).getByText('Vergunning uitzonderlijk vervoer')).toBeInTheDocument()
    expect(within(shared).getByRole('button', { name: 'Downloaden: Vergunning uitzonderlijk vervoer' })).toBeInTheDocument()
    expect(within(shared).queryByRole('button', { name: /Verwijderen: Vergunning/ })).not.toBeInTheDocument()
    expect(within(shared).getByRole('button', { name: 'Beheren bij Dossierdocumenten' })).toBeInTheDocument()
    // Issued transport documents (CMR numbers) of the order live on its level.
    expect(within(section()).getByRole('region', { name: 'Transportdocumenten' })).toBeInTheDocument()
    expect(documents.list).toHaveBeenCalledTimes(1)
  })

  it('?opdracht=<orderId> opens the order level directly', async () => {
    renderPage(`${DOCUMENTS_TAB}?opdracht=o-1`)
    expect(await screen.findByRole('tab', { name: /ORD-0001/ })).toHaveAttribute('aria-selected', 'true')
    expect(within(await list('Documenten van deze opdracht')).getByText('Getekende CMR')).toBeInTheDocument()
  })

  it('uploads an order document: metadata through the dossier route, the file through the order-documents route, then re-reads the list', async () => {
    const user = userEvent.setup()
    const created = document({ id: 'doc-new', transportOrderId: 'o-1', orderNumber: 'ORD-0001', scope: 'Order', title: 'Leverbon', documentType: 'DeliveryNote', hasFile: false, fileName: null })
    documents.create.mockResolvedValue(created)
    upload.file.mockResolvedValue({ ...created, hasAttachment: true })
    documents.list
      .mockResolvedValueOnce([generalDoc(), orderDoc()])
      .mockResolvedValue([generalDoc(), orderDoc(), { ...created, hasFile: true, fileName: 'leverbon.pdf' }])
    renderPage(`${DOCUMENTS_TAB}?opdracht=o-1`)
    await screen.findByRole('tab', { name: /ORD-0001/ })

    await user.click(await screen.findByRole('button', { name: '+ Document uploaden' }))
    const dialog = screen.getByRole('dialog', { name: 'Document uploaden' })
    // Opened from the order level, the target defaults to that order.
    expect(within(dialog).getByLabelText('Opslaan bij')).toHaveValue('o-1')
    await user.selectOptions(within(dialog).getByLabelText('Documenttype'), 'DeliveryNote')
    const file = new File(['%PDF-1.4'], 'leverbon.pdf', { type: 'application/pdf' })
    await user.upload(within(dialog).getByLabelText(/^Bestand/), file)
    // The title is prefilled from the file name and stays editable.
    expect(within(dialog).getByLabelText(/^Titel/)).toHaveValue('leverbon')
    await user.clear(within(dialog).getByLabelText(/^Titel/))
    await user.type(within(dialog).getByLabelText(/^Titel/), 'Leverbon')
    await user.click(within(dialog).getByRole('button', { name: 'Uploaden' }))

    await waitFor(() => expect(upload.file).toHaveBeenCalledWith('doc-new', file))
    expect(documents.create).toHaveBeenCalledWith('d-1', {
      transportOrderId: 'o-1', dossierActivityId: null, documentType: 'DeliveryNote', customTypeName: null, title: 'Leverbon', issueDate: null, notes: null, customerVisible: false,
    })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(toast.showSuccess).toHaveBeenCalledWith('Document toegevoegd.')
    expect(await within(await list('Documenten van deze opdracht')).findByText('Leverbon', { selector: '.ddoc-title' })).toBeInTheDocument()
    expect(within(section()).getByText('3 documenten in dit dossier')).toBeInTheDocument()
  })

  it('refuses an unsupported file before anything is sent, and keeps the record for a retry when only the file upload fails', async () => {
    // applyAccept false: the browser-side accept filter must not hide what the component itself refuses.
    const user = userEvent.setup({ applyAccept: false })
    const created = document({ id: 'doc-new', title: 'Scan', hasFile: false, fileName: null })
    documents.create.mockResolvedValue(created)
    upload.file.mockRejectedValue(new Error('Storage offline'))
    renderPage()
    await screen.findByRole('tab', { name: /Dossierdocumenten/ })

    await user.click(await screen.findByRole('button', { name: '+ Document uploaden' }))
    const dialog = screen.getByRole('dialog', { name: 'Document uploaden' })
    await user.upload(within(dialog).getByLabelText(/^Bestand/), new File(['x'], 'virus.exe', { type: 'application/octet-stream' }))
    await user.click(within(dialog).getByRole('button', { name: 'Uploaden' }))
    expect(within(dialog).getByText('Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan.')).toBeInTheDocument()
    expect(documents.create).not.toHaveBeenCalled()

    await user.upload(within(dialog).getByLabelText(/^Bestand/), new File(['%PDF'], 'scan.pdf', { type: 'application/pdf' }))
    await user.click(within(dialog).getByRole('button', { name: 'Uploaden' }))
    // Metadata stored, file not: the dialog says so and offers retry / close / delete — no silent half state.
    expect(await within(dialog).findByText(/het bestand is NIET geüpload: Storage offline/)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Bestand opnieuw uploaden' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Sluiten zonder bestand' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Document verwijderen' })).toBeInTheDocument()
    expect(documents.create).toHaveBeenCalledTimes(1)
  })

  it('publishes to the portal after an explicit confirmation — the PUT carries the metadata unchanged with the new flag', async () => {
    const user = userEvent.setup()
    documents.update.mockResolvedValue({ customerVisible: true })
    renderPage()
    await screen.findByRole('tab', { name: /Dossierdocumenten/ })

    await user.click(await screen.findByRole('button', { name: 'Zichtbaarheid aanpassen: Vergunning uitzonderlijk vervoer' }))
    const dialog = screen.getByRole('dialog', { name: 'Zichtbaarheid aanpassen' })
    expect(within(dialog).getByText('De klant kan dit document daarna bekijken en downloaden in het klantportaal.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Zichtbaar maken voor de klant' }))

    await waitFor(() => expect(documents.update).toHaveBeenCalledWith('doc-general', {
      documentType: 'Other', customTypeName: 'Vergunning', title: 'Vergunning uitzonderlijk vervoer', issueDate: null, notes: null, customerVisible: true,
    }))
    expect(toast.showSuccess).toHaveBeenCalledWith('Het document is nu zichtbaar in het klantportaal.')
    await waitFor(() => expect(documents.list).toHaveBeenCalledTimes(2))
  })

  it('moves an order document to the dossier level without touching the file, and deletes after confirmation', async () => {
    const user = userEvent.setup()
    documents.move.mockResolvedValue({ ...orderDoc(), transportOrderId: null, orderNumber: null, scope: 'Dossier' })
    documents.remove.mockResolvedValue(undefined)
    renderPage(`${DOCUMENTS_TAB}?opdracht=o-1`)
    await screen.findByRole('tab', { name: /ORD-0001/ })

    await user.click(await screen.findByRole('button', { name: 'Verplaatsen: Getekende CMR' }))
    const move = screen.getByRole('dialog', { name: 'Document verplaatsen naar…' })
    expect(within(move).getByRole('radio', { name: /ORD-0001/ })).toBeChecked()
    expect(within(move).getByRole('radio', { name: /ORD-0001/ }).closest('label')).toHaveTextContent('huidige locatie')
    // Every level of the dossier is offered, grouped — the activity included.
    expect(within(move).getByRole('radio', { name: 'Plateau' })).toBeInTheDocument()
    await user.click(within(move).getByRole('radio', { name: 'Dossierdocumenten' }))
    await user.click(within(move).getByRole('button', { name: 'Verplaatsen' }))
    await waitFor(() => expect(documents.move).toHaveBeenCalledWith('doc-order', { targetTransportOrderId: null, targetDossierActivityId: null }))
    expect(toast.showSuccess).toHaveBeenCalledWith('Document verplaatst naar Dossierdocumenten.')

    await user.click(await screen.findByRole('button', { name: 'Verwijderen: Getekende CMR' }))
    const remove = screen.getByRole('dialog', { name: 'Document verwijderen' })
    expect(within(remove).getByText(/hoort alleen bij opdracht ORD-0001/)).toBeInTheDocument()
    await user.click(within(remove).getByRole('button', { name: 'Verwijderen' }))
    await waitFor(() => expect(documents.remove).toHaveBeenCalledWith('doc-order'))
  })

  it('groups the levels as Dossier / Opdrachten / Activiteiten: the standalone Plateau is a level with its OWN count and documents', async () => {
    const user = userEvent.setup()
    documents.list.mockResolvedValue([generalDoc(), orderDoc(), activityDoc()])
    renderPage()
    await screen.findByRole('tab', { name: /Dossierdocumenten/ })

    const nav = screen.getByRole('tablist', { name: 'Documentniveau' })
    expect(within(nav).getByText('Dossier', { selector: '.ddoc-level-group-label' })).toBeInTheDocument()
    expect(within(nav).getByText('Opdrachten', { selector: '.ddoc-level-group-label' })).toBeInTheDocument()
    expect(within(nav).getByText('Activiteiten', { selector: '.ddoc-level-group-label' })).toBeInTheDocument()
    // Secondary labels say what kind of level it is; the count is the level's OWN documents, never the total.
    const plateau = within(nav).getByRole('tab', { name: /Plateau/ })
    expect(plateau).toHaveTextContent('Activiteit')
    expect(plateau.querySelector('.ddoc-level-count')).toHaveTextContent('1')
    expect(within(nav).getByRole('tab', { name: /ORD-0001/ })).toHaveTextContent('Opdracht')
    expect(within(nav).getByRole('tab', { name: /ORD-0001/ }).querySelector('.ddoc-level-count')).toHaveTextContent('1')
    expect(within(nav).getByRole('tab', { name: /Dossierdocumenten/ }).querySelector('.ddoc-level-count')).toHaveTextContent('1')
    expect(within(section()).getByText('3 documenten in dit dossier')).toBeInTheDocument()

    // Dossier level: only the dossier document.
    const general = await list('Dossierdocumenten')
    expect(within(general).queryByText('Hijsplan plateau')).not.toBeInTheDocument()
    expect(within(general).queryByText('Getekende CMR')).not.toBeInTheDocument()

    // Plateau level: its own document, plus the dossier documents read-only — never the order's document.
    await user.click(plateau)
    expect(plateau).toHaveAttribute('aria-selected', 'true')
    const own = await list('Documenten van deze activiteit')
    expect(within(own).getByText('Hijsplan plateau')).toBeInTheDocument()
    expect(within(own).queryByText('Getekende CMR')).not.toBeInTheDocument()
    expect(within(own).queryByText('Vergunning uitzonderlijk vervoer')).not.toBeInTheDocument()
    expect(within(await list('Dossierdocumenten (gelden ook hier)')).getByText('Vergunning uitzonderlijk vervoer')).toBeInTheDocument()
    // An activity has no issued transport documents (no order).
    expect(within(section()).queryByRole('region', { name: 'Transportdocumenten' })).not.toBeInTheDocument()

    // Order level: never the activity's document.
    await user.click(within(nav).getByRole('tab', { name: /ORD-0001/ }))
    expect(within(await list('Documenten van deze opdracht')).queryByText('Hijsplan plateau')).not.toBeInTheDocument()
  })

  it('every standalone activity is a level, whatever it is called — decided by its link, not its name', async () => {
    api.getDossier.mockResolvedValue(dossierDetail({
      activities: [
        dossierActivity({ id: 'a-1', sequence: 1, linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft' }),
        dossierActivity({ id: 'a-7', sequence: 2, activityTypeCode: 'OVERIG', activityTypeName: 'Overig', label: 'Kraanwerk site B', hasStops: false }),
        // A transport-shaped activity WITHOUT an order is not an activity level: its documents follow its (future) order.
        dossierActivity({ id: 'a-8', sequence: 3, activityTypeCode: 'DIRECT_TRANSPORT', activityTypeName: 'Direct transport', hasStops: true }),
      ],
    }))
    renderPage()
    const nav = await screen.findByRole('tablist', { name: 'Documentniveau' })
    expect(within(nav).getByRole('tab', { name: /Kraanwerk site B/ })).toHaveTextContent('Activiteit')
    expect(within(nav).getAllByRole('tab')).toHaveLength(3)
  })

  it('?activiteit=<id> opens the activity level directly, and uploading from there targets that activity', async () => {
    const user = userEvent.setup()
    documents.list.mockResolvedValue([generalDoc(), orderDoc(), activityDoc()])
    const created = document({ id: 'doc-new', dossierActivityId: 'a-2', activityLabel: 'Plateau', scope: 'Activity', title: 'Foto plateau', hasFile: false, fileName: null })
    documents.create.mockResolvedValue(created)
    upload.file.mockResolvedValue({})
    renderPage(`${DOCUMENTS_TAB}?activiteit=a-2`)

    expect(await screen.findByRole('tab', { name: /Plateau/ })).toHaveAttribute('aria-selected', 'true')
    expect(within(await list('Documenten van deze activiteit')).getByText('Hijsplan plateau')).toBeInTheDocument()

    await user.click(await screen.findByRole('button', { name: '+ Document uploaden' }))
    const dialog = screen.getByRole('dialog', { name: 'Document uploaden' })
    // Context-sensitive default: the level the planner is looking at.
    const target = within(dialog).getByLabelText('Opslaan bij')
    expect(target).toHaveValue('activity:a-2')
    expect(within(target).getByRole('option', { name: 'Activiteit Plateau' })).toBeInTheDocument()
    expect(within(target).getByRole('option', { name: 'Dossierdocumenten (volledig dossier)' })).toBeInTheDocument()
    await user.upload(within(dialog).getByLabelText(/^Bestand/), new File(['%PDF-1.4'], 'foto.pdf', { type: 'application/pdf' }))
    await user.click(within(dialog).getByRole('button', { name: 'Uploaden' }))

    await waitFor(() => expect(documents.create).toHaveBeenCalledWith('d-1', expect.objectContaining({ transportOrderId: null, dossierActivityId: 'a-2', title: 'foto' })))
  })

  it('deleting an activity document says which activity it belongs to', async () => {
    const user = userEvent.setup()
    documents.list.mockResolvedValue([generalDoc(), orderDoc(), activityDoc()])
    documents.remove.mockResolvedValue(undefined)
    renderPage(`${DOCUMENTS_TAB}?activiteit=a-2`)
    await screen.findByRole('tab', { name: /Plateau/ })

    await user.click(await screen.findByRole('button', { name: 'Verwijderen: Hijsplan plateau' }))
    const remove = screen.getByRole('dialog', { name: 'Document verwijderen' })
    expect(within(remove).getByText(/hoort alleen bij activiteit Plateau/)).toBeInTheDocument()
    await user.click(within(remove).getByRole('button', { name: 'Verwijderen' }))
    await waitFor(() => expect(documents.remove).toHaveBeenCalledWith('doc-activity'))
  })

  it('without write rights the tab is read-only: view/download stay, every mutation is gone', async () => {
    auth.permissions = new Set(['dossiers.view', 'orders.view'])
    renderPage()
    await screen.findByRole('tab', { name: /Dossierdocumenten/ })
    const general = await list('Dossierdocumenten')
    expect(await within(general).findByRole('button', { name: 'Downloaden: Vergunning uitzonderlijk vervoer' })).toBeInTheDocument()
    expect(within(general).getByRole('button', { name: 'Bekijken: Vergunning uitzonderlijk vervoer' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Document uploaden' })).not.toBeInTheDocument()
    expect(within(general).queryByRole('button', { name: /Zichtbaarheid aanpassen/ })).not.toBeInTheDocument()
    expect(within(general).queryByRole('button', { name: /Verplaatsen/ })).not.toBeInTheDocument()
    expect(within(general).queryByRole('button', { name: /Verwijderen/ })).not.toBeInTheDocument()
  })

  it('a load failure is told, with a retry — never an empty "no documents"', async () => {
    const user = userEvent.setup()
    documents.list.mockRejectedValueOnce(new Error('down')).mockResolvedValue([generalDoc()])
    renderPage()
    expect(await within(await screen.findByRole('alert')).findByText(/De documenten konden niet worden geladen/)).toBeInTheDocument()
    expect(screen.queryByText('Nog geen dossierdocumenten.')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))
    expect(await screen.findByText('Vergunning uitzonderlijk vervoer')).toBeInTheDocument()
  })
})

describe('ActivityDocumentStatus on the activity card (spec §20)', () => {
  it('a standalone activity without uploads is quietly "Geen documenten" — never "CMR missing"', () => {
    const plateau = dossierActivity({ id: 'a-2', activityTypeCode: 'PLATEAU', activityTypeName: 'Plateau', hasStops: false, supportsGoods: false })
    render(<ul><ActivityCard activity={plateau} activities={[plateau]} dossier={dossierDetail()} onOpen={vi.fn()} /></ul>)
    const cell = screen.getByText('CMR / document').parentElement!.querySelector('dd')!
    expect(cell).toHaveTextContent('Geen documenten')
    expect(cell).not.toHaveTextContent('Nog niet aangemaakt')
    expect(cell).not.toHaveTextContent('Niet van toepassing')
    expect(within(cell).queryByRole('button')).not.toBeInTheDocument()
  })

  it('a standalone activity with uploads shows their count and opens the Documenten tab on the activity', async () => {
    const user = userEvent.setup()
    const onOpenDocuments = vi.fn()
    const plateau = dossierActivity({ id: 'a-2', activityTypeCode: 'PLATEAU', activityTypeName: 'Plateau', hasStops: false, supportsGoods: false, documentCount: 2 })
    render(<ul><ActivityCard activity={plateau} activities={[plateau]} dossier={dossierDetail()} onOpen={vi.fn()} onOpenDocuments={onOpenDocuments} /></ul>)
    const cell = screen.getByText('CMR / document').parentElement!.querySelector('dd')!
    const status = within(cell).getByRole('button')
    expect(status).toHaveTextContent('2 documenten')
    await user.click(status)
    expect(onOpenDocuments).toHaveBeenCalledWith(plateau)
  })

  it('an order-backed activity lists its uploads next to the issued document', () => {
    const transport = dossierActivity({ linkedTransportOrderId: 'o-1', linkedOrderNumber: 'ORD-0001', linkedOrderStatus: 'Draft', issuedDocuments: [{ id: 'i-1', kind: 'WorkOrder', documentNumber: 'WB-2026-00003' }], documentCount: 2 })
    render(<ul><ActivityCard activity={transport} activities={[transport]} dossier={dossierDetail()} onOpen={vi.fn()} /></ul>)
    const cell = screen.getByText('CMR / document').parentElement!.querySelector('dd')!
    expect(cell).toHaveTextContent('WB-2026-00003 · 2 documenten')
  })
})
