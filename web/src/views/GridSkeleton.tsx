import { Box, Skeleton } from '@mui/material'
import { TILE_MIN_WIDTH, useTileSize } from '../grid/tileSize'

export function GridSkeleton() {
  const [tileSizeKey] = useTileSize()
  const size = TILE_MIN_WIDTH[tileSizeKey]
  return (
    <Box
      aria-busy="true"
      aria-label="Loading photos"
      className="p-2"
      sx={{ display: 'flex', flexWrap: 'wrap', gap: '4px' }}
    >
      {Array.from({ length: 12 }, (_, index) => (
        <Skeleton
          key={index}
          variant="rectangular"
          width={size}
          height={size}
          sx={{ borderRadius: '10px' }}
        />
      ))}
    </Box>
  )
}
