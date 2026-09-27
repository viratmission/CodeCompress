import React, { createContext, useContext, useState, useEffect, useCallback } from 'react'
import type { User, AuthResponse } from '../types/auth'

interface AuthContextType {
  user: User | null
  token: string | null
  isAuthenticated: boolean
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string, displayName: string) => Promise<void>
  logout: () => void
  refreshUser: () => Promise<void>
  authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

const TOKEN_KEY = 'codecompass_auth_token'

// Setup global fetch interceptor to automatically attach JWT token to all /api/ calls
const originalFetch = window.fetch
window.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : (input as Request).url
  const token = localStorage.getItem(TOKEN_KEY)

  if (token && url.includes('/api/')) {
    init = init || {}
    const headers = new Headers(init.headers || {})
    if (!headers.has('Authorization')) {
      headers.set('Authorization', `Bearer ${token}`)
    }
    init.headers = headers
  }

  const response = await originalFetch(input, init)

  // Auto-handle expired token / 401 Unauthorized on protected routes
  if (response.status === 401 && !url.includes('/api/auth/login') && !url.includes('/api/auth/register')) {
    window.dispatchEvent(new CustomEvent('codecompass_unauthorized'))
  }

  return response
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem(TOKEN_KEY))
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState<boolean>(true)

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY)
    setToken(null)
    setUser(null)
  }, [])

  useEffect(() => {
    const handleUnauthorized = () => {
      logout()
    }
    window.addEventListener('codecompass_unauthorized', handleUnauthorized)
    return () => {
      window.removeEventListener('codecompass_unauthorized', handleUnauthorized)
    }
  }, [logout])

  const refreshUser = useCallback(async () => {
    const currentToken = localStorage.getItem(TOKEN_KEY)
    if (!currentToken) {
      setUser(null)
      setLoading(false)
      return
    }

    try {
      const res = await originalFetch('/api/auth/me', {
        headers: {
          Authorization: `Bearer ${currentToken}`,
        },
      })

      if (res.ok) {
        const userData: User = await res.json()
        setUser(userData)
      } else {
        // Token invalid or expired
        logout()
      }
    } catch {
      // Network failure; retain token if offline or handle gracefully
    } finally {
      setLoading(false)
    }
  }, [logout])

  useEffect(() => {
    refreshUser()
  }, [refreshUser])

  const login = async (email: string, password: string) => {
    const res = await originalFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.trim(), password }),
    })

    const data = await res.json()
    if (!res.ok) {
      throw new Error(data.error || 'Failed to login.')
    }

    const authData = data as AuthResponse
    localStorage.setItem(TOKEN_KEY, authData.token)
    setToken(authData.token)
    setUser(authData.user)
  }

  const register = async (email: string, password: string, displayName: string) => {
    const res = await originalFetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: email.trim(),
        password,
        displayName: displayName.trim(),
      }),
    })

    const data = await res.json()
    if (!res.ok) {
      throw new Error(data.error || 'Failed to register.')
    }

    const authData = data as AuthResponse
    localStorage.setItem(TOKEN_KEY, authData.token)
    setToken(authData.token)
    setUser(authData.user)
  }

  const authFetch = useCallback(
    async (input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> => {
      return window.fetch(input, init)
    },
    []
  )

  const value: AuthContextType = {
    user,
    token,
    isAuthenticated: !!user && !!token,
    loading,
    login,
    register,
    logout,
    refreshUser,
    authFetch,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
