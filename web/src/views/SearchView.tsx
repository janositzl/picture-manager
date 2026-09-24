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

  // An empty query lists every photo (within the scope, if set) rather than a prompt.
  const filter: ImageFilter =
    scopeId === null
      ? { kind: 'search', q, sort, order }
      : { kind: 'search', q, in: scopeId, sort, order }

  return (
    <ImageBrowser
      filter={filter}
      header={
        <GridHeader title={q === '' ? 'All photos' : `Search: ${q}`} sort={sort} order={order} />
      }
      captionFor={(item) => item.folderPath}
      emptyState={
        <EmptyMessage>{q === '' ? 'No photos yet.' : `No photos match “${q}”.`}</EmptyMessage>
      }
    />
  )
}
