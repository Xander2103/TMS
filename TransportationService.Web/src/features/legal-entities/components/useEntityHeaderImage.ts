import { useEffect, useState } from 'react'
import defaultHeaderImage from '../../../assets/transport-header.jpg'
import { fetchLegalEntityHeaderImage, getLegalEntityOptions } from '../api/legalEntitiesApi'

/** One object URL per entity for the whole session: switching between dossiers never refetches a photo. */
const loaded = new Map<string, Promise<string | null>>()

function loadImage(entityId: string): Promise<string | null> {
  let pending = loaded.get(entityId)
  if (!pending) {
    pending = fetchLegalEntityHeaderImage(entityId).then((blob) => (blob ? URL.createObjectURL(blob) : null))
    loaded.set(entityId, pending)
  }
  return pending
}

/** After an upload or removal in Parameters → Entiteiten. */
export function forgetEntityHeaderImage(entityId: string) {
  loaded.delete(entityId)
}

/**
 * The banner photo for a dossier or order header: the photo of its legal entity, else of the
 * tenant's default entity (a dossier without entity, "Klantstandaard" on an order), else the
 * bundled transport photo. Cosmetic: any failure simply keeps the bundled photo.
 */
export function useEntityHeaderImage(legalEntityId: string | null | undefined): string {
  const [image, setImage] = useState<{ forId: string; url: string } | null>(null)
  const key = legalEntityId ?? ''

  useEffect(() => {
    let cancelled = false
    Promise.resolve()
      .then(() => getLegalEntityOptions())
      .then((options) => {
        const entity = options.find((o) => o.id === key) ?? (key ? undefined : options.find((o) => o.isDefault))
        return entity?.hasHeaderImage ? loadImage(entity.id) : null
      })
      .then((url) => {
        if (!cancelled && url) setImage({ forId: key, url })
      })
      .catch(() => {
        /* the bundled photo stays */
      })
    return () => {
      cancelled = true
    }
  }, [key])

  return image?.forId === key ? image.url : defaultHeaderImage
}
