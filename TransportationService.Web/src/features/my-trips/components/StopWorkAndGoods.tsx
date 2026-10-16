import { useLocale } from '../../../i18n/localeContext'
import type { ExecutionStop } from '../types'

const NUMBER_LOCALE: Record<string, string> = { nl: 'nl-BE', fr: 'fr-BE', en: 'en-GB' }

/**
 * M7 (acceptance 2026-10-16): what the driver does at a stop, compactly — the work description and
 * lift data of on-site work (crane job), and the goods handled at a loading/unloading stop
 * (description, quantity × unit, weight). Operational data only: the API never sends a price here.
 */
export function StopWorkAndGoods({ stop }: { stop: Pick<ExecutionStop, 'work' | 'goods' | 'goodsTotalWeightKg'> }) {
  const { t, locale } = useLocale()
  const number = (value: number) => new Intl.NumberFormat(NUMBER_LOCALE[locale] ?? locale, { maximumFractionDigits: 2 }).format(value)

  const work = stop.work ?? null
  const goods = stop.goods ?? []
  const liftFacts = work
    ? [
        work.liftLoadWeightKg != null ? t('myTrips.execution.work.liftWeight', { kg: number(work.liftLoadWeightKg) }) : null,
        work.liftLoadDimensions ? t('myTrips.execution.work.liftDimensions', { value: work.liftLoadDimensions }) : null,
        work.liftRadiusMeters != null ? t('myTrips.execution.work.liftRadius', { m: number(work.liftRadiusMeters) }) : null,
        work.liftHeightMeters != null ? t('myTrips.execution.work.liftHeight', { m: number(work.liftHeightMeters) }) : null,
        work.liftEquipment ? t('myTrips.execution.work.liftEquipment', { value: work.liftEquipment }) : null,
        work.liftConditions ? t('myTrips.execution.work.liftConditions', { value: work.liftConditions }) : null,
      ].filter((fact): fact is string => fact !== null)
    : []
  const hasWork = work !== null && (Boolean(work.workDescription) || liftFacts.length > 0)

  if (!hasWork && goods.length === 0) return null

  return (
    <>
      {hasWork && (
        <section className="mt-stop-work" aria-label={t('myTrips.execution.work.title')}>
          <h4>{t('myTrips.execution.work.title')}</h4>
          {work?.workDescription && <p className="mt-stop-work-text">{work.workDescription}</p>}
          {liftFacts.length > 0 && <p className="mt-stop-work-facts">{liftFacts.join(' · ')}</p>}
        </section>
      )}
      {goods.length > 0 && (
        <section className="mt-stop-goods" aria-label={t('myTrips.execution.goods.title')}>
          <h4>{t('myTrips.execution.goods.title')}</h4>
          <ul>
            {goods.map((line, index) => (
              <li key={index}>
                <span className="mt-goods-qty">
                  {line.unit ? `${number(line.quantity)} × ${line.unit}` : number(line.quantity)}
                </span>{' '}
                {line.description}
                {line.weightKg != null && <span className="mt-goods-weight"> · {t('myTrips.execution.goods.weight', { kg: number(line.weightKg) })}</span>}
              </li>
            ))}
          </ul>
          {goods.length > 1 && stop.goodsTotalWeightKg != null && (
            <p className="mt-goods-total">{t('myTrips.execution.goods.total', { kg: number(stop.goodsTotalWeightKg) })}</p>
          )}
        </section>
      )}
    </>
  )
}
