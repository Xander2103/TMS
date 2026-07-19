import { describe, expect, it } from 'vitest'
import {
  SCAN_RESULT_ICONS,
  SCAN_RESULT_LABELS,
  SCAN_STATE_ICONS,
  SCAN_STATE_LABELS,
  SCAN_STATE_TONE,
  type CargoScanState,
  type ScanResult,
} from '../types'

const ALL_RESULTS: ScanResult[] = [
  'Expected',
  'UnexpectedItem',
  'WrongItem',
  'DuplicateScan',
  'OverDelivery',
  'DamagedItem',
  'ManualCorrection',
]

const ALL_STATES: CargoScanState[] = ['Missing', 'Partial', 'Complete', 'Over']

describe('scanning metadata', () => {
  it('labels and icons exist for every scan result (feedback is never colour-only)', () => {
    for (const result of ALL_RESULTS) {
      expect(SCAN_RESULT_LABELS[result], `label ${result}`).toBeTruthy()
      expect(SCAN_RESULT_ICONS[result], `icon ${result}`).toBeTruthy()
    }
  })

  it('labels, tones and icons exist for every cargo scan state', () => {
    for (const state of ALL_STATES) {
      expect(SCAN_STATE_LABELS[state], `label ${state}`).toBeTruthy()
      expect(SCAN_STATE_TONE[state], `tone ${state}`).toBeTruthy()
      expect(SCAN_STATE_ICONS[state], `icon ${state}`).toBeTruthy()
    }
  })
})
