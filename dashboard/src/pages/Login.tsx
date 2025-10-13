import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Box,
  Card,
  Typography,
  Input,
  Button,
  FormControl,
  FormLabel,
  Alert,
  Divider,
} from '@mui/joy'
import { useAuth } from '../lib/auth'
import { api } from '../lib/api'

export default function Login() {
  const [token, setToken] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const { login } = useAuth()
  const navigate = useNavigate()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      const isValid = await api.testConnection(token)
      if (isValid) {
        login(token)
        navigate('/')
      } else {
        setError('Invalid token')
      }
    } catch (err) {
      setError('Failed to connect to API')
    } finally {
      setLoading(false)
    }
  }

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '100vh',
        background: 'linear-gradient(135deg, #0f172a 0%, #1e293b 50%, #334155 100%)',
        position: 'relative',
        '&::before': {
          content: '""',
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          bottom: 0,
          background: 'radial-gradient(circle at 20% 50%, rgba(59, 130, 246, 0.1) 0%, transparent 50%), radial-gradient(circle at 80% 80%, rgba(139, 92, 246, 0.1) 0%, transparent 50%)',
          pointerEvents: 'none',
        },
      }}
    >
      <Card
        variant="outlined"
        sx={{
          maxWidth: 440,
          width: '100%',
          mx: 2,
          p: 5,
          boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.5)',
          bgcolor: 'rgba(15, 23, 42, 0.8)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(148, 163, 184, 0.1)',
          position: 'relative',
          zIndex: 1,
        }}
      >
        {/* Header */}
        <Box sx={{ textAlign: 'center', mb: 4 }}>
          <Box
            sx={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: 80,
              height: 80,
              borderRadius: '50%',
              background: 'linear-gradient(135deg, #3b82f6 0%, #8b5cf6 100%)',
              mb: 2,
              boxShadow: '0 10px 40px rgba(59, 130, 246, 0.3)',
            }}
          >
            <Typography sx={{ fontSize: '2.5rem' }}>🔐</Typography>
          </Box>
          
          <Typography 
            level="h2" 
            fontWeight="bold" 
            sx={{ 
              color: '#f8fafc',
              mb: 1,
              fontSize: '1.875rem',
            }}
          >
            Vaultwarden K8s Sync
          </Typography>
          
          <Typography 
            level="body-sm" 
            sx={{ 
              color: '#94a3b8',
              fontSize: '0.875rem',
            }}
          >
            Secure Secret Synchronization Dashboard
          </Typography>
        </Box>

        <Divider sx={{ my: 3, borderColor: 'rgba(148, 163, 184, 0.1)' }} />

        {error && (
          <Alert 
            color="danger" 
            sx={{ 
              mb: 3,
              bgcolor: 'rgba(239, 68, 68, 0.1)',
              border: '1px solid rgba(239, 68, 68, 0.2)',
            }}
          >
            {error}
          </Alert>
        )}

        <form onSubmit={handleSubmit}>
          <FormControl>
            <FormLabel sx={{ color: '#e2e8f0', mb: 1, fontWeight: 500 }}>
              Authentication Token
            </FormLabel>
            <Input
              type="password"
              placeholder="Enter your token"
              value={token}
              onChange={(e) => setToken(e.target.value)}
              required
              autoFocus
              sx={{
                bgcolor: 'rgba(30, 41, 59, 0.5)',
                border: '1px solid rgba(148, 163, 184, 0.2)',
                color: '#f8fafc',
                '&:hover': {
                  bgcolor: 'rgba(30, 41, 59, 0.7)',
                  borderColor: 'rgba(148, 163, 184, 0.3)',
                },
                '&:focus-within': {
                  bgcolor: 'rgba(30, 41, 59, 0.7)',
                  borderColor: '#3b82f6',
                  boxShadow: '0 0 0 3px rgba(59, 130, 246, 0.1)',
                },
                '& input::placeholder': {
                  color: '#64748b',
                },
              }}
            />
          </FormControl>

          <Button
            type="submit"
            fullWidth
            loading={loading}
            size="lg"
            sx={{
              mt: 3,
              background: 'linear-gradient(135deg, #3b82f6 0%, #8b5cf6 100%)',
              color: '#fff',
              fontWeight: 600,
              py: 1.5,
              '&:hover': {
                background: 'linear-gradient(135deg, #2563eb 0%, #7c3aed 100%)',
                boxShadow: '0 10px 40px rgba(59, 130, 246, 0.3)',
              },
              '&:active': {
                transform: 'scale(0.98)',
              },
            }}
          >
            🔓 Sign In
          </Button>
        </form>

        <Divider sx={{ my: 3, borderColor: 'rgba(148, 163, 184, 0.1)' }} />

        <Box sx={{ textAlign: 'center' }}>
          <Typography 
            level="body-xs" 
            sx={{ 
              color: '#64748b',
              fontSize: '0.75rem',
              lineHeight: 1.6,
            }}
          >
            🔑 Token is securely stored in Kubernetes secrets
            <br />
            Configured during Helm chart installation
          </Typography>
        </Box>

        {/* Footer hint */}
        <Box 
          sx={{ 
            mt: 3, 
            pt: 3, 
            borderTop: '1px solid rgba(148, 163, 184, 0.1)',
            textAlign: 'center',
          }}
        >
          <Typography 
            level="body-xs" 
            sx={{ 
              color: '#475569',
              fontSize: '0.7rem',
            }}
          >
            💡 Tip: Set <code style={{ 
              background: 'rgba(59, 130, 246, 0.1)', 
              padding: '2px 6px', 
              borderRadius: '4px',
              color: '#60a5fa',
            }}>VITE_LOGINLESS_MODE=true</code> to skip authentication
          </Typography>
        </Box>
      </Card>
    </Box>
  )
}

// export default Login
