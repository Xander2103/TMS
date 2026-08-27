import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import * as legalEntitiesApi from '../../../legal-entities/api/legalEntitiesApi'
import { CustomerForm } from '../CustomerForm'
import customersCss from '../customers.css?raw'
import type { CustomerDetail } from '../../types'

/**
 * Sprint 1C/1D — klantdetail.
 *
 * 1C: de secties volgen de bedrijfslogica "wie is de klant? → waar werken we? →
 * wie spreken we aan? → financieel/commercieel". Adressen en contactpersonen zijn
 * eigen secties (voorheen verstopt binnen "Klantgegevens"), en facturatie komt
 * vóór fiscaal.
 *
 * 1D: de fiscale kaart gebruikt hetzelfde detailrij-raster als de hoofdsamenvatting,
 * zodat alle waardekolommen op dezelfde horizontale positie starten.
 */

const auth = vi.hoisted(() => ({ permissions: ['customers.manage_fiscal', 'customers.view'] }))

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((code) => auth.permissions.includes(code)),
  }),
}))
vi.mock('../../../master-data/hooks/useLookupOptions', () => ({
  useLookupOptions: () => ({ options: [], isLoading: false, error: null }),
}))
vi.mock('../../../master-data/components/LookupSelect', () => ({
  LookupSelect: ({ id }: { id?: string }) => <input id={id} aria-label="lookup" />,
}))
vi.mock('../../../reference/components/CountryCombobox', () => ({
  CountryCombobox: ({ id }: { id?: string }) => <input id={id} aria-label="Land" />,
}))
vi.mock('../../../../components/ui/UnsavedChangesGuard', () => ({
  UnsavedChangesGuard: () => null,
}))

beforeEach(() => {
  auth.permissions = ['customers.manage_fiscal', 'customers.view']
  vi.spyOn(api, 'getVatTreatments').mockResolvedValue([])
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([])
  vi.spyOn(legalEntitiesApi, 'getLegalEntityOptions').mockResolvedValue([])
})

function existingCustomer(): CustomerDetail {
  return {
    id: 'c1', customerNumber: 'KL-1', name: 'Haven BV', legalName: null, vatNumber: null,
    categoryId: null, categoryName: null, email: null, phoneNumber: null, website: null,
    street: null, houseNumber: null, postalCode: null, city: null, countryCode: null,
    invoiceEmail: null, paymentTermDays: 30, defaultLanguageCode: null, notes: null,
    isActive: true, isBlocked: false, blockReason: null, nickname: null, companyNumber: null,
    currencyCode: 'EUR', iban: null, bic: null, bankName: null, bankAccountNumber: null,
    defaultLegalEntityId: null, contacts: [],
    vatTreatment: 'DomesticVat', defaultVatRatePercent: null, vatCountryCode: null, vatNotes: null,
    peppolId: null, peppolScheme: null, invoiceLanguageCode: null, purchaseOrderRequired: false,
    signedDeliveryNoteRequired: false, customerReferenceRequired: false,
    peppolEnabled: false, peppolDeliveryPreference: 'Peppol', buyerReference: null,
    peppolValidationStatus: 'Unknown', peppolValidatedAt: null, peppolValidationReference: null,
  } as CustomerDetail
}

function renderEditForm() {
  return render(
    <MemoryRouter>
      <CustomerForm
        mode="edit"
        initial={existingCustomer()}
        isSubmitting={false}
        submitError={null}
        serverFieldErrors={{}}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
        editPanels={{
          adressen: <div>adressen-paneel</div>,
          contactpersonen: <div>contacten-paneel</div>,
          communicatie: <div>communicatie-paneel</div>,
          historiek: <div>historiek-paneel</div>,
          tarieven: <div>tarieven-paneel</div>,
        }}
      />
    </MemoryRouter>,
  )
}

/** Bodies of every rule whose selector list mentions `selector`, concatenated. */
function ruleBody(css: string, selector: string): string {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const bodies: string[] = []
  for (const match of stripped.matchAll(/([^{}]+)\{([^}]*)\}/g)) {
    const selectors = match[1].split(',').map((s) => s.trim())
    if (selectors.some((s) => s === selector || s.startsWith(`${selector} `) || s.startsWith(`${selector}:`))) {
      bodies.push(match[2])
    }
  }
  return bodies.join('\n')
}

describe('customer detail — section order (1C)', () => {
  it('orders the edit sections by business logic, with addresses and contacts of their own', async () => {
    renderEditForm()
    const tabs = await screen.findAllByRole('tab')
    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual([
      'Klantgegevens',
      'Adressen',
      'Contactpersonen',
      'Facturatie',
      'Fiscaal & Peppol',
      'Bank',
      'Communicatie',
      'Tarieven & toeslagen',
      'Historiek',
    ])
  })

  it('puts the address panel in its own section rather than inside Klantgegevens', async () => {
    renderEditForm()
    const tabs = await screen.findAllByRole('tab')
    const addresses = tabs.find((tab) => tab.textContent?.includes('Adressen'))
    expect(addresses).toBeDefined()
    // Klantgegevens is shown first and must no longer carry the address panel.
    expect(screen.queryByText('adressen-paneel')).not.toBeInTheDocument()
  })
})

describe('customer detail — fiscal card alignment (1D)', () => {
  it('uses one shared label column so every value column starts at the same position', () => {
    const dl = ruleBody(customersCss, '.customer-summary dl')
    expect(dl, '.customer-summary dl rule not found').not.toBe('')
    // `auto` sizes the label column per card, so two cards never line up.
    expect(dl).not.toMatch(/grid-template-columns:\s*auto\s/)
    expect(dl).toMatch(/grid-template-columns:\s*var\(--customer-summary-label\)/)
  })

  it('does not offset the fiscal card from its grid sibling', () => {
    const card = ruleBody(customersCss, '.customer-vat-summary')
    expect(card).not.toMatch(/margin-top:\s*20px/)
  })

  it('styles the card heading once for every summary card', () => {
    expect(ruleBody(customersCss, '.customer-summary h3')).not.toBe('')
  })
})
