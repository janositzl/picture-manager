import { ConfirmDialog } from '../shared/ConfirmDialog'

type Props = { folderName: string; recursive: boolean; onConfirm: () => void; onClose: () => void }

/** Re-analysing runs the (slow) detector again on every photo, so it is confirmed first. */
export function ReanalyseFacesConfirm({ folderName, recursive, onConfirm, onClose }: Props) {
  return (
    <ConfirmDialog
      title="Re-analyse faces?"
      message={`Analyse every photo in ${folderName}${recursive ? ' and its subfolders' : ''} for faces again with the current settings? This can take a while. People you have already confirmed are kept.`}
      confirmLabel="Re-analyse"
      onConfirm={onConfirm}
      onClose={onClose}
    />
  )
}
