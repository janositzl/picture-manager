import { Alert, Box, Button, FormControlLabel, Link, Switch, Typography } from '@mui/material'
import { useMemo, useState, type ReactNode } from 'react'
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router'
import { useDeleteAlbum, useMoveInAlbum, useRemoveFromAlbum, type AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { useSetFavorite } from '../api/favorites'
import { useAlbum, useAlbumImages } from '../api/queries'
import { useNotify } from '../app/notify'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, parseId, withParams } from '../routing/urlState'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from '../views/GridSkeleton'
import { AlbumFormDialog } from './AlbumFormDialog'
import { ExportDialog } from './ExportDialog'
import { AlbumGrid } from './AlbumGrid'
import { AlbumPicker } from './AlbumPicker'
import { photoCount } from './messages'
import { readShowFolders, writeShowFolders } from './preferences'
import { planMove } from './reorder'

const LARGE_ALBUM = 2000

type OpenDialog = 'edit' | 'delete' | 'remove' | 'export' | null
type PickerState = { target: AddTarget; unavailable: number }

export function AlbumView() {
  const params = useParams()
  const albumId = parseId(params.albumId ?? null)
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const navigate = useNavigate()
  const notify = useNotify()
  const album = useAlbum(albumId)
  const images = useAlbumImages(albumId)
  const setFavorite = useSetFavorite()
  const removeImages = useRemoveFromAlbum(albumId ?? 0)
  const moveImage = useMoveInAlbum(albumId ?? 0)
  const deleteAlbum = useDeleteAlbum(albumId ?? 0)
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const ids = useMemo(() => items.map((item) => item.id), [items])
  const selection = useSelection(ids, `album-${albumId}`)
  const [showFolders, setShowFolders] = useState(readShowFolders)
  const [dialog, setDialog] = useState<OpenDialog>(null)
  const [picker, setPicker] = useState<PickerState | null>(null)

  if (albumId === null || isNotFound(album.error)) {
    return (
      <Box sx={{ p: 3 }}>
        <Typography gutterBottom>Album not found.</Typography>
        <Link component={RouterLink} to="/albums">
          Back to albums
        </Link>
      </Box>
    )
  }
  if (album.isPending) return <GridSkeleton />
  if (album.isError) {
    return (
      <QueryErrorAlert message="Couldn't load this album." onRetry={() => void album.refetch()} />
    )
  }

  const detail = album.data
  const selectedIds = ids.filter((id) => selection.selected.has(id))

  const open = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { state: { viewer: true } })

  const toggleShowFolders = () => {
    const next = !showFolders
    setShowFolders(next)
    writeShowFolders(next)
  }

  // Missing files can't be added (the API refuses them), so they're left out and counted.
  const addSelection = () => {
    const missing = new Set(items.filter((item) => item.isMissing).map((item) => item.id))
    const available = selectedIds.filter((id) => !missing.has(id))
    if (available.length === 0) {
      notify("Photos whose file is missing can't be added to an album.")
      return
    }
    setPicker({
      target: { imageIds: available },
      unavailable: selectedIds.length - available.length,
    })
  }

  const confirmRemove = async () => {
    try {
      await removeImages.mutateAsync({ imageIds: selectedIds })
      notify(`Removed ${photoCount(selectedIds.length)} from ${detail.name}.`)
      selection.clear()
    } catch {
      notify("Couldn't remove photos.")
    }
    setDialog(null)
  }

  const confirmDelete = async () => {
    try {
      await deleteAlbum.mutateAsync()
      void navigate('/albums')
    } catch {
      notify("Couldn't delete the album.")
      setDialog(null)
    }
  }

  let body: ReactNode
  if (images.isError) {
    body = (
      <QueryErrorAlert
        message="Couldn't load this album's photos."
        onRetry={() => void images.refetch()}
      />
    )
  } else if (images.isPending || images.hasNextPage) {
    body = <GridSkeleton />
  } else if (items.length === 0) {
    body = (
      <EmptyMessage>
        This album is empty. Select photos anywhere and choose Add to album.
      </EmptyMessage>
    )
  } else {
    body = (
      <>
        {items.length > LARGE_ALBUM && (
          <Alert severity="info" sx={{ m: 2, mb: 0 }}>
            This album is large, so reordering may be slow.
          </Alert>
        )}
        <AlbumGrid
          items={items}
          showFolders={showFolders}
          selection={selection}
          onOpen={open}
          onToggleFavorite={(item) =>
            setFavorite.mutate({ id: item.id, isFavorite: !item.isFavorite })
          }
          onMove={(activeId, overId) => {
            const plan = planMove(ids, activeId, overId)
            if (plan !== null) moveImage.mutate({ imageId: activeId, ...plan })
          }}
        />
      </>
    )
  }

  const header = selection.isSelecting ? (
    <SelectionBar
      count={selection.count}
      onAddToAlbum={addSelection}
      onRemove={() => setDialog('remove')}
      onClear={selection.clear}
    />
  ) : (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        px: 2,
        py: 1,
        borderBottom: 1,
        borderColor: 'divider',
      }}
    >
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="h6" component="h1" noWrap>
          {detail.name}
        </Typography>
        {detail.description && (
          <Typography variant="body2" color="text.secondary" noWrap>
            {detail.description}
          </Typography>
        )}
      </Box>
      <Box sx={{ flex: 1 }} />
      <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
        {photoCount(detail.imageCount)}
      </Typography>
      <FormControlLabel
        control={<Switch size="small" checked={showFolders} onChange={toggleShowFolders} />}
        label="Show folders"
      />
      <Button size="small" onClick={() => setDialog('edit')}>
        Edit…
      </Button>
      <Button size="small" onClick={() => setDialog('export')}>
        Export…
      </Button>
      <Button size="small" color="error" onClick={() => setDialog('delete')}>
        Delete…
      </Button>
    </Box>
  )

  return (
    <>
      {header}
      {body}
      {image !== null && (
        <PhotoViewer
          list={{
            items,
            hasNextPage: images.hasNextPage,
            fetchNextPage: () => images.fetchNextPage(),
          }}
        />
      )}
      {picker !== null && (
        <AlbumPicker
          target={picker.target}
          unavailable={picker.unavailable}
          onClose={() => setPicker(null)}
          onAdded={selection.clear}
        />
      )}
      {dialog === 'edit' && (
        <AlbumFormDialog
          mode="edit"
          album={detail}
          onClose={() => setDialog(null)}
          onSaved={() => setDialog(null)}
        />
      )}
      {dialog === 'export' && (
        <ExportDialog
          album={detail}
          missingCount={items.filter((item) => item.isMissing).length}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'delete' && (
        <ConfirmDialog
          title="Delete album"
          message={`Delete album ${detail.name}? The photos stay in your library.`}
          confirmLabel="Delete"
          busy={deleteAlbum.isPending}
          onConfirm={() => void confirmDelete()}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'remove' && (
        <ConfirmDialog
          title="Remove photos"
          message={`Remove ${photoCount(selectedIds.length)} from ${detail.name}?`}
          confirmLabel="Remove"
          busy={removeImages.isPending}
          onConfirm={() => void confirmRemove()}
          onClose={() => setDialog(null)}
        />
      )}
    </>
  )
}
