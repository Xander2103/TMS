import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { TimeInput } from '../TimeInput'
import { normalizeTimeText } from '../timeText'

function ControlledTimeInput(props: {
  initial?: string
  onChange?: (value: string) => void
  onInvalid?: (raw: string) => void
}) {
  const [value, setValue] = useState(props.initial ?? '')
  return (
    <>
      <TimeInput
        aria-label="Tijd"
        value={value}
        onChange={(next) => {
          setValue(next)
          props.onChange?.(next)
        }}
        onInvalid={props.onInvalid}
      />
      <output data-testid="value">{value}</output>
    </>
  )
}

const getInput = () => screen.getByRole('textbox', { name: 'Tijd' }) as HTMLInputElement

describe('normalizeTimeText', () => {
  it.each([
    ['8', '08:00'],
    ['08', '08:00'],
    ['830', '08:30'],
    ['0830', '08:30'],
    ['1730', '17:30'],
    ['8:5', '08:05'],
    ['8:3', '08:03'],
    ['8:30', '08:30'],
    ['8.30', '08:30'],
    ['8,30', '08:30'],
    ['8h30', '08:30'],
    ['8u30', '08:30'],
    ['8H30', '08:30'],
    ['8 30', '08:30'],
    ['12', '12:00'],
    ['0', '00:00'],
    ['123', '01:23'],
    ['  13:00  ', '13:00'],
    ['23:59', '23:59'],
  ])('normalizes %j to %j', (raw, expected) => {
    expect(normalizeTimeText(raw)).toBe(expected)
  })

  it.each([['', null], ['   ', null], ['24:00', null], ['24', null], ['25:00', null], ['08:60', null], ['abc', null], ['8:', null], ['12345', null]])(
    'rejects %j',
    (raw, expected) => {
      expect(normalizeTimeText(raw)).toBe(expected)
    },
  )
})

describe('TimeInput', () => {
  it('renders a 24h value as-is, never converted to AM/PM', () => {
    render(<TimeInput aria-label="Tijd" value="13:00" onChange={() => {}} />)
    const input = getInput()
    expect(input.value).toBe('13:00')
    expect(input).toHaveAttribute('type', 'text')
    expect(input).toHaveAttribute('inputmode', 'numeric')
    expect(input).toHaveAttribute('maxlength', '5')
    expect(input).toHaveClass('ui-time-input')
  })

  it('uses the localized placeholder by default', () => {
    render(<TimeInput aria-label="Tijd" value="" onChange={() => {}} />)
    expect(getInput()).toHaveAttribute('placeholder', 'uu:mm')
  })

  it('auto-inserts the colon and emits once the value is complete when typing digits only', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput onChange={onChange} />)
    const input = getInput()

    await user.type(input, '083')
    expect(input.value).toBe('08:3')
    expect(onChange).not.toHaveBeenCalled()

    await user.type(input, '0')
    expect(input.value).toBe('08:30')
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('08:30')
  })

  it('keeps noon as 12:00 and midnight as 00:00', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput onChange={onChange} />)
    const input = getInput()

    await user.type(input, '12:00')
    expect(onChange).toHaveBeenLastCalledWith('12:00')
    expect(input.value).toBe('12:00')

    await user.clear(input)
    expect(onChange).toHaveBeenLastCalledWith('')

    await user.type(input, '00:00')
    expect(onChange).toHaveBeenLastCalledWith('00:00')
    expect(input.value).toBe('00:00')
  })

  it('normalizes shorthand on blur and emits the result', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput onChange={onChange} />)
    const input = getInput()

    await user.type(input, '8')
    expect(onChange).not.toHaveBeenCalled()
    await user.tab()

    expect(input.value).toBe('08:00')
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('08:00')
    expect(input).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('flags unresolvable text on blur without emitting', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    const onInvalid = vi.fn()
    render(<ControlledTimeInput onChange={onChange} onInvalid={onInvalid} />)
    const input = getInput()

    await user.type(input, '24:00')
    await user.tab()

    expect(onChange).not.toHaveBeenCalled()
    expect(onInvalid).toHaveBeenCalledWith('24:00')
    expect(input.value).toBe('24:00')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByTestId('value')).toHaveTextContent('')
  })

  it('accepts a full value in a single change event', () => {
    const onChange = vi.fn()
    render(<ControlledTimeInput onChange={onChange} />)
    const input = getInput()

    fireEvent.change(input, { target: { value: '07:00' } })

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('07:00')
    expect(input.value).toBe('07:00')
  })

  it('emits an empty string when cleared', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput initial="09:15" onChange={onChange} />)
    const input = getInput()

    await user.clear(input)

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('')
    expect(input.value).toBe('')
  })

  it('follows an external value change', () => {
    const { rerender } = render(<TimeInput aria-label="Tijd" value="08:00" onChange={() => {}} />)
    expect(getInput().value).toBe('08:00')

    rerender(<TimeInput aria-label="Tijd" value="17:45" onChange={() => {}} />)
    expect(getInput().value).toBe('17:45')

    rerender(<TimeInput aria-label="Tijd" value="" onChange={() => {}} />)
    expect(getInput().value).toBe('')
  })

  it('steps by 15 minutes with the arrow keys and by an hour with Shift', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput initial="08:00" onChange={onChange} />)
    const input = getInput()

    await user.click(input)
    await user.keyboard('{ArrowUp}')
    expect(onChange).toHaveBeenLastCalledWith('08:15')
    expect(input.value).toBe('08:15')

    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(onChange).toHaveBeenLastCalledWith('07:45')

    await user.keyboard('{Shift>}{ArrowUp}{/Shift}')
    expect(onChange).toHaveBeenLastCalledWith('08:45')
  })

  it('wraps around midnight when stepping', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput initial="23:50" onChange={onChange} />)

    await user.click(getInput())
    await user.keyboard('{ArrowUp}')
    expect(onChange).toHaveBeenLastCalledWith('00:05')

    await user.keyboard('{ArrowDown}')
    expect(onChange).toHaveBeenLastCalledWith('23:50')
  })

  it('does not emit partial input while typing', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ControlledTimeInput onChange={onChange} />)

    await user.type(getInput(), '1')
    await user.type(getInput(), '3:')
    expect(onChange).not.toHaveBeenCalled()
  })
})
