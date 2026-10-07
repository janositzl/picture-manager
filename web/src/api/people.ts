import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from './client'
import { queryKeys } from './queries'

/** Counts are distinct images: confirmed, and suggested but not yet confirmed. */
export type PersonSummary = {
  id: number
  name: string | null
  confirmedImageCount: number
  suggestedImageCount: number
  coverFaceId: number | null
}

export type FaceState = 'unknown' | 'suggested' | 'confirmed' | 'ignored'

/** A detected face; the box is normalized (0-1) against the orientation-corrected image. */
export type ImageFace = {
  id: number
  x: number
  y: number
  width: number
  height: number
  state: FaceState
  personId: number | null
  personName: string | null
}

export type AssignTarget = { personId: number } | { name: string }

export const peopleKeys = {
  all: () => ['people'] as const,
  one: (id: number) => ['people', id] as const,
}

export const faceKeys = {
  all: () => ['faces'] as const,
  forImage: (imageId: number, mode: string) => ['faces', imageId, mode] as const,
}

export function faceThumbnailUrl(faceId: number): string {
  return `/api/faces/${faceId}/thumbnail`
}

export function usePeople() {
  return useQuery({
    queryKey: peopleKeys.all(),
    queryFn: () => apiFetch<PersonSummary[]>('/api/people'),
  })
}

export function usePerson(id: number) {
  return useQuery({
    queryKey: peopleKeys.one(id),
    queryFn: () => apiFetch<PersonSummary>(`/api/people/${id}`),
  })
}

/** The "Unknown #n" label of an unnamed group: its place among the unnamed groups, by id. */
export function unknownLabels(people: readonly PersonSummary[]): Map<number, string> {
  const labels = new Map<number, string>()
  people
    .filter((p) => p.name === null)
    .sort((a, b) => a.id - b.id)
    .forEach((p, index) => labels.set(p.id, `Unknown #${index + 1}`))
  return labels
}

/** Names a person; naming one after an existing person merges them, so the response may be a different id. */
export function useNamePerson() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) =>
      apiFetch<PersonSummary>(`/api/people/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name }),
      }),
    onSuccess: invalidate,
  })
}

/** Chooses which of the person's faces is shown as their cover. */
export function useSetPersonCover() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({ id, faceId }: { id: number; faceId: number }) =>
      apiFetch<PersonSummary>(`/api/people/${id}/cover`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ faceId }),
      }),
    onSuccess: invalidate,
  })
}

/** Forgets a person: their faces become unassigned and may regroup on the next recognition run. */
export function useDeletePerson() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: (id: number) => apiFetch<void>(`/api/people/${id}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

/** Ignores an unnamed group for good; resolves to how many faces were ignored. */
export function useIgnoreGroup() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: (id: number) => post<{ count: number }>(`/api/people/${id}/ignore`),
    onSuccess: invalidate,
  })
}

type ImageFacesOptions = { includeIgnored?: boolean; confirmedOnly?: boolean; enabled?: boolean }

/** The detected faces of one photo. confirmedOnly = what the normal viewer may show. */
export function useImageFaces(imageId: number | null, options: ImageFacesOptions = {}) {
  const { includeIgnored = false, confirmedOnly = false, enabled = true } = options
  const params = new URLSearchParams()
  if (includeIgnored) params.set('includeIgnored', 'true')
  if (confirmedOnly) params.set('confirmedOnly', 'true')
  return useQuery({
    queryKey: faceKeys.forImage(imageId ?? 0, params.toString()),
    queryFn: ({ signal }) =>
      apiFetch<ImageFace[]>(`/api/images/${imageId}/faces?${params}`, { signal }),
    enabled: enabled && imageId !== null,
  })
}

/** After any change to faces or people: counts, face lists and every photo grid are stale. */
function useInvalidatePeopleData() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: peopleKeys.all() })
    void queryClient.invalidateQueries({ queryKey: faceKeys.all() })
    void queryClient.invalidateQueries({ queryKey: queryKeys.imageLists() })
  }
}

export type FaceAction = 'accept' | 'reject' | 'unknown' | 'ignore' | 'restore'

const post = <T>(path: string, body?: unknown) =>
  apiFetch<T>(path, {
    method: 'POST',
    ...(body === undefined
      ? {}
      : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
  })

/** One decision about one detected face. */
export function useFaceAction() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({ faceId, action }: { faceId: number; action: FaceAction }) =>
      post<void>(`/api/faces/${faceId}/${action}`),
    onSuccess: invalidate,
  })
}

/** Confirms a face for an existing person, or for the person with this name (created if new). */
export function useAssignFace() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({ faceId, target }: { faceId: number; target: AssignTarget }) =>
      post<PersonSummary>(`/api/faces/${faceId}/assign`, target),
    onSuccess: invalidate,
  })
}

/** Merges an unnamed group (or just its faces on imageIds) into a named person, confirming them; resolves to the target person. */
export function useAssignGroup() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({
      id,
      personId,
      imageIds,
    }: {
      id: number
      personId: number
      imageIds?: number[]
    }) =>
      post<PersonSummary>(
        `/api/people/${id}/assign`,
        imageIds === undefined ? { personId } : { personId, imageIds },
      ),
    onSuccess: invalidate,
  })
}

/** Runs face detection again on one photo right now; resolves to how many faces it has and how many got a suggestion. */
export function useReanalyzeImage() {
  const invalidate = useInvalidatePeopleData()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      imageId,
      preset = 'detailed',
    }: {
      imageId: number
      preset?: 'fast' | 'detailed'
    }) =>
      post<{ faces: number; suggested: number }>(`/api/images/${imageId}/faces/reanalyze`, {
        preset,
      }),
    onSuccess: () => {
      invalidate()
      void queryClient.invalidateQueries({ queryKey: queryKeys.faceCoverage() })
    },
  })
}

/** Asks the matcher again about a photo's Unknown faces; resolves to how many now have a suggestion. */
export function useRecheckFaces() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: (imageId: number) =>
      post<{ count: number }>(`/api/images/${imageId}/faces/recheck`),
    onSuccess: invalidate,
  })
}

/** Accept all of a person's suggestions; resolves to how many faces were confirmed. */
export function useAcceptAllSuggestions() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: (personId: number) =>
      post<{ count: number }>(`/api/people/${personId}/suggestions/accept`),
    onSuccess: invalidate,
  })
}

/** Accept or reject one person's suggestion in one photo. */
export function useImageSuggestion() {
  const invalidate = useInvalidatePeopleData()
  return useMutation({
    mutationFn: ({
      personId,
      imageId,
      action,
    }: {
      personId: number
      imageId: number
      action: 'accept' | 'reject'
    }) => post<void>(`/api/people/${personId}/images/${imageId}/${action}`),
    onSuccess: invalidate,
  })
}
