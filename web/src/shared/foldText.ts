// Letters that carry no combining accent in Unicode, so NFD can't split them.
const SPECIAL: Record<string, string> = { ł: 'l', đ: 'd', ø: 'o', ß: 'ss', æ: 'ae', œ: 'oe' }

/** Lower-cases and drops accents (Ábel -> abel, Márti -> marti, ő/ű -> o/u) so names match however they are typed. */
export function foldText(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .replace(/[łđøßæœ]/g, (char) => SPECIAL[char] ?? char)
}
