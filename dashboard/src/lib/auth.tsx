import React, { createContext, useContext, useState } from 'react'

interface AuthContextType {
  isAuthenticated: boolean
  token: string | null
  login: (token: string) => void
  logout: () => void
  loginlessMode: boolean
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

// Check if loginless mode is enabled via environment variable
const isLoginlessMode = import.meta.env.VITE_LOGINLESS_MODE === 'true'

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [token, setToken] = useState<string | null>(() => {
    // In loginless mode, auto-authenticate
    if (isLoginlessMode) {
      return 'loginless-mode'
    }
    return localStorage.getItem('auth_token')
  })

  const isAuthenticated = isLoginlessMode || !!token

  const login = (newToken: string) => {
    localStorage.setItem('auth_token', newToken)
    setToken(newToken)
  }

  const logout = () => {
    // Don't allow logout in loginless mode
    if (isLoginlessMode) return
    
    localStorage.removeItem('auth_token')
    setToken(null)
  }

  return (
    <AuthContext.Provider value={{ isAuthenticated, token, login, logout, loginlessMode: isLoginlessMode }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return context
}
