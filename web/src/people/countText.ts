import type { PersonSummary } from '../api/people'

/** "20 (5)": 20 confirmed images, 5 more suggested. Unknown groups only have suggestions. */
export function countText(person: PersonSummary): string {
  if (person.name === null) return String(person.suggestedImageCount)
  return person.suggestedImageCount > 0
    ? `${person.confirmedImageCount} (${person.suggestedImageCount})`
    : String(person.confirmedImageCount)
}
