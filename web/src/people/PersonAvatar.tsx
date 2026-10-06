import HelpOutlineOutlinedIcon from '@mui/icons-material/HelpOutlineOutlined'
import PersonOutlinedIcon from '@mui/icons-material/PersonOutlined'
import { faceThumbnailUrl } from '../api/people'

type Props = { faceId: number | null; unknown: boolean; className?: string }

/** The person's cover face, or an icon (question mark for unnamed groups) when there is none. */
export function PersonAvatar({ faceId, unknown, className = 'h-10 w-10' }: Props) {
  const ring = 'shrink-0 rounded-full ring-2 ring-white dark:ring-zinc-900'
  return faceId !== null ? (
    <img src={faceThumbnailUrl(faceId)} alt="" className={`${className} ${ring} object-cover`} loading="lazy" />
  ) : (
    <div
      className={`${className} ${ring} flex items-center justify-center bg-zinc-100 text-zinc-400 dark:bg-zinc-800 dark:text-zinc-500`}
    >
      {unknown ? <HelpOutlineOutlinedIcon fontSize="small" /> : <PersonOutlinedIcon fontSize="small" />}
    </div>
  )
}
