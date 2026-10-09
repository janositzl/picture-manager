import type { ReactNode } from 'react'
import { Navigate } from 'react-router'
import { usePermissions } from '../api/auth'

/** Sends anyone who is not an admin back to the app; the server refuses the admin API to them anyway. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  return usePermissions().isAdmin ? children : <Navigate to="/" replace />
}
