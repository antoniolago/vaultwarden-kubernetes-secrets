import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import {
  Box,
  Sheet,
  List,
  ListItem,
  ListItemButton,
  Typography,
  IconButton,
} from '@mui/joy'
import { useAuth } from '../lib/auth'

interface LayoutProps {
  children: React.ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const { logout } = useAuth()

  const navItems = [
    { path: '/', label: '📊 Dashboard' },
    { path: '/secrets', label: '🔑 Secrets' },
    { path: '/logs', label: '📜 Sync Logs' },
    { path: '/discovery', label: '🔍 Discovery' },
  ]

  const { loginlessMode } = useAuth()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      {/* Sidebar */}
      <Sheet
        sx={{
          width: 240,
          p: 2,
          flexShrink: 0,
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          borderRight: '1px solid',
          borderColor: 'divider',
        }}
      >
        <Box sx={{ mb: 3 }}>
          <Typography level="h4" fontWeight="bold" sx={{ color: 'primary.500' }}>
            🔐 Vaultwarden K8s
          </Typography>
          <Typography level="body-xs" sx={{ color: 'text.secondary' }}>
            Secret Sync Dashboard
          </Typography>
        </Box>

        <List sx={{ flexGrow: 1 }}>
          {navItems.map((item) => (
            <ListItem key={item.path}>
              <ListItemButton
                selected={location.pathname === item.path}
                onClick={() => navigate(item.path)}
              >
                {item.label}
              </ListItemButton>
            </ListItem>
          ))}
        </List>

        {!loginlessMode && (
          <ListItemButton onClick={handleLogout} color="danger">
            🚪 Logout
          </ListItemButton>
        )}
        
        {loginlessMode && (
          <Box sx={{ p: 2, textAlign: 'center' }}>
            <Typography level="body-xs" sx={{ color: 'success.500' }}>
              🔓 Loginless Mode
            </Typography>
          </Box>
        )}
      </Sheet>

      {/* Main content */}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        {/* Mobile header */}
        <Sheet
          sx={{
            display: { xs: 'flex', md: 'none' },
            alignItems: 'center',
            gap: 1,
            p: 2,
            borderBottom: '1px solid',
            borderColor: 'divider',
          }}
        >
          <IconButton
            variant="outlined"
            size="sm"
            onClick={() => setDrawerOpen(!drawerOpen)}
          >
            ☰
          </IconButton>
          <Typography level="title-lg" fontWeight="bold">
            Vaultwarden K8s
          </Typography>
        </Sheet>

        {/* Content */}
        <Box sx={{ flex: 1, p: { xs: 2, md: 3 }, overflow: 'auto' }}>
          {children}
        </Box>
      </Box>
    </Box>
  )
}
