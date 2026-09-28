import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward'
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
import ImageOutlinedIcon from '@mui/icons-material/ImageOutlined'
import { Box, Breadcrumbs, IconButton, Link, MenuItem, TextField, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink, useSearchParams } from 'react-router'
import type { BreadcrumbItem } from '../api/types'
import { withParams, type Order, type Sort } from '../routing/urlState'

type Props = {
  title: string
  path?: BreadcrumbItem[]
  count?: number
  sort: Sort
  order: Order
  actions?: ReactNode
}

export function GridHeader({ title, path, count, sort, order, actions }: Props) {
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
              <ImageOutlinedIcon sx={{ fontSize: 16, color: 'text.secondary' }} />
              <Typography variant="body2" color="text.secondary">
                {count === 1 ? '1 photo' : `${count} photos`}
              </Typography>
            </Box>
          )}
        </Box>
      </Box>
      <Box sx={{ flex: 1 }} />
      {actions}
      <TextField
        select
        size="small"
        label="Sort"
        value={sort}
        onChange={(event) =>
          setSearchParams(withParams(searchParams, { sort: event.target.value, order: null }))
        }
        sx={{ minWidth: 110, '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
      >
        <MenuItem value="date">Date</MenuItem>
        <MenuItem value="name">Name</MenuItem>
      </TextField>
      <IconButton
        aria-label={
          order === 'asc' ? 'Ascending, switch to descending' : 'Descending, switch to ascending'
        }
        onClick={() =>
          setSearchParams(withParams(searchParams, { order: order === 'asc' ? 'desc' : 'asc' }))
        }
        className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
      >
        {order === 'asc' ? <ArrowUpwardIcon fontSize="small" /> : <ArrowDownwardIcon fontSize="small" />}
      </IconButton>
    </Box>
  )
}
