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
  Chip,
} from '@mui/joy'
import { useAuth } from '../lib/auth'
import SyncProgressBar from './SyncProgressBar'

interface LayoutProps {
  children: React.ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const { logout, loginlessMode } = useAuth()

  const navItems = [
    { path: '/', label: 'Dashboard' },
    { path: '/secrets', label: 'Secrets' },
    { path: '/discovery', label: 'Discovery' },
    { path: '/logs', label: 'Sync Logs' },
  ]

  const handleLogout = () => {
    logout()
    navigate('/login')
  }


  return (
    <Box sx={{ display: 'flex', height: '100vh', width: '100%' }}>
      {/* Sidebar */}
      <Sheet
        variant="soft"
        sx={{
          width: 240,
          p: 2,
          flexShrink: 0,
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          borderRight: '1px solid',
          borderColor: 'divider',
          bgcolor: 'background.surface',
        }}
      >
        <Box sx={{ mb: 3, display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <img
            src="/vks.png"
            alt="VKS Logo"
            style={{
              width: 40,
              height: 40,
              borderRadius: '8px',
              objectFit: 'cover',
            }}
          />
          <Box>
            <Typography level="h4" fontWeight="bold" color="primary">
              VKS
            </Typography>
            <a href="https://github.com/antoniolago/vaultwarden-kubernetes-secrets" target="_blank">
              <Typography level="body-xs" sx={{fontSize: '10px'}} color="neutral">
                vaultwarden-kubernetes-secrets
              </Typography>
            </a>
          </Box>
        </Box>

        <List sx={{ flexGrow: 1, '--List-gap': '8px' }}>
          {navItems.map((item) => (
            <ListItem key={item.path}>
              <ListItemButton
                selected={location.pathname === item.path}
                onClick={() => navigate(item.path)}
                color={location.pathname === item.path ? 'primary' : 'neutral'}
              >
                {item.label}
              </ListItemButton>
            </ListItem>
          ))}
        </List>


        {/* Sync Progress Bar - Sticky at top */}
        {/* <Box sx={{
          position: 'sticky',
          top: 0,
          zIndex: 10,
          pb: 0,
          bgcolor: 'background.body',
        }}>
          <SyncProgressBar />
        </Box> */}
        
        {/* Logout button below sync bar */}
        {!loginlessMode && (
          <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1 }}>
            <IconButton
              onClick={handleLogout}
              variant="soft"
              color="neutral"
              size="sm"
              sx={{
                fontSize: '11px',
                px: 1.5,
                py: 0.5,
                minHeight: '24px',
                color: 'text.secondary',
                '&:hover': {
                  bgcolor: 'neutral.softHoverBg',
                },
              }}
            >
              Logout
            </IconButton>
          </Box>
        )}
        
        {loginlessMode && (
          <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1 }}>
            <Chip
              size="sm"
              variant="soft"
              color="danger"
            >
              Loginless Mode!
            </Chip>
          </Box>
        )}
      </Sheet>

      {/* Main content */}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        {/* Desktop header */}
        <Sheet
          variant="soft"
          sx={{
            display: { xs: 'none', md: 'flex' },
            justifyContent: 'flex-end',
            alignItems: 'center',
            p: 2,
            borderBottom: '1px solid',
            borderColor: 'divider',
            bgcolor: 'background.surface',
            minHeight: '56px',
          }}
        >
          {/* Empty header for consistent spacing */}
        </Sheet>

        {/* Mobile header */}
        <Sheet
          variant="soft"
          sx={{
            display: { xs: 'flex', md: 'none' },
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 1,
            p: 2,
            borderBottom: '1px solid',
            borderColor: 'divider',
            bgcolor: 'background.surface',
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <IconButton
              variant="outlined"
              color="neutral"
              size="sm"
              onClick={() => setDrawerOpen(!drawerOpen)}
            >
              ☰
            </IconButton>
            <img
              src="/vks.png"
              alt="VKS Logo"
              style={{
                width: 32,
                height: 32,
                borderRadius: '6px',
                objectFit: 'cover'
              }}
            />
            <Typography level="title-lg" fontWeight="bold" color="primary">
              VKS
            </Typography>
          </Box>
        </Sheet>

        {/* Content */}
        <Box sx={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          overflow: 'auto',
          bgcolor: 'background.body',
        }}>

          {/* Main Content */}
          <Box sx={{
            flex: 1,
            p: { xs: 2, md: 3 },
            pt: { xs: 1, md: 2 },
          }}>
            {children}
          </Box>
        </Box>
      </Box>
      
    </Box>
  )
}
