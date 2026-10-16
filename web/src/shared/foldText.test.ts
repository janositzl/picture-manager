import { describe, expect, it } from 'vitest'
import { foldText } from './foldText'

describe('foldText', () => {
  it('drops accents and case', () => {
    expect(foldText('Ábel')).toBe('abel')
    expect(foldText('Márti')).toBe('marti')
    expect(foldText('ÁÉÖÜÚŰőű')).toBe('aeouuuou')
  })

  it('handles letters without a combining accent', () => {
    expect(foldText('Łukasz Đorđe')).toBe('lukasz dorde')
  })

  it('leaves plain text alone', () => {
    expect(foldText('anna smith')).toBe('anna smith')
  })
})
