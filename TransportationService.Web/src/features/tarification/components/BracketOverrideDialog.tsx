import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { searchCustomers } from '../../customers/api/customersApi'
import { createBracketOverride, type PriceRule, type PriceRuleBracket } from '../api/pricingApi'

interface BracketOverrideDialogProps {
  rule: PriceRule
  bracket: PriceRuleBracket
  onSaved: () => void
  onClose: () => void
}

function bracketLabel(bracket: PriceRuleBracket): string {
  return bracket.toQuantity !== null ? `${bracket.fromQuantity}–${bracket.toQuantity}` : `${bracket.fromQuantity}+`
}

/**
 * "Maak klantafwijking": one customer-specific price for a single bracket row of a shared rule.
 * All other rows keep following the shared table; the engine falls back automatically.
 */
export function BracketOverrideDialog({ rule, bracket, onSaved, onClose }: BracketOverrideDialogProps) {
  const { showSuccess, showError } = useToast()
  const [customers, setCustomers] = useState<SearchableSelectOption[]>([])
  const [customersLoading, setCustomersLoading] = useState(true)
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [price, setPrice] = useState('')
  const [pricePerExtraUnit, setPricePerExtraUnit] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveUntil, setEffectiveUntil] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    searchCustomers({ page: 1, pageSize: 200, isActive: true })
      .then((result) => setCustomers(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomers([]))
      .finally(() => setCustomersLoading(false))
  }, [])

  async function submit() {
    if (!customerId) {
      setError('Kies een klant.')
      return
    }
    const parsedPrice = Number(price)
    if (price.trim() === '' || Number.isNaN(parsedPrice) || parsedPrice < 0) {
      setError('Geef een geldige prijs op (0 of hoger).')
      return
    }
    const parsedExtra = pricePerExtraUnit.trim() === '' ? null : Number(pricePerExtraUnit)
    if (parsedExtra !== null && (Number.isNaN(parsedExtra) || parsedExtra < 0)) {
      setError('De prijs per extra eenheid moet 0 of hoger zijn.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await createBracketOverride(rule.id, {
        customerId,
        fromQuantity: bracket.fromQuantity,
        toQuantity: bracket.toQuantity,
        weightToKg: bracket.weightToKg,
        volumeToM3: bracket.volumeToM3,
        loadingMetersTo: bracket.loadingMetersTo,
        price: parsedPrice,
        pricePerExtraUnit: parsedExtra,
        effectiveFrom: effectiveFrom || null,
        effectiveUntil: effectiveUntil || null,
      })
      showSuccess('Klantafwijking toegevoegd.')
      onSaved()
    } catch (err) {
      const described = describeApiError(err, 'De klantafwijking kon niet worden opgeslagen.')
      showError(described.message)
      setError(described.message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`Klantafwijking — staffel ${bracketLabel(bracket)} van "${rule.name}"`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <p className="placeholder-text">
        Alleen deze staffelrij krijgt een klantprijs; alle andere rijen blijven de gedeelde tabel volgen
        (standaard € {bracket.price.toFixed(2)}).
      </p>
      <FormField label="Klant" required error={!customerId && error ? error : undefined}>
        <SearchableSelect
          value={customerId}
          onChange={setCustomerId}
          options={customers}
          isLoading={customersLoading}
          ariaLabel="Klant voor klantafwijking"
          placeholder="— Kies klant —"
        />
      </FormField>
      <FormField label="Prijs voor deze rij (€)" required>
        <input
          type="number"
          step="0.01"
          min="0"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          aria-label="Prijs voor klantafwijking"
        />
      </FormField>
      {bracket.toQuantity === null && (
        <FormField
          label="Prijs per extra eenheid (€)"
          hint={`Leeg = de gedeelde prijs per extra eenheid blijft gelden${bracket.pricePerExtraUnit !== null ? ` (€ ${bracket.pricePerExtraUnit.toFixed(2)})` : ''}.`}
        >
          <input
            type="number"
            step="0.01"
            min="0"
            value={pricePerExtraUnit}
            onChange={(e) => setPricePerExtraUnit(e.target.value)}
            aria-label="Prijs per extra eenheid voor klantafwijking"
          />
        </FormField>
      )}
      <FormField label="Geldig van" hint="Leeg = volgt de geldigheid van de regel.">
        <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} aria-label="Klantafwijking geldig van" />
      </FormField>
      <FormField label="Geldig tot">
        <input type="date" value={effectiveUntil} onChange={(e) => setEffectiveUntil(e.target.value)} aria-label="Klantafwijking geldig tot" />
      </FormField>
      {error && customerId && <p role="alert" className="placeholder-text">{error}</p>}
    </Modal>
  )
}
