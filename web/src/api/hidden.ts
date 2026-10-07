import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import { faceKeys, peopleKeys } from './people'
import { queryKeys } from './queries'

type HiddenChange = { imageIds: number[]; isHidden: boolean }

/** Hides or unhides photos in bulk, then refreshes everything that lists or counts photos. */
export function useSetHidden() {
  const queryClient = useQueryClient()
  const notify = useNotify()

  return useMutation({
    mutationFn: ({ imageIds, isHidden }: HiddenChange) =>
      apiFetch<{ affected: number }>('/api/images/hidden', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ imageIds, isHidden }),
      }),
    onSuccess: (_data, { imageIds, isHidden }) => {
      for (const queryKey of [
        ['images'],
        queryKeys.duplicates(),
        ['folders'],
        ['albums'],
        peopleKeys.all(),
        faceKeys.all(),
      ]) {
        void queryClient.invalidateQueries({ queryKey })
      }
      const count = imageIds.length
      notify(`${count} photo${count === 1 ? '' : 's'} ${isHidden ? 'hidden' : 'unhidden'}.`)
    },
    onError: () => notify("Couldn't update the photos."),
  })
}
