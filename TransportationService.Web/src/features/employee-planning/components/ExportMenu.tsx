import { useRef, useState } from 'react'
import { ChevronDown, Download, FileSpreadsheet } from 'lucide-react'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import { useLocale } from '../../../i18n/localeContext'
import { downloadScheduleExcel } from '../api/employeePlanningApi'
import type { PlanningSelection } from '../planningFilters'
import { PlanningPopover } from './PlanningPopover'

/** "Exporteren ▾": the workbook is built server-side for exactly the current selection and period. */
export function ExportMenu({ selection }: { selection: PlanningSelection }) {
  const { t } = useLocale()
  const { showError } = useToast()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)

  async function exportExcel() {
    setOpen(false)
    setBusy(true)
    try {
      await downloadScheduleExcel(selection)
    } catch (error) {
      showError(error instanceof ApiError ? error.message : t('employeePlanning.toolbar.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="ep-toolbar-popover-host">
      <button
        ref={buttonRef}
        type="button"
        className="ep-toolbar-button"
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={busy}
        onClick={() => setOpen((value) => !value)}
      >
        <Download size={15} aria-hidden="true" />
        {busy ? t('employeePlanning.toolbar.exportBusy') : t('employeePlanning.toolbar.export')}
        <ChevronDown size={14} aria-hidden="true" />
      </button>
      <PlanningPopover open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label={t('employeePlanning.toolbar.export')} className="ep-popover-menu">
        <button type="button" role="menuitem" className="ep-menu-item" onClick={() => void exportExcel()}>
          <FileSpreadsheet size={15} aria-hidden="true" />
          <span>
            {t('employeePlanning.toolbar.exportExcel')}
            <span className="ep-menu-item-hint">{t('employeePlanning.toolbar.exportHint')}</span>
          </span>
        </button>
      </PlanningPopover>
    </div>
  )
}
