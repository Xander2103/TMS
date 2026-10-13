import { createContext } from 'react'

/**
 * True inside a SectionedForm body. The active section was an explicit user choice, so
 * nested collapsible sections/accordions must render expanded — a section tab click may
 * never require a second click to reveal the content.
 */
export const SectionedFormBodyContext = createContext(false)
