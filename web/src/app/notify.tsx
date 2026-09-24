import { Snackbar } from '@mui/material'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

type Notify = (message: string) => void

const NotifyContext = createContext<Notify | null>(null)

/** One app-wide snackbar for short, transient messages. */
export function NotifyProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null)
  const notify = useCallback<Notify>((next) => setMessage(next), [])

  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar
        open={message !== null}
        message={message ?? ''}
        autoHideDuration={4000}
        onClose={() => setMessage(null)}
      />
    </NotifyContext.Provider>
  )
}

export function useNotify(): Notify {
  const notify = useContext(NotifyContext)
  if (notify === null) throw new Error('useNotify must be used inside NotifyProvider')
  return notify
}
