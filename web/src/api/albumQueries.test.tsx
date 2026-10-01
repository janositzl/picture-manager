import type { InfiniteData } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { duplicateGroups, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { patchFavorite } from './favorites'
import { queryKeys, useAlbumImages, useAlbums, useDuplicates } from './queries'
import type { DuplicateGroup, Page, SimilarGroup } from './types'

function setup() {
  const queryClient = createTestQueryClient()
  return { queryClient, wrapper: createWrapper(queryClient) }
}

describe('album and duplicate queries', () => {
  it('lists albums most recently updated first', async () => {
    server.use(
      http.get('/api/albums', () =>
        HttpResponse.json([
          {
            id: 1,
            name: 'Old',
            description: null,
            imageCount: 0,
            coverThumbnailUrl: null,
            updatedAt: '2026-01-01T00:00:00Z',
          },
          {
            id: 2,
            name: 'New',
            description: null,
            imageCount: 0,
            coverThumbnailUrl: null,
            updatedAt: '2026-05-01T00:00:00Z',
          },
        ]),
      ),
    )
    const { result } = renderHook(() => useAlbums(), { wrapper: setup().wrapper })
    await waitFor(() => expect(result.current.data?.map((a) => a.name)).toEqual(['New', 'Old']))
  })

  it('loads every page of an album', async () => {
    server.use(
      http.get('/api/albums/:id/images', ({ request }) =>
        new URL(request.url).searchParams.get('cursor') === null
          ? HttpResponse.json({
              items: [{ ...madeiraImages[0]!, isMissing: false }],
              nextCursor: 'c1',
            })
          : HttpResponse.json({
              items: [{ ...madeiraImages[1]!, isMissing: false }],
              nextCursor: null,
            }),
      ),
    )
    const { result } = renderHook(() => useAlbumImages(5), { wrapper: setup().wrapper })
    await waitFor(() =>
      expect(result.current.data?.pages.flatMap((p) => p.items.map((i) => i.id))).toEqual([20, 21]),
    )
    expect(result.current.hasNextPage).toBe(false)
  })

  it('loads duplicate groups page by page', async () => {
    const { result } = renderHook(() => useDuplicates(), { wrapper: setup().wrapper })
    await waitFor(() => expect(result.current.data?.pages).toHaveLength(1))
    expect(result.current.hasNextPage).toBe(true)
  })

  it('a star change also updates copies inside cached duplicate groups', () => {
    const { queryClient } = setup()
    queryClient.setQueryData<InfiniteData<Page<DuplicateGroup>, string | null>>(
      queryKeys.duplicates(),
      { pages: [{ items: duplicateGroups, nextCursor: null }], pageParams: [null] },
    )
    patchFavorite(queryClient, 20, true)
    const data = queryClient.getQueryData<InfiniteData<Page<DuplicateGroup>>>(
      queryKeys.duplicates(),
    )
    expect(data?.pages[0]?.items[0]?.images.find((i) => i.id === 20)?.isFavorite).toBe(true)
  })

  it('a star change also updates copies inside cached similar groups', () => {
    const { queryClient } = setup()
    const group: SimilarGroup = {
      key: 'S1',
      count: 2,
      maxDistance: 3,
      images: duplicateGroups[0]!.images,
    }
    queryClient.setQueryData<InfiniteData<Page<SimilarGroup>, string | null>>(
      queryKeys.similarDuplicates(),
      { pages: [{ items: [group], nextCursor: null }], pageParams: [null] },
    )
    patchFavorite(queryClient, 20, true)
    const data = queryClient.getQueryData<InfiniteData<Page<SimilarGroup>>>(
      queryKeys.similarDuplicates(),
    )
    expect(data?.pages[0]?.items[0]?.images.find((i) => i.id === 20)?.isFavorite).toBe(true)
  })
})
