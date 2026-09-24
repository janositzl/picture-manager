import { Navigate, type RouteObject } from 'react-router'
import { RootRedirect } from '../views/RootRedirect'
import { AppShell } from './AppShell'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <RootRedirect /> },
      { path: 'folders/:folderId' },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]
