const LOCAL_DEV_API_BASE_URL = 'http://localhost:5019'

/**
 * CFG-11: the localhost fallback is a development/test convenience only. A production bundle
 * without VITE_API_BASE_URL cannot be built (vite.config.ts refuses it); should one exist anyway,
 * it fails loudly here instead of silently calling the developer's machine.
 */
function resolveApiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL
  if (configured !== undefined && configured !== '') {
    return configured
  }

  if (import.meta.env.PROD) {
    throw new Error('VITE_API_BASE_URL ontbreekt in deze productiebuild; de API-basis-URL is niet geconfigureerd.')
  }

  return LOCAL_DEV_API_BASE_URL
}

export const apiBaseUrl: string = resolveApiBaseUrl()
