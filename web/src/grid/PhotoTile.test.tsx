import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { image } from '../test/fixtures'
import { PhotoTile } from './PhotoTile'

const noop = () => {}

describe('PhotoTile', () => {
  it('shows a processing placeholder until the image has a thumbnail', () => {
    render(
      <PhotoTile
        item={image(1, 3, { thumbnailUrl: null, previewUrl: null })}
        size={180}
        caption={null}
        dimmed={false}
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('Processing')).toBeInTheDocument()
  })

  it('falls back to a broken-image icon when the thumbnail fails to load', () => {
    render(
      <PhotoTile
        item={image(1, 3)}
        size={180}
        caption={null}
        dimmed={false}
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    fireEvent.error(screen.getByTestId('tile-1').querySelector('img')!)
    expect(screen.getByText('Thumbnail unavailable')).toBeInTheDocument()
  })

  it('shows the file name on the tile, even without a caption', () => {
    render(
      <PhotoTile
        item={image(7, 3)}
        size={180}
        caption={null}
        dimmed={false}
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('IMG_0007.jpg')).toBeInTheDocument()
  })

  it('shows the caption and opens on Enter', async () => {
    const onOpen = vi.fn()
    render(
      <PhotoTile
        item={image(7, 3)}
        size={180}
        caption="dev/Holidays/Madeira"
        dimmed={false}
        onOpen={onOpen}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    screen.getByRole('button', { name: 'IMG_0007.jpg' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(onOpen).toHaveBeenCalledWith(7)
  })
})
