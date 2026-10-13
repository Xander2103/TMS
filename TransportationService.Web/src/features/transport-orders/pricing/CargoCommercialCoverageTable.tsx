import { useLocale } from '../../../i18n/localeContext'
import { CargoCoverageBadge } from '../components/CargoCoverageBadge'
import type { CargoItem } from '../types'
import { describeCargoItem, type UnitLabelResolver } from './cargoLabels'

interface CargoCommercialCoverageTableProps {
  cargoItems: CargoItem[]
  unitLabel: UnitLabelResolver
}

/**
 * D4 "Prijsdekking goederen": every goods line with the commercial coverage the SERVER derived
 * (linked to a sales line / included in the trip or activity price / still to review). Nothing is
 * guessed here — a payload without any coverage value renders no table at all, and a line without
 * a value renders an empty cell.
 */
export function CargoCommercialCoverageTable({ cargoItems, unitLabel }: CargoCommercialCoverageTableProps) {
  const { t } = useLocale()
  if (!cargoItems.some((item) => item.commercialCoverage)) return null
  const items = [...cargoItems].sort((a, b) => a.sequence - b.sequence)

  return (
    <div className="order-cargo-coverage">
      <h3>{t('dossierPricing.goodsCoverage.title')}</h3>
      <div className="order-cargo-coverage-wrap">
        <table className="order-cargo-coverage-table">
          <thead>
            <tr>
              <th scope="col">{t('dossierPricing.goodsCoverage.colGoods')}</th>
              <th scope="col">{t('dossierPricing.goodsCoverage.colCoverage')}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{describeCargoItem(item, unitLabel)}</td>
                <td>
                  <CargoCoverageBadge coverage={item.commercialCoverage} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="order-cargo-coverage-hint">{t('dossierPricing.goodsCoverage.includedHint')}</p>
    </div>
  )
}
