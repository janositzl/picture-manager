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

  it('shows a corrupt-file placeholder for an invalid image, without attempting to load a thumbnail', () => {
    render(
      <PhotoTile
        item={image(1, 3, { isInvalid: true })}
        size={180}
        caption={null}
        dimmed={false}
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('Corrupt file')).toBeInTheDocument()
    expect(screen.getByTestId('tile-1').querySelector('img')).not.toBeInTheDocument()
  })

  it('shows a placeholder when the file is missing', () => {
    render(
      <PhotoTile
        item={image(1, 3)}
        size={180}
        caption={null}
        dimmed={false}
        missing
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('File missing')).toBeInTheDocument()
  })

  it('Ctrl-click selects instead of opening, and the checkbox reports Shift', async () => {
    const onOpen = vi.fn()
    const onSelect = vi.fn()
    render(
      <PhotoTile
        item={image(7, 3)}
        size={180}
        caption={null}
        dimmed={false}
        selection={{ selecting: false, selected: false, onSelect }}
        onOpen={onOpen}
        onToggleFavorite={noop}
      />,
    )
    const user = userEvent.setup()
    await user.keyboard('{Control>}')
    await user.click(screen.getByRole('button', { name: 'IMG_0007.jpg' }))
    await user.keyboard('{/Control}')
    expect(onSelect).toHaveBeenLastCalledWith(7, { shift: false })
    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0007.jpg' }))
    await user.keyboard('{/Shift}')
    expect(onSelect).toHaveBeenLastCalledWith(7, { shift: true })
    expect(onOpen).not.toHaveBeenCalled()
  })
})
