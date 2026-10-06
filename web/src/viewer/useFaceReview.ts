import { useState } from 'react'
import { useImageFaces, type ImageFace } from '../api/people'

export type FaceReview = {
  faces: readonly ImageFace[]
  isLoading: boolean
  isError: boolean
  hoveredId: number | null
  selectedId: number | null
  showIgnored: boolean
  setHoveredId: (id: number | null) => void
  select: (id: number) => void
  setShowIgnored: (show: boolean) => void
}

/**
 * Face-review state for the viewer: the faces of the current photo and which one is hovered/selected.
 * Only active (and only fetching) in face-review mode; selection starts on the opened person's face, or without a person
 * on the first face that still needs a decision.
 */
export function useFaceReview(imageId: number | null, review: { personId: number | null } | undefined): FaceReview {
  const [showIgnored, setShowIgnored] = useState(false)
  const [hoveredId, setHoveredId] = useState<number | null>(null)
  const [selection, setSelection] = useState<{ imageId: number | null; faceId: number } | null>(null)
  const query = useImageFaces(imageId, { includeIgnored: showIgnored, enabled: review !== undefined })

  const faces = query.data ?? []
  const preferred =
    (review?.personId == null
      ? (faces.find((f) => f.state === 'unknown' || f.state === 'suggested') ?? faces[0])
      : faces.find((f) => f.personId === review.personId && (f.state === 'suggested' || f.state === 'confirmed'))
    )?.id ?? null
  // A selection belongs to the photo it was made in; stepping to another photo starts from the person's own face.
  const chosen = selection?.imageId === imageId ? selection.faceId : null
  const selectedId = chosen !== null && faces.some((f) => f.id === chosen) ? chosen : preferred

  return {
    faces,
    isLoading: query.isPending && query.fetchStatus !== 'idle',
    isError: query.isError,
    hoveredId,
    selectedId,
    showIgnored,
    setHoveredId,
    select: (faceId) => setSelection({ imageId, faceId }),
    setShowIgnored,
  }
}
