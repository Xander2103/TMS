import { DOSSIER_DOCUMENT_EXTENSIONS, DOSSIER_DOCUMENT_MAX_BYTES } from '../api/dossierDocumentsApi'

/** Why a file is refused before it ever leaves the browser; null = acceptable (same rules as the upload route). */
export function fileProblem(file: File): 'empty' | 'tooLarge' | 'unsupportedType' | null {
  if (file.size === 0) return 'empty'
  if (file.size > DOSSIER_DOCUMENT_MAX_BYTES) return 'tooLarge'
  const name = file.name.toLowerCase()
  return DOSSIER_DOCUMENT_EXTENSIONS.some((ext) => name.endsWith(ext)) ? null : 'unsupportedType'
}
