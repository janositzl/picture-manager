import { describe, expect, it } from 'vitest'
import { exportFileName } from './exportFile'

describe('exportFileName', () => {
  it.each([
    ['Best of 2025', 'Best of 2025.txt'],
    ['Madeira: best?', 'Madeira_ best_.txt'],
    ['a/b\\c', 'a_b_c.txt'],
    ['   ', 'album.txt'],
  ])('%s → %s', (name, expected) => expect(exportFileName(name)).toBe(expected))
})
