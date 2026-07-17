import './Feedback.css'

interface ErrorStateProps {
  message: string
}

export function ErrorState({ message }: ErrorStateProps) {
  return (
    <p className="error-text" role="alert" aria-live="assertive">
      {message}
    </p>
  )
}
