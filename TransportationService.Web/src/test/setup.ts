import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'

// jsdom defaults navigator.language to en-US; the portal's mini-i18n falls back to the browser
// language, so pin it to Dutch to keep every existing (Dutch-asserting) test deterministic.
Object.defineProperty(window.navigator, 'language', { value: 'nl-BE', configurable: true })
Object.defineProperty(window.navigator, 'languages', { value: ['nl-BE'], configurable: true })

afterEach(() => {
  cleanup()
})
