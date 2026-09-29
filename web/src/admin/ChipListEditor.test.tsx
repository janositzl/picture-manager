import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { ChipListEditor } from './ChipListEditor'

function Harness() {
  const [values, setValues] = useState<string[]>([])
  return <ChipListEditor label="Excluded folder names" values={values} onChange={setValues} />
}

describe('ChipListEditor', () => {
  it('shows the usage hint', () => {
    render(<Harness />)
    expect(
      screen.getByText('Separate multiple entries with commas or spaces. Quote entries ("Old Photos") to keep spaces.'),
    ).toBeInTheDocument()
  })

  it('adds a comma-separated batch in one go', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const field = screen.getByRole('textbox')
    await user.type(field, '@eaDir, Thumbs.db{enter}')
    expect(screen.getByText('@eaDir')).toBeInTheDocument()
    expect(screen.getByText('Thumbs.db')).toBeInTheDocument()
  })

  it('adds a whitespace-separated batch in one go', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const field = screen.getByRole('textbox')
    await user.type(field, '.tmp .bak{enter}')
    expect(screen.getByText('.tmp')).toBeInTheDocument()
    expect(screen.getByText('.bak')).toBeInTheDocument()
  })

  it('ignores blank and already-present tokens', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const field = screen.getByRole('textbox')
    await user.type(field, '  a  ,  ,  a  ,  b {enter}')
    expect(screen.getAllByText('a')).toHaveLength(1)
    expect(screen.getByText('b')).toBeInTheDocument()
  })

  it('keeps a quoted entry with spaces as a single token', async () => {
    const user = userEvent.setup()
    render(<Harness />)
    const field = screen.getByRole('textbox')
    await user.type(field, '"Old Photos", .bak{enter}')
    expect(screen.getByText('Old Photos')).toBeInTheDocument()
    expect(screen.getByText('.bak')).toBeInTheDocument()
  })
})
