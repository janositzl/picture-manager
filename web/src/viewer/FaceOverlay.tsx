import { Box } from '@mui/material'
import { useEffect, useState } from 'react'
import type { FaceState, ImageFace } from '../api/people'
import { faceLabel } from './faceLabel'

const BORDER: Record<FaceState, string> = {
  confirmed: '#4caf50',
  suggested: '#ffb300',
  unknown: '#ef5350',
  ignored: '#9e9e9e',
}

type Rect = { left: number; top: number; width: number; height: number }

/** Where the image is drawn inside its (relatively positioned) stage, tracked through resizes. */
function useRenderedRect(image: HTMLImageElement | null): Rect | null {
  const [rect, setRect] = useState<Rect | null>(null)
  useEffect(() => {
    if (image === null) return
    const measure = () =>
      setRect({ left: image.offsetLeft, top: image.offsetTop, width: image.clientWidth, height: image.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(image)
    if (image.parentElement) observer.observe(image.parentElement)
    return () => observer.disconnect()
  }, [image])
  return image === null ? null : rect
}

type Props = {
  image: HTMLImageElement | null
  faces: readonly ImageFace[]
  hoveredId: number | null
  selectedId: number | null
  onHover: (id: number | null) => void
  onSelect: (id: number) => void
}

/** Rectangles over the photo, one per detected face; kept in step with the People-in-photo list. */
export function FaceOverlay({ image, faces, hoveredId, selectedId, onHover, onSelect }: Props) {
  const rect = useRenderedRect(image)
  if (rect === null || rect.width === 0) return null

  return (
    <Box
      aria-label="Detected faces"
      sx={{ position: 'absolute', left: rect.left, top: rect.top, width: rect.width, height: rect.height, pointerEvents: 'none' }}
    >
      {faces.map((face) => {
        const active = face.id === hoveredId || face.id === selectedId
        const label = faceLabel(face)
        return (
          <Box
            key={face.id}
            component="button"
            type="button"
            aria-label={`Face: ${label}`}
            aria-pressed={face.id === selectedId}
            onMouseEnter={() => onHover(face.id)}
            onMouseLeave={() => onHover(null)}
            onFocus={() => onHover(face.id)}
            onBlur={() => onHover(null)}
            onClick={() => onSelect(face.id)}
            sx={{
              position: 'absolute',
              left: `${face.x * 100}%`,
              top: `${face.y * 100}%`,
              width: `${face.width * 100}%`,
              height: `${face.height * 100}%`,
              p: 0,
              bgcolor: active ? 'rgba(255,255,255,0.15)' : 'transparent',
              border: `${active ? 3 : 2}px ${face.state === 'suggested' || face.state === 'ignored' ? 'dashed' : 'solid'} ${BORDER[face.state]}`,
              borderRadius: '4px',
              cursor: 'pointer',
              pointerEvents: 'auto',
              transition: 'border-width 0.1s ease-in-out',
            }}
          >
            {active && (
              <Box
                component="span"
                sx={{ position: 'absolute', left: 0, top: '100%', mt: 0.5, px: 0.75, py: 0.25, whiteSpace: 'nowrap', fontSize: 12, color: 'common.white', bgcolor: 'rgba(0,0,0,0.75)', borderRadius: '4px' }}
              >
                {label}
              </Box>
            )}
          </Box>
        )
      })}
    </Box>
  )
}
