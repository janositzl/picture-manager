import type { InfiniteData, QueryClient } from '@tanstack/react-query'
import { act, renderHook, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { imageDetail, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { useSetFavorite } from './favorites'
import type { ImageFilter } from './imageFilter'
import { queryKeys } from './queries'
import type { ImageDetail, ImageListItem, Page } from './types'

const folderFilter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }
const favoritesFilter: ImageFilter = { kind: 'favorites', sort: 'date', order: 'desc' }

function seed() {
  const queryClient = createTestQueryClient()
  const pageOf = (items: ImageListItem[]) => ({
    pages: [{ items, nextCursor: null }],
    pageParams: [null],
  })
  queryClient.setQueryData(queryKeys.images(folderFilter), pageOf(madeiraImages))
  queryClient.setQueryData(
    queryKeys.images(favoritesFilter),
    pageOf(madeiraImages.filter((i) => i.isFavorite)),
  )
  queryClient.setQueryData(queryKeys.image(20), imageDetail(madeiraImages[0]!))
  return { queryClient, wrapper: createWrapper(queryClient) }
}

function listItem(queryClient: QueryClient, filter: ImageFilter, id: number) {
  const data = queryClient.getQueryData<InfiniteData<Page<ImageListItem>>>(queryKeys.images(filter))
  return data?.pages.flatMap((p) => p.items).find((i) => i.id === id)
}

const detail = (queryClient: QueryClient, id: number) =>
  queryClient.getQueryData<ImageDetail>(queryKeys.image(id))

describe('useSetFavorite', () => {
  it('flips the star in every cached copy before the server answers', async () => {
    let release = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.put('/api/images/:id/favorite', async () => {
        await gate
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 20, isFavorite: true }))

    await waitFor(() => expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true))
    expect(detail(queryClient, 20)?.isFavorite).toBe(true)
    expect(result.current.isPending).toBe(true)

    release()
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true)
  })

  it('rolls back and notifies when the server refuses', async () => {
    server.use(
      http.put('/api/images/:id/favorite', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 20, isFavorite: true }))

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(detail(queryClient, 20)?.isFavorite).toBe(false)
    expect(await screen.findByText("Couldn't update favorite.")).toBeInTheDocument()
  })

  it('marks favorites lists stale without dropping the unstarred photo', async () => {
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 21, isFavorite: false }))

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(queryClient.getQueryState(queryKeys.images(favoritesFilter))?.isInvalidated).toBe(true)
    expect(listItem(queryClient, favoritesFilter, 21)?.isFavorite).toBe(false)
    expect(queryClient.getQueryState(queryKeys.images(folderFilter))?.isInvalidated).toBe(false)
  })

  it("a failure on one photo does not undo another photo's star", async () => {
    server.use(
      http.put('/api/images/:id/favorite', async ({ params }) => {
        if (params.id !== '20') return new HttpResponse(null, { status: 204 })
        await new Promise((resolve) => setTimeout(resolve, 50))
        return HttpResponse.json({ title: 'boom' }, { status: 500 })
      }),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => {
      result.current.mutate({ id: 20, isFavorite: true })
      result.current.mutate({ id: 22, isFavorite: true })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(listItem(queryClient, folderFilter, 22)?.isFavorite).toBe(true)
  })

  it('sends toggles in order, and an earlier failure does not override a later success', async () => {
    const calls: string[] = []
    let release = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    let puts = 0
    server.use(
      http.put('/api/images/:id/favorite', async () => {
        calls.push('PUT')
        if (++puts === 1) {
          await gate
          return HttpResponse.json({ title: 'boom' }, { status: 500 })
        }
        return new HttpResponse(null, { status: 204 })
      }),
      http.delete('/api/images/:id/favorite', () => {
        calls.push('DELETE')
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => {
      result.current.mutate({ id: 20, isFavorite: true })
      result.current.mutate({ id: 20, isFavorite: false })
      result.current.mutate({ id: 20, isFavorite: true })
    })
    await waitFor(() => expect(calls).toEqual(['PUT']))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true)

    release()
    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(calls).toEqual(['PUT', 'DELETE', 'PUT'])
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true)
  })

  it('when every toggle fails, the star returns to what the server has', async () => {
    server.use(
      http.put('/api/images/:id/favorite', () => HttpResponse.json({}, { status: 500 })),
      http.delete('/api/images/:id/favorite', () => HttpResponse.json({}, { status: 500 })),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => {
      result.current.mutate({ id: 20, isFavorite: true })
      result.current.mutate({ id: 20, isFavorite: false })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(detail(queryClient, 20)?.isFavorite).toBe(false)
  })

  it('two quick toggles end in the last state', async () => {
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => {
      result.current.mutate({ id: 20, isFavorite: true })
      result.current.mutate({ id: 20, isFavorite: false })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(detail(queryClient, 20)?.isFavorite).toBe(false)
  })
})
