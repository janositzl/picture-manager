import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import {
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  CardMedia,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router'
import { useAlbums } from '../api/queries'
import type { AlbumSummary } from '../api/types'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { GridSkeleton } from '../views/GridSkeleton'
import { AlbumFormDialog } from './AlbumFormDialog'
import { photoCount } from './messages'

export function AlbumsPage() {
  const albums = useAlbums()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)

  let body: ReactNode
  if (albums.isPending) {
    body = <GridSkeleton />
  } else if (albums.isError) {
    body = <QueryErrorAlert message="Couldn't load albums." onRetry={() => void albums.refetch()} />
  } else if (albums.data.length === 0) {
    body = (
      <EmptyMessage>
        No albums yet. Create one, or select photos anywhere and choose Add to album.
      </EmptyMessage>
    )
  } else {
    body = (
      <Box
        component="ul"
        aria-label="Albums"
        sx={{
          listStyle: 'none',
          m: 0,
          p: 2,
          display: 'grid',
          gap: 2,
          gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
          alignContent: 'start',
          overflowY: 'auto',
          flex: 1,
        }}
      >
        {albums.data.map((album) => (
          <li key={album.id}>
            <AlbumCard album={album} />
          </li>
        ))}
      </Box>
    )
  }

  return (
    <>
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
        <Typography variant="h6" component="h1">
          Albums
        </Typography>
        <Box sx={{ flex: 1 }} />
        <Button variant="contained" size="small" onClick={() => setCreating(true)}>
          New album
        </Button>
      </Box>
      {body}
      {creating && (
        <AlbumFormDialog
          mode="create"
          onClose={() => setCreating(false)}
          onSaved={(album) => {
            setCreating(false)
            void navigate(`/albums/${album.id}`)
          }}
        />
      )}
    </>
  )
}

function AlbumCard({ album }: { album: AlbumSummary }) {
  return (
    <Card variant="outlined">
      <CardActionArea component={RouterLink} to={`/albums/${album.id}`} aria-label={album.name}>
        {album.coverThumbnailUrl ? (
          <CardMedia
            component="img"
            image={album.coverThumbnailUrl}
            alt=""
            sx={{ height: 160, objectFit: 'cover' }}
          />
        ) : (
          <Box
            sx={{
              height: 160,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: 'action.hover',
              color: 'text.secondary',
            }}
          >
            <PhotoLibraryOutlinedIcon fontSize="large" />
          </Box>
        )}
        <CardContent>
          <Typography variant="subtitle1" component="h2" noWrap>
            {album.name}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {photoCount(album.imageCount)} · Updated {album.updatedAt.slice(0, 10)}
          </Typography>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
