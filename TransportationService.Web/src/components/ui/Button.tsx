import type { ButtonHTMLAttributes, ReactNode } from 'react'
import './Button.css'

type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  children: ReactNode
}

/** Themed button. Defaults to a submit-safe `type="button"` to avoid accidental form posts. */
export function Button({ variant = 'primary', type = 'button', className, children, ...rest }: ButtonProps) {
  const classes = ['ui-button', `ui-button-${variant}`, className].filter(Boolean).join(' ')
  return (
    <button type={type} className={classes} {...rest}>
      {children}
    </button>
  )
}
