import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from './client'
import { queryKeys } from './queries'

export type PersonSummary = {
  id: number
  name: string | null
  faceCount: number
  photoCount: number
  coverFaceId: number | null
}

export const peopleKeys = {
  all: () => ['people'] as const,
  one: (id: number) => ['people', id] as const,
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

/** Names a person; naming one after an existing person merges them, so the response may be a different id. */
export function useNamePerson() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) =>
      apiFetch<PersonSummary>(`/api/people/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name }),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: peopleKeys.all() })
      void queryClient.invalidateQueries({ queryKey: queryKeys.imageLists() })
    },
  })
}