/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  /** Commit-hash van de frontend-build; ontbreekt in lokale dev ("lokale build"). */
  readonly VITE_BUILD_COMMIT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
