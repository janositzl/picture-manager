import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { TileSizeToggle } from './TileSizeToggle'

describe('TileSizeToggle', () => {
  it('defaults to Large selected', () => {
    render(<TileSizeToggle />)
    expect(screen.getByRole('button', { name: 'Large thumbnails' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('selecting Small persists and reflects in another instance', async () => {
    const user = userEvent.setup()
    render(<TileSizeToggle />)
    await user.click(screen.getByRole('button', { name: 'Small thumbnails' }))
    expect(screen.getByRole('button', { name: 'Small thumbnails' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )

    render(<TileSizeToggle />)
    expect(screen.getAllByRole('button', { name: 'Small thumbnails' })[1]).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })
})
