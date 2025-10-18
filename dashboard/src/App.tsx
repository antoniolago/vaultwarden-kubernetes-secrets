import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Box, useColorScheme } from '@mui/joy'
import Layout from './components/Layout'
import Dashboard from './pages/Dashboard'
import Secrets from './pages/Secrets'
import SyncLogs from './pages/SyncLogs'
import Resources from './pages/Resources'
import Discovery from './pages/Discovery'
import Login from './pages/Login'
import { AuthProvider, useAuth } from './lib/auth'
import { ErrorBoundary } from './components/ErrorBoundary'
import { useEffect } from 'react'

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />
}

function App() {
  const { setMode } = useColorScheme();
  useEffect(() => {
    setMode('dark');
  }, [setMode]);
  return (
    <ErrorBoundary>
      <BrowserRouter>
        <AuthProvider>
          <Box sx={{ display: 'flex', minHeight: '100vh', minWidth: '99dvw' }}>
            <Routes>
              <Route path="/login" element={<Login />} />
              <Route
                path="/*"
                element={
                  <ProtectedRoute>
                    <Layout>
                      <Routes>
                        <Route path="/" element={<Dashboard />} />
                        <Route path="/secrets" element={<Secrets />} />
                        <Route path="/logs" element={<SyncLogs />} />
                        <Route path="/discovery" element={<Discovery />} />
                        <Route path="/resources" element={<Resources />} />
                      </Routes>
                    </Layout>
                  </ProtectedRoute>
                }
              />
            </Routes>
          </Box>
        </AuthProvider>
      </BrowserRouter>
    </ErrorBoundary>
  )
}

export default App
