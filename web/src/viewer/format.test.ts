import { describe, expect, it } from 'vitest'
import { formatBytes, formatDateTaken, openStreetMapUrl } from './format'

describe('formatBytes', () => {
  it.each([
    [500, '500 B'],
    [1536, '1.5 KB'],
    [2_400_000, '2.3 MB'],
    [5 * 1024 ** 3, '5.0 GB'],
  ])('%i → %s', (bytes, expected) => expect(formatBytes(bytes)).toBe(expected))
})

describe('formatDateTaken', () => {
  it('shows the camera time as written, without timezone conversion', () =>
    expect(formatDateTaken('2025-08-14T18:32:05')).toBe('2025-08-14 18:32'))
})

describe('openStreetMapUrl', () => {
  it('points a marker at the coordinates', () =>
    expect(openStreetMapUrl(32.6669, -16.9241)).toBe(
      'https://www.openstreetmap.org/?mlat=32.6669&mlon=-16.9241#map=16/32.6669/-16.9241',
    ))
})
