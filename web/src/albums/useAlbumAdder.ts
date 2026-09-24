import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { useAddToAlbum, type AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { queryKeys } from '../api/queries'
import { useNotify } from '../app/notify'
import { addedMessage } from './messages'
import { readLastUsedAlbum, writeLastUsedAlbum } from './preferences'

export type AddOutcome = 'added' | 'gone' | 'failed'
type AlbumRef = { id: number; name: string }

/** Adds photos to an album and reports it; shared by the picker and the viewer's Shift+A. */
export function useAlbumAdder() {
  const add = useAddToAlbum()
  const notify = useNotify()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const addTo = async (
    album: AlbumRef,
    target: AddTarget,
    unavailable = 0,
  ): Promise<AddOutcome> => {
    try {
      const result = await add.mutateAsync({ albumId: album.id, target })
      writeLastUsedAlbum(album.id)
      notify(addedMessage(result, album.name, unavailable), {
        label: 'Open album',
        onClick: () => void navigate(`/albums/${album.id}`),
      })
      return 'added'
    } catch (error) {
      if (!isNotFound(error)) return 'failed'
      if (readLastUsedAlbum() === album.id) writeLastUsedAlbum(null)
      void queryClient.invalidateQueries({ queryKey: queryKeys.albums() })
      return 'gone'
    }
  }

  return { addTo, isAdding: add.isPending }
}
