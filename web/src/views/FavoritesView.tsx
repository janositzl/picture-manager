import { useSearchParams } from 'react-router'
import { parseGridParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from './GridHeader'
import { ImageBrowser } from './ImageBrowser'

export function FavoritesView() {
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)

  return (
    <ImageBrowser
      filter={{ kind: 'favorites', sort, order }}
      header={<GridHeader title="Favorites" sort={sort} order={order} />}
      dimUnfavorited
      emptyState={
        <EmptyMessage>No favorites yet. Star a photo with ★ or F in the viewer.</EmptyMessage>
      }
    />
  )
}
