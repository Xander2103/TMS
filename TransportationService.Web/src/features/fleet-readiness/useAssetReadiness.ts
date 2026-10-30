import { useEffect, useState } from 'react'
import { getAssetReadiness } from './fleetReadinessApi'
import type { AssetReadiness, FleetAssetKind } from './types'

/** UX-007: today's verdict for a page header; null until known, and when it could not be read. */
export function useAssetReadiness(kind: FleetAssetKind, assetId: string | undefined, reloadToken: number): AssetReadiness | null {
  const [state, setState] = useState<{ key: string; data: AssetReadiness | null }>({ key: '', data: null })
  const key = `${kind}:${assetId}:${reloadToken}`
  useEffect(() => {
    if (!assetId) return
    let mounted = true
    getAssetReadiness(kind, assetId)
      .then((data) => { if (mounted) setState({ key, data }) })
      .catch(() => { if (mounted) setState({ key, data: null }) })
    return () => { mounted = false }
  }, [kind, assetId, key])
  return state.key === key ? state.data : null
}
