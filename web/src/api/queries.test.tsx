import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import type { ImageFilter } from './imageFilter'
import { useImages } from './queries'

const folderFilter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }

describe('useImages', () => {
  it('requests the filter as query params and follows nextCursor', async () => {
    const seen: string[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const url = new URL(request.url)
        seen.push(url.search)
        return url.searchParams.get('cursor') === null
          ? HttpResponse.json({ items: [madeiraImages[0]], nextCursor: 'p1' })
          : HttpResponse.json({ items: [madeiraImages[1]], nextCursor: null })
      }),
    )
    const wrapper = createWrapper(createTestQueryClient())
    const { result } = renderHook(() => useImages(folderFilter), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.hasNextPage).toBe(true)
    await act(() => result.current.fetchNextPage())

    // Query results reach the hook through a batched notification, after fetchNextPage resolves.
    await waitFor(() => expect(result.current.hasNextPage).toBe(false))
    expect(result.current.data?.pages.flatMap((p) => p.items).map((i) => i.id)).toEqual([20, 21])
    expect(seen).toEqual([
      '?folderId=3&sort=date&order=desc&limit=100',
      '?folderId=3&sort=date&order=desc&limit=100&cursor=p1',
    ])
  })
})
