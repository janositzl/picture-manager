import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import { queryKeys } from './queries'

type RotationChange = { imageIds: number[]; degrees: 90 | 180 | 270 }

/**
 * Turns photos' thumbnails clockwise (the originals are untouched), then refreshes every list of photos:
 * the rotation is part of the thumbnail URL's version, so the new URL is what busts the browser cache.
 */
export function useRotateThumbnails() {
  const queryClient = useQueryClient()
  const notify = useNotify()

  return useMutation({
    mutationFn: ({ imageIds, degrees }: RotationChange) =>
      apiFetch<{ affected: number }>('/api/images/thumbnail-rotation', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ imageIds, degrees }),
      }),
    onSuccess: () => {
      for (const queryKey of [['images'], queryKeys.duplicates(), ['albums']]) {
        void queryClient.invalidateQueries({ queryKey })
      }
    },
    onError: () => notify("Couldn't rotate the thumbnails."),
  })
}
