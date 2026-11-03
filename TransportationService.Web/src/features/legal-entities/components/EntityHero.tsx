import type { ReactNode } from 'react'
import { useEntityHeaderImage } from './useEntityHeaderImage'
import './entity-hero.css'

interface EntityHeroProps {
  /** Whose photo the banner shows; null falls back to the default entity. */
  legalEntityId: string | null | undefined
  /** Title block and actions, on top of the photo. */
  children: ReactNode
  /** Attention points and the like: under the photo, inside the same card. Renders nothing when empty. */
  footer?: ReactNode
  className?: string
}

/**
 * The banner of the dossier and order headers: one card with the entity's photo faded into the
 * theme's surface, so it reads dark navy in the dark theme and a light purple in the light one.
 */
export function EntityHero({ legalEntityId, children, footer, className }: EntityHeroProps) {
  const image = useEntityHeaderImage(legalEntityId)
  return (
    <div className={['entity-hero', className].filter(Boolean).join(' ')}>
      <div className="entity-hero-banner">
        {/* The clip lives on its own layer: a "Meer" menu in the content has to reach past the banner. */}
        <div className="entity-hero-photo-clip" aria-hidden="true">
          <div className="entity-hero-photo" style={{ backgroundImage: `url("${image}")` }} data-testid="entity-hero-photo" />
        </div>
        <div className="entity-hero-content">{children}</div>
      </div>
      {footer}
    </div>
  )
}
