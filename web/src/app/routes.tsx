import { Navigate, type RouteObject } from 'react-router'
import { AlbumsPage } from '../albums/AlbumsPage'
import { FavoritesView } from '../views/FavoritesView'
import { FolderView } from '../views/FolderView'
import { RootRedirect } from '../views/RootRedirect'
import { SearchView } from '../views/SearchView'
import { AppShell } from './AppShell'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <RootRedirect /> },
      { path: 'folders/:folderId', element: <FolderView /> },
      { path: 'favorites', element: <FavoritesView /> },
      { path: 'albums', element: <AlbumsPage /> },
      { path: 'search', element: <SearchView /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]
