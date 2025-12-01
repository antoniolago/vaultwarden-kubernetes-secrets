import { Component, ErrorInfo, ReactNode } from 'react'
import { Box, Typography, Button, Alert } from '@mui/joy'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Error caught by boundary:', error, errorInfo)
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null })
    window.location.href = '/'
  }

  render() {
    if (this.state.hasError) {
      return (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: '100vh',
            width: '100%',
            p: 3,
            bgcolor: 'background.body',
          }}
        >
          <Box sx={{ maxWidth: 500, width: '100%' }}>
            <Alert
              variant="soft"
              color="danger"
              sx={{ mb: 2 }}
            >
              <Box>
                <Typography level="title-lg" sx={{ mb: 1 }}>
                  Something went wrong
                </Typography>
                <Typography level="body-sm">
                  {this.state.error?.message || 'An unexpected error occurred'}
                </Typography>
              </Box>
            </Alert>

            <Button 
              fullWidth 
              onClick={this.handleReset}
              color="primary"
              size="lg"
            >
              Go to Dashboard
            </Button>
          </Box>
        </Box>
      )
    }

    return this.props.children
  }
}
