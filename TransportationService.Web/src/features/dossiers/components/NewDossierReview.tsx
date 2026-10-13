import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatDate } from '../../../utils/dates'
import { formatQuantity } from '../../../utils/numbers'
import type { CargoFormRow, CargoSummary, OrderFormValidationError, StopFormRow } from '../../transport-orders/components/sections/orderFormState'

export type NewDossierSectionId = 'basis' | 'route' | 'goederen' | 'vereisten' | 'controle'

export interface NewDossierReviewProps {
  customerName: string | null
  /** Chosen transport type name; null while nothing is chosen. */
  typeName: string | null
  isBlanco: boolean
  customerReference: string
  dossierDate: string
  /** True when route/goods/requirements are part of this create (transport intake). */
  intake: boolean
  onSite: boolean
  /** The stops that will be submitted (empty rows already dropped). */
  stops: StopFormRow[]
  workDescription: string
  goodsDescription: string
  cargoItems: CargoFormRow[]
  cargoSummary: CargoSummary | null
  /** Labels of the ticked operational requirements. */
  requirements: string[]
  notes: string
  /** Non-blocking things worth a look before creating (missing dates, unknown weights, …). */
  warnings: string[]
  /** Blocking validation errors of the last submit attempt. */
  errors: OrderFormValidationError[]
  /** Context notes (blanco, no order permission, type without route). */
  hints: string[]
  onEdit: (section: NewDossierSectionId) => void
  onErrorSelect: (field: string) => void
}

/**
 * "Controle": a compact read-only recap of the WHOLE create state before the dossier is made —
 * every block links back to its section, and the open validation errors sit on top with a jump.
 * It reads the same lifted state the sections edit; nothing is recomputed differently here.
 */
export function NewDossierReview({
  customerName, typeName, isBlanco, customerReference, dossierDate, intake, onSite, stops, workDescription,
  goodsDescription, cargoItems, cargoSummary, requirements, notes, warnings, errors, hints, onEdit, onErrorSelect,
}: NewDossierReviewProps) {
  const { t } = useLocale()
  const loading = stops.filter((s) => s.stopType === 'Loading')
  const unloading = stops.filter((s) => s.stopType === 'Unloading')
  const first = stops[0]
  const last = stops[stops.length - 1]
  const cities = first && last && first !== last && (first.city || last.city) ? `${first.city || '?'} → ${last.city || '?'}` : null
  const datedStops = stops.filter((s) => s.date)
  const planning = datedStops.length > 0
    ? datedStops.map((s) => (s.fromTime && s.toTime ? `${formatDate(s.date)} ${s.fromTime}–${s.toTime}` : formatDate(s.date)))
    : []
  const incompleteWindow = stops.some((s) => !s.date || !s.fromTime || !s.toTime)

  const block = (section: NewDossierSectionId, title: string, lines: string[], empty?: string) => (
    <section className="new-dossier-review-block" aria-labelledby={`ndr-${section}-${title.replace(/\s+/g, '-')}`}>
      <div className="new-dossier-review-head">
        <h3 id={`ndr-${section}-${title.replace(/\s+/g, '-')}`}>{title}</h3>
        <Button variant="ghost" onClick={() => onEdit(section)}>{t('dossiers.new.review.edit')}</Button>
      </div>
      {lines.length > 0 ? (
        <ul className="new-dossier-review-lines">
          {lines.map((line, index) => <li key={index}>{line}</li>)}
        </ul>
      ) : (
        <p className="new-dossier-review-empty">{empty ?? '—'}</p>
      )}
    </section>
  )

  return (
    <div className="new-dossier-review">
      <p className="new-dossier-hint">{t('dossiers.new.review.intro')}</p>

      {errors.length > 0 && (
        <div className="new-dossier-review-errors" role="alert">
          <p className="new-dossier-review-errors-title">{t('dossiers.new.review.errorsTitle', { count: errors.length })}</p>
          <ul>
            {errors.map((error, index) => (
              <li key={`${error.field}-${index}`}>
                <button type="button" className="link-button" onClick={() => onErrorSelect(error.field)}>
                  {error.label}: {error.message}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {warnings.length > 0 && (
        <div className="new-dossier-review-warnings" role="status">
          <p className="new-dossier-review-warnings-title">{t('dossiers.new.review.warnings')}</p>
          <ul>
            {warnings.map((warning) => <li key={warning}>⚠ {warning}</li>)}
          </ul>
        </div>
      )}

      <div className="new-dossier-review-grid">
        {block('basis', t('dossiers.new.review.customer'), customerName ? [customerName] : [], t('dossiers.new.review.noCustomer'))}
        {block(
          'basis',
          t('dossiers.new.review.transport'),
          isBlanco ? [t('dossiers.new.review.blanco')] : typeName ? [typeName] : [],
          t('dossiers.new.review.noType'),
        )}
        {block('basis', t('dossiers.new.review.referenceAndDate'), [
          `${t('dossiers.new.customerReference')}: ${customerReference.trim() || '—'}`,
          `${t('dossiers.new.date')}: ${dossierDate ? formatDate(dossierDate) : '—'}`,
        ])}
        {intake && block(
          'route',
          t('dossiers.new.review.route'),
          onSite
            ? [
                stops.length > 0 ? t('dossiers.new.review.routeSite', { city: stops[0].city || stops[0].locationName || '—' }) : t('dossiers.new.review.routeEmpty'),
                ...(workDescription.trim() ? [workDescription.trim()] : []),
              ]
            : [
                ...(stops.length > 0 ? [t('dossiers.new.review.routeStops', { loading: loading.length, unloading: unloading.length })] : []),
                ...(cities ? [cities] : []),
              ],
          t('dossiers.new.review.routeEmpty'),
        )}
        {intake && block(
          'route',
          t('dossiers.new.review.planning'),
          [...planning, ...(stops.length > 0 && incompleteWindow ? [t('dossiers.new.review.planningNoWindow')] : [])],
          t('dossiers.new.review.planningNone'),
        )}
        {intake && !onSite && block(
          'goederen',
          t('dossiers.new.review.goods'),
          [
            ...(goodsDescription.trim() ? [goodsDescription.trim()] : []),
            ...(cargoItems.length > 0 ? [t('dossiers.new.review.goodsLines', { count: cargoItems.length })] : []),
            ...cargoItems.slice(0, 3).map((cargo) => cargo.description.trim()).filter((description) => description !== ''),
            ...(cargoSummary?.units.map(([label, qty]) => `${formatQuantity(qty)} ${label}`) ?? []),
            ...(cargoSummary?.weight !== null && cargoSummary?.weight !== undefined ? [t('dossiers.new.review.goodsWeight', { value: formatQuantity(cargoSummary.weight) })] : []),
          ],
          t('dossiers.new.review.goodsEmpty'),
        )}
        {intake && !onSite && block('vereisten', t('dossiers.new.review.requirements'), requirements, t('dossiers.new.review.requirementsNone'))}
        {intake && block('vereisten', t('dossiers.new.review.notes'), notes.trim() ? [notes.trim()] : [], t('dossiers.new.review.notesNone'))}
      </div>

      {hints.map((hint) => (
        <p key={hint} className="new-dossier-hint" role="note">{hint}</p>
      ))}
    </div>
  )
}
