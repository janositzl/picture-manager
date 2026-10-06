import type { ImageFace } from '../api/people'

/** How a face is named to the user: "John", "Suggested: John", "Unknown" or "Ignored". */
export function faceLabel(face: ImageFace): string {
  if (face.state === 'ignored') return 'Ignored'
  if (face.state === 'suggested') return `Suggested: ${face.personName ?? 'someone'}`
  return face.personName ?? 'Unknown'
}
