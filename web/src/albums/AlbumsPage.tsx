import PeopleAltOutlinedIcon from '@mui/icons-material/PeopleAltOutlined'
import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import {
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  CardMedia,
  Tooltip,
  Typography,
} from '@mui/material'
import { useId, useState, type ReactNode } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router'
import { useAlbums } from '../api/queries'
import type { AlbumSummary } from '../api/types'
import { HEADING_SX } from '../design/accent'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { UserAvatar } from '../shared/UserAvatar'
import { GridSkeleton } from '../views/GridSkeleton'
import { isOwner } from './access'
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
    const mine = albums.data.filter(isOwner)
    const shared = albums.data.filter((album) => !isOwner(album))
    body = (
      <Box
        className="bg-zinc-50/60 dark:bg-transparent"
        sx={{ flex: 1, overflowY: 'auto', p: 3, display: 'flex', flexDirection: 'column', gap: 4 }}
      >
        <AlbumSection
          title="My albums"
          albums={mine}
          empty="You haven't made any albums yet. Select photos anywhere and choose Add to album."
        />
        {shared.length > 0 && <AlbumSection title="Shared with me" albums={shared} />}
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
          px: 2.5,
          py: 1.25,
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="h6" component="h1" sx={{ fontWeight: 600, letterSpacing: '-0.01em' }}>
          Albums
        </Typography>
        <Box sx={{ flex: 1 }} />
        <Button
          variant="contained"
          size="small"
          disableElevation
          onClick={() => setCreating(true)}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
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

type AlbumSectionProps = { title: string; albums: AlbumSummary[]; empty?: string }

function AlbumSection({ title, albums, empty }: AlbumSectionProps) {
  const headingId = useId()
  return (
    <Box component="section" aria-labelledby={headingId}>
      <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, mb: 1.5 }}>
        <Typography id={headingId} variant="subtitle1" component="h2" sx={HEADING_SX}>
          {title}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ fontVariantNumeric: 'tabular-nums' }}>
          {albums.length}
        </Typography>
      </Box>
      {albums.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          {empty}
        </Typography>
      ) : (
        <Box
          component="ul"
          aria-labelledby={headingId}
          sx={{
            listStyle: 'none',
            m: 0,
            p: 0,
            display: 'grid',
            gap: 3,
            gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
          }}
        >
          {albums.map((album) => (
            <li key={album.id}>
              <AlbumCard album={album} />
            </li>
          ))}
        </Box>
      )}
    </Box>
  )
}

const COVER_HEIGHT = 160

function AlbumCard({ album }: { album: AlbumSummary }) {
  const owned = isOwner(album)
  const people = album.shareCount === 1 ? '1 person' : `${album.shareCount} people`
  return (
    <Card
      variant="outlined"
      className="overflow-hidden rounded-xl border-zinc-100 shadow-sm transition-all duration-200 ease-in-out hover:-translate-y-0.5 hover:shadow-md dark:border-zinc-800"
    >
      <CardActionArea component={RouterLink} to={`/albums/${album.id}`} aria-label={album.name}>
        <Box sx={{ position: 'relative' }}>
          {album.coverThumbnailUrl ? (
            <CardMedia
              component="img"
              image={album.coverThumbnailUrl}
              alt=""
              sx={{ height: COVER_HEIGHT, objectFit: 'cover' }}
            />
          ) : (
            <Box
              sx={{
                height: COVER_HEIGHT,
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
          {owned && album.shareCount > 0 && (
            <Tooltip title={`Shared with ${people}`}>
              <Box
                role="img"
                aria-label={`Shared with ${people}`}
                sx={{
                  position: 'absolute',
                  top: 8,
                  right: 8,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 0.5,
                  px: 0.75,
                  py: 0.25,
                  borderRadius: '999px',
                  bgcolor: 'rgba(17, 17, 26, 0.55)',
                  color: '#fff',
                  fontSize: 12,
                  fontWeight: 600,
                  backdropFilter: 'blur(4px)',
                }}
              >
                <PeopleAltOutlinedIcon sx={{ fontSize: 14 }} />
                {album.shareCount}
              </Box>
            </Tooltip>
          )}
          {!owned && (
            <Box
              sx={{
                position: 'absolute',
                left: 12,
                bottom: -14,
                borderRadius: '50%',
                border: 2,
                borderColor: 'background.paper',
                lineHeight: 0,
              }}
            >
              <UserAvatar displayName={album.ownerDisplayName} size={28} />
            </Box>
          )}
        </Box>
        <CardContent sx={!owned ? { pt: 2.5 } : undefined}>
          <Typography variant="subtitle1" component="h3" noWrap sx={{ fontWeight: 600 }}>
            {album.name}
          </Typography>
          <Typography variant="body2" color="text.secondary" noWrap>
            {owned
              ? `${photoCount(album.imageCount)} · Updated ${album.updatedAt.slice(0, 10)}`
              : `${photoCount(album.imageCount)} · Shared by ${album.ownerDisplayName}`}
          </Typography>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
