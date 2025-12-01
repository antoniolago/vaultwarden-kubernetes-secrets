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
        width: '100%',
        padding: 2,
        bgcolor: 'background.body',
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      <Card
        variant="outlined"
        color="primary"
        sx={{
          maxWidth: 440,
          width: '100%',
          mx: 2,
          p: 5,
          backdropFilter: 'blur(12px)',
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
              bgcolor: 'primary.solidBg',
              mb: 2,
            }}
          >
            {/* VKS.png logo */}
            <img src="/vks.png" alt="vks" width="80" height="80" style={{ borderRadius: '50%' }}/>
          </Box>
          
          <Typography 
            level="h2" 
            fontWeight="bold" 
            sx={{ 
              mb: 1,
              fontSize: '1.2rem',
            }}
          >
            VKS
          </Typography>
          <Typography 
            level="body-xs" 
            color="neutral"
            sx={{ 
              fontSize: '0.75rem',
              lineHeight: 1.6,
            }}
          >
            vaultwarden-kubernetes-secrets
          </Typography>
          
        </Box>

        <Divider sx={{ my: 3 }} />

        {error && (
          <Alert 
            variant="soft"
            color="danger" 
            sx={{ mb: 3 }}
          >
            {error}
          </Alert>
        )}

        <form onSubmit={handleSubmit}>
          <FormControl>
            <FormLabel sx={{ mb: 1 }}>
              Authentication Token
            </FormLabel>
            <Input
              type="password"
              placeholder="Enter your token"
              value={token}
              onChange={(e) => setToken(e.target.value)}
              required
              autoFocus
              color="primary"
              variant="soft"
            />
          </FormControl>

          <Button
            type="submit"
            fullWidth
            loading={loading}
            color="primary"
            size="lg"
            sx={{ mt: 3 }}
          >
            Sign In
          </Button>
        </form>

        <Divider sx={{ my: 3 }} />

        <Box sx={{ textAlign: 'center' }}>
          <Typography 
            level="body-xs"
            color="neutral"
            sx={{ 
              fontSize: '0.75rem',
              lineHeight: 1.6,
            }}
          >
            If not provided on install, app will generate and store token in Kubernetes secret "vaultwarden-kubernetes-secrets"
          </Typography>
        </Box>
      </Card>
    </Box>
  )
}
