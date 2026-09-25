import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

afterEach(() => vi.restoreAllMocks())

const preview = () => screen.getByLabelText('Export preview')

async function openExport() {
  const view = renderApp('/albums/5')
  await view.user.click(await screen.findByRole('button', { name: 'Export…' }))
  await screen.findByRole('dialog', { name: 'Export album' })
  return view
}

describe('ExportDialog', () => {
  it('previews the path list, applies the prefix, and remembers it', async () => {
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('/dev/Holidays/Madeira/IMG_0002.jpg'))
    expect(screen.getByText('2 lines')).toBeInTheDocument()
    await user.type(screen.getByRole('textbox', { name: 'Path prefix' }), '/mnt/frame')
    await waitFor(() =>
      expect(preview()).toHaveTextContent('/mnt/frame/dev/Holidays/Madeira/IMG_0002.jpg'),
    )
    expect(localStorage.getItem('pm.albums.exportPrefix')).toBe('/mnt/frame')
  })

  it('downloads a file named after the album', async () => {
    const created: Blob[] = []
    // jsdom has no object URLs; these stay defined for later tests, which is harmless.
    URL.createObjectURL = vi.fn((blob: Blob) => {
      created.push(blob)
      return 'blob:export'
    })
    URL.revokeObjectURL = vi.fn()
    const downloads: string[] = []
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download)
    })
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('IMG_0001.jpg'))
    await user.click(screen.getByRole('button', { name: 'Download' }))
    expect(downloads).toEqual(['Best of 2025.txt'])
    expect(await created[0]!.text()).toBe(
      '/dev/Holidays/Madeira/IMG_0002.jpg\n/dev/Holidays/Madeira/IMG_0001.jpg\n',
    )
  })

  it('copies the list to the clipboard', async () => {
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('IMG_0001.jpg'))
    await user.click(screen.getByRole('button', { name: 'Copy' }))
    expect(await screen.findByText('Copied to clipboard.')).toBeInTheDocument()
    expect(await navigator.clipboard.readText()).toContain('/dev/Holidays/Madeira/IMG_0001.jpg')
  })

  it('warns when some files are missing on disk', async () => {
    albumStore.get(5)!.missing.add(20)
    await openExport()
    expect(await screen.findByText('1 photo is missing on disk.')).toBeInTheDocument()
  })
})
