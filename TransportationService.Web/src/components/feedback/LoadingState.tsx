import './Feedback.css'

interface LoadingStateProps {
  message: string
}

export function LoadingState({ message }: LoadingStateProps) {
  return (
    <p role="status" aria-live="polite">
      {message}
    </p>
  )
}
