import { useSearchParams } from 'react-router'
import type { ImageFilter } from '../api/imageFilter'
import { parseGridParams, parseSearchState } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from './GridHeader'
import { ImageBrowser } from './ImageBrowser'

export function SearchView() {
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)
  const { q, in: scopeId } = parseSearchState(searchParams)

  if (q === '') return <EmptyMessage>Type a file name to search.</EmptyMessage>

  const filter: ImageFilter =
    scopeId === null
      ? { kind: 'search', q, sort, order }
      : { kind: 'search', q, in: scopeId, sort, order }

  return (
    <ImageBrowser
      filter={filter}
      header={<GridHeader title={`Search: ${q}`} sort={sort} order={order} />}
      captionFor={(item) => item.folderPath}
      emptyState={<EmptyMessage>No photos match “{q}”.</EmptyMessage>}
    />
  )
}
