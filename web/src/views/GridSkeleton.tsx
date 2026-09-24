import { Box, Skeleton } from '@mui/material'

export function GridSkeleton() {
  return (
    <Box
      aria-busy="true"
      aria-label="Loading photos"
      sx={{ display: 'flex', flexWrap: 'wrap', gap: '4px' }}
    >
      {Array.from({ length: 12 }, (_, index) => (
        <Skeleton key={index} variant="rectangular" width={180} height={180} />
      ))}
    </Box>
  )
}
