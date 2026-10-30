const DAY_FORMAT = new Intl.DateTimeFormat(undefined, {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  // The day is already the photo's wall-clock date; formatting it in the viewer's zone would shift it.
  timeZone: 'UTC',
})

/** "2025-08-14" as a readable date, without any time zone conversion. */
export function formatDay(day: string): string {
  const [year, month, date] = day.split('-').map(Number)
  if (!year || !month || !date) return day
  return DAY_FORMAT.format(new Date(Date.UTC(year, month - 1, date)))
}
