const UNITS = ['KB', 'MB', 'GB', 'TB']

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(1)} ${UNITS[unit] ?? 'TB'}`
}

/** The camera clock has no timezone, so show it as written ("2025-08-14T18:32:05" → "2025-08-14 18:32"). */
export function formatDateTaken(value: string): string {
  return value.replace('T', ' ').slice(0, 16)
}

export function openStreetMapUrl(latitude: number, longitude: number): string {
  return `https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=16/${latitude}/${longitude}`
}
