import { describe, expect, it } from 'vitest'
import { addedMessage, photoCount } from './messages'

describe('messages', () => {
  it('counts photos', () => {
    expect(photoCount(1)).toBe('1 photo')
    expect(photoCount(3)).toBe('3 photos')
  })

  it('says what was added and what was already there or missing', () => {
    expect(addedMessage({ added: 5, skipped: 0 }, 'Trip')).toBe('Added 5 photos to Trip.')
    expect(addedMessage({ added: 1, skipped: 2 }, 'Trip')).toBe(
      'Added 1 photo to Trip (2 were already there).',
    )
    expect(addedMessage({ added: 2, skipped: 1 }, 'Trip', 1)).toBe(
      'Added 2 photos to Trip (1 was already there; 1 missing on disk was skipped).',
    )
  })
})
