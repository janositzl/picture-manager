import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward'
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
import ImageOutlinedIcon from '@mui/icons-material/ImageOutlined'
import SyncAltIcon from '@mui/icons-material/SyncAlt'
import { Box, Breadcrumbs, IconButton, Link, MenuItem, TextField, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink, useSearchParams } from 'react-router'
import type { BreadcrumbItem } from '../api/types'
import { BORDER } from '../design/accent'
import { TileSizeToggle } from '../grid/TileSizeToggle'
import { withParams, type Order, type Sort } from '../routing/urlState'

type Props = {
  title: string
  path?: BreadcrumbItem[]
  count?: number
  sort: Sort
  order: Order
  actions?: ReactNode
  /** Rendered right after the title (e.g. an edit button). */
  titleAdornment?: ReactNode
}

export function GridHeader({ title, path, count, sort, order, actions, titleAdornment }: Props) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <Box
      className="backdrop-blur-sm"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        px: 2.5,
        py: 1.25,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'background.paper',
      }}
    >
      <Box sx={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 0.25 }}>
        {path && (
          <Breadcrumbs
            aria-label="Folder path"
            sx={{ minWidth: 0, color: 'text.secondary', fontSize: 13 }}
          >
            {path.map((crumb) => (
              <Link
                key={crumb.id}
                component={RouterLink}
                to={`/folders/${crumb.id}`}
                underline="hover"
                color="inherit"
                className="transition-colors duration-200 ease-in-out"
              >
                {crumb.name}
              </Link>
            ))}
          </Breadcrumbs>
        )}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, minWidth: 0 }}>
          <Typography
            variant="h6"
            component="h1"
            noWrap
            sx={{ flexShrink: 0, fontWeight: 600, letterSpacing: '-0.01em' }}
          >
            {title}
          </Typography>
          {titleAdornment}
          {count !== undefined && (
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 0.5,
                flexShrink: 0,
                px: 1,
                py: 0.25,
                borderRadius: '999px',
                bgcolor: '#edeef3',
              }}
            >
              <ImageOutlinedIcon sx={{ fontSize: 16, color: '#6B7180' }} />
              <Typography sx={{ fontSize: '12px', color: '#6B7180' }}>
                {count === 1 ? '1 photo' : `${count} photos`}
              </Typography>
            </Box>
          )}
        </Box>
      </Box>
      <Box sx={{ flex: 1 }} />
      {actions}
      <TileSizeToggle />
      <Box
        sx={{
          display: 'flex',
          alignItems: 'stretch',
          border: `1px solid ${BORDER}`,
          borderRadius: '10px',
          bgcolor: 'background.paper',
          overflow: 'hidden',
        }}
      >
        <TextField
          select
          size="small"
          variant="standard"
          value={sort}
          onChange={(event) =>
            setSearchParams(withParams(searchParams, { sort: event.target.value, order: null }))
          }
          slotProps={{
            input: { disableUnderline: true },
            select: {
              'aria-label': 'Sort',
              renderValue: () => (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                  <SyncAltIcon
                    fontSize="small"
                    sx={{ transform: 'rotate(90deg)', color: 'text.secondary' }}
                  />
                  <Typography variant="body2" color="text.secondary" component="span">
                    Sort
                  </Typography>
                  <Typography variant="body2" component="span" sx={{ fontWeight: 500 }}>
                    {sort === 'name' ? 'Name' : 'Date'}
                  </Typography>
                </Box>
              ),
            },
          }}
          sx={{
            minWidth: 150,
            px: 1.25,
            display: 'flex',
            alignItems: 'center',
            '& .MuiSelect-select': { display: 'flex', alignItems: 'center', py: '8px' },
          }}
        >
          <MenuItem value="date">Date</MenuItem>
          <MenuItem value="name">Name</MenuItem>
        </TextField>
        <Box sx={{ width: '1px', bgcolor: BORDER }} />
        <IconButton
          aria-label={
            order === 'asc' ? 'Ascending, switch to descending' : 'Descending, switch to ascending'
          }
          onClick={() =>
            setSearchParams(withParams(searchParams, { order: order === 'asc' ? 'desc' : 'asc' }))
          }
          className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
          sx={{ borderRadius: 0 }}
        >
          {order === 'asc' ? (
            <ArrowUpwardIcon fontSize="small" />
          ) : (
            <ArrowDownwardIcon fontSize="small" />
          )}
        </IconButton>
      </Box>
    </Box>
  )
}
