import { describe, expect, it } from 'vitest'
import { formatDay } from './dayFormat'

describe('formatDay', () => {
  it('keeps the calendar day whatever the local time zone', () => {
    const text = formatDay('2025-08-14')
    expect(text).toContain('14')
    expect(text).toContain('2025')
  })

  it('falls back to the raw text for something that is not a day', () =>
    expect(formatDay('soon')).toBe('soon'))
})
