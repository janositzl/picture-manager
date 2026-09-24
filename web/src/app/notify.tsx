import { Button, Snackbar } from '@mui/material'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

export type NotifyAction = { label: string; onClick: () => void }
type Notify = (message: string, action?: NotifyAction) => void
type Message = { text: string; action: NotifyAction | undefined }

const NotifyContext = createContext<Notify | null>(null)

/** One app-wide snackbar for short, transient messages, optionally with one action. */
export function NotifyProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<Message | null>(null)
  const notify = useCallback<Notify>((text, action) => setMessage({ text, action }), [])
  const action = message?.action

  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar
        open={message !== null}
        message={message?.text ?? ''}
        autoHideDuration={4000}
        onClose={() => setMessage(null)}
        action={
          action && (
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                setMessage(null)
                action.onClick()
              }}
            >
              {action.label}
            </Button>
          )
        }
      />
    </NotifyContext.Provider>
  )
}

export function useNotify(): Notify {
  const notify = useContext(NotifyContext)
  if (notify === null) throw new Error('useNotify must be used inside NotifyProvider')
  return notify
}
