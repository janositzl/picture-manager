import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { NotifyProvider, useNotify } from './notify'

function Trigger({ onAction }: { onAction: () => void }) {
  const notify = useNotify()
  return <button onClick={() => notify('Saved.', { label: 'Open', onClick: onAction })}>Go</button>
}

describe('notify', () => {
  it('shows an action button that runs and dismisses the message', async () => {
    const onAction = vi.fn()
    render(
      <NotifyProvider>
        <Trigger onAction={onAction} />
      </NotifyProvider>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Go' }))
    expect(await screen.findByText('Saved.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Open' }))
    expect(onAction).toHaveBeenCalledOnce()
    await waitFor(() => expect(screen.queryByText('Saved.')).not.toBeInTheDocument())
  })
})
