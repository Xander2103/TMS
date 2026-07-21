/** Bestandsgrootte in KB/MB voor de bijlagenlijst (nl-BE notatie). */
export function formatFileSize(sizeBytes: number): string {
  if (sizeBytes >= 1024 * 1024) {
    return `${(sizeBytes / (1024 * 1024)).toLocaleString('nl-BE', { maximumFractionDigits: 1 })} MB`
  }
  return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`
}
