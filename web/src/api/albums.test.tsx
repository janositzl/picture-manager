import { act, renderHook, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { imageDetail, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { useAddToAlbum, useMoveInAlbum } from './albums'
import { queryKeys, useAlbumImages, useAlbums } from './queries'
import type { AlbumImageItem, Page } from './types'

function setup() {
  const queryClient = createTestQueryClient()
  return { queryClient, wrapper: createWrapper(queryClient) }
}

const ids = (data: { pages: Page<AlbumImageItem>[] } | undefined) =>
  data?.pages.flatMap((p) => p.items.map((i) => i.id))

describe('album mutations', () => {
  it("adding photos refreshes the album list and the photos' details", async () => {
    const { queryClient, wrapper } = setup()
    const list = renderHook(() => useAlbums(), { wrapper })
    await waitFor(() => expect(list.result.current.data).toBeDefined())
    queryClient.setQueryData(queryKeys.image(22), imageDetail(madeiraImages[2]!))
    const { result } = renderHook(() => useAddToAlbum(), { wrapper })

    await act(() => result.current.mutateAsync({ albumId: 6, target: { imageIds: [22] } }))

    await waitFor(() =>
      expect(list.result.current.data?.find((a) => a.id === 6)?.imageCount).toBe(1),
    )
    expect(queryClient.getQueryState(queryKeys.image(22))?.isInvalidated).toBe(true)
  })

  it('moves optimistically and keeps the order when the server agrees', async () => {
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([21, 20]))

    act(() => result.current.move.mutate({ imageId: 20, afterImageId: null, order: [20, 21] }))

    await waitFor(() =>
      expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([20, 21]),
    )
    await waitFor(() => expect(result.current.move.isSuccess).toBe(true))
    expect(albumStore.get(5)!.imageIds).toEqual([20, 21])
  })

  it('puts the order back and says so when saving fails', async () => {
    server.use(
      http.post('/api/albums/:id/images/:imageId/move', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([21, 20]))

    act(() => result.current.move.mutate({ imageId: 20, afterImageId: null, order: [20, 21] }))

    await waitFor(() => expect(result.current.move.isError).toBe(true))
    expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([21, 20])
    expect(await screen.findByText("Couldn't save the new order.")).toBeInTheDocument()
  })

  it('two quick moves are sent in order and both stick', async () => {
    albumStore.get(5)!.imageIds = [20, 21, 22]
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([20, 21, 22]))

    act(() => {
      result.current.move.mutate({ imageId: 22, afterImageId: null, order: [22, 20, 21] })
      result.current.move.mutate({ imageId: 21, afterImageId: 22, order: [22, 21, 20] })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(albumStore.get(5)!.imageIds).toEqual([22, 21, 20])
    expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([22, 21, 20])
  })
})
