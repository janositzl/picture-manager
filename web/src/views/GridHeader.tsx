import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward'
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
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
      <Typography variant="h6" component="h1" noWrap sx={{ flexShrink: 0 }}>
        {title}
      </Typography>
      {path && (
        <Breadcrumbs
          aria-label="Folder path"
          sx={{ minWidth: 0, color: 'text.secondary', fontSize: 14 }}
        >
          {path.map((crumb) => (
            <Link
              key={crumb.id}
              component={RouterLink}
              to={`/folders/${crumb.id}`}
              underline="hover"
              color="inherit"
            >
              {crumb.name}
            </Link>
          ))}
        </Breadcrumbs>
      )}
      <Box sx={{ flex: 1 }} />
      {count !== undefined && (
        <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
          {count === 1 ? '1 photo' : `${count} photos`}
        </Typography>
      )}
      {actions}
      <TextField
        select
        size="small"
        label="Sort"
        value={sort}
        onChange={(event) =>
          setSearchParams(withParams(searchParams, { sort: event.target.value, order: null }))
        }
        sx={{ minWidth: 110 }}
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
      >
        {order === 'asc' ? <ArrowUpwardIcon /> : <ArrowDownwardIcon />}
      </IconButton>
    </Box>
  )
}
