import { useEffect, useMemo, useState } from 'react'
import { useLocale } from '../../../i18n/localeContext'
import { listUnitLabels, listUnitOrderOptions, type UnitTypeLabel } from '../api/unitTypesApi'
import { buildUnitTypesModel, type UnitChoice, type UnitTypesModel } from '../unitTypes'

export interface UseUnitTypesResult extends UnitTypesModel {
  isLoading: boolean
}

/**
 * Loads the units twice over: `order-options` (what a user may PICK on an order) and `labels`
 * (the name of every code ever used, for DISPLAY). Replaces `useLookupOptions('/api/unit-types')`,
 * whose active-only list both offered non-order units and dropped the names of retired ones.
 * A failed request leaves its list empty — labels then fall back to the code.
 */
export function useUnitTypes(): UseUnitTypesResult {
  const { t } = useLocale()
  const [state, setState] = useState<{ orderOptions: UnitChoice[]; labels: UnitTypeLabel[]; loaded: boolean }>({
    orderOptions: [],
    labels: [],
    loaded: false,
  })

  useEffect(() => {
    let cancelled = false
    void Promise.allSettled([listUnitOrderOptions(), listUnitLabels()]).then(([options, labels]) => {
      if (cancelled) return
      setState({
        orderOptions: options.status === 'fulfilled' ? options.value : [],
        labels: labels.status === 'fulfilled' ? labels.value : [],
        loaded: true,
      })
    })
    return () => {
      cancelled = true
    }
  }, [])

  const model = useMemo(
    () =>
      buildUnitTypesModel(state.orderOptions, state.labels, (name, label) =>
        t(
          !label || !label.isActive || label.isDeleted
            ? 'masterData.units.retainedInactive'
            : 'masterData.units.retainedUnavailable',
          { name },
        ),
      ),
    [state.orderOptions, state.labels, t],
  )

  return { ...model, isLoading: !state.loaded }
}
