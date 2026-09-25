const INVALID = new Set(['\\', '/', ':', '*', '?', '"', '<', '>', '|'])

const isControl = (code: number) => code < 32 || (code >= 127 && code <= 159)

/** Same rule as the API's AlbumExportFormatter.FileName. */
export function exportFileName(albumName: string): string {
  const safe = [...albumName]
    .map((char) => (isControl(char.charCodeAt(0)) || INVALID.has(char) ? '_' : char))
    .join('')
    .trim()
  return `${safe === '' ? 'album' : safe}.txt`
}

export function downloadText(fileName: string, text: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
