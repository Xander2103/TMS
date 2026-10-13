import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import './Button.css'

type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  children: ReactNode
  /** Forwarded to the underlying button (focus targets for section navigation). */
  ref?: Ref<HTMLButtonElement>
}

/** Themed button. Defaults to a submit-safe `type="button"` to avoid accidental form posts. */
export function Button({ variant = 'primary', type = 'button', className, children, ref, ...rest }: ButtonProps) {
  const classes = ['ui-button', `ui-button-${variant}`, className].filter(Boolean).join(' ')
  return (
    <button ref={ref} type={type} className={classes} {...rest}>
      {children}
    </button>
  )
}
