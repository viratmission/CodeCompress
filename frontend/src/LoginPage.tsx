import React, { useState } from 'react'
import { useAuth } from './context/AuthContext'
import { CodeCompassLogo } from './CodeCompassLogo'

interface LoginPageProps {
  onSwitchToRegister: () => void
}

export const LoginPage: React.FC<LoginPageProps> = ({ onSwitchToRegister }) => {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [errorMessage, setErrorMessage] = useState('')

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!email.trim() || !password) {
      setErrorMessage('Please provide both email and password.')
      return
    }

    setErrorMessage('')
    setIsLoading(true)

    try {
      await login(email, password)
    } catch (err: any) {
      setErrorMessage(err.message || 'Login failed. Please check your credentials.')
    } finally {
      setIsLoading(false)
    }
  }

  const fillDemoCredentials = () => {
    setEmail('demo@codecompass.dev')
    setPassword('DemoPassword123!')
    setErrorMessage('')
  }

  return (
    <div className="auth-container">
      <div className="auth-card">
        {/* Header with Logo */}
        <div className="auth-header">
          <div className="auth-logo-badge">
            <CodeCompassLogo size={48} />
          </div>
          <h1 className="auth-title">Welcome to CodeCompass</h1>
          <p className="auth-subtitle">
            Sign in to access repository intelligence, architecture analysis, and your AI assistant.
          </p>
        </div>

        {/* Error Alert */}
        {errorMessage && (
          <div className="auth-error-alert" role="alert">
            <span className="auth-error-icon">⚠️</span>
            <span>{errorMessage}</span>
          </div>
        )}

        {/* Login Form */}
        <form onSubmit={handleSubmit} className="auth-form" noValidate>
          <div className="auth-field">
            <label className="auth-label" htmlFor="login-email">
              Email Address
            </label>
            <input
              id="login-email"
              type="email"
              className="auth-input"
              placeholder="name@company.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={isLoading}
              autoComplete="email"
              autoFocus
              required
            />
          </div>

          <div className="auth-field">
            <div className="auth-label-row">
              <label className="auth-label" htmlFor="login-password">
                Password
              </label>
            </div>
            <div className="auth-input-wrapper">
              <input
                id="login-password"
                type={showPassword ? 'text' : 'password'}
                className="auth-input auth-input--has-toggle"
                placeholder="••••••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoading}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="auth-pw-toggle"
                onClick={() => setShowPassword(!showPassword)}
                title={showPassword ? 'Hide password' : 'Show password'}
                tabIndex={-1}
              >
                {showPassword ? '👁️' : '👁️‍🗨️'}
              </button>
            </div>
          </div>

          <button
            type="submit"
            className="auth-submit-btn"
            disabled={isLoading}
          >
            {isLoading ? (
              <span className="auth-btn-spinner-content">
                <span className="auth-spinner" />
                Signing in...
              </span>
            ) : (
              'Sign In to CodeCompass →'
            )}
          </button>
        </form>

        {/* Demo Fast Fill */}
        <div className="auth-demo-box">
          <div className="auth-demo-text">Hackathon Evaluator Quick Access</div>
          <button
            type="button"
            className="auth-demo-btn"
            onClick={fillDemoCredentials}
            disabled={isLoading}
          >
            ⚡ Auto-fill Demo Credentials
          </button>
        </div>

        {/* Footer Link to Register */}
        <div className="auth-footer">
          <span>Don't have an account yet? </span>
          <button
            type="button"
            className="auth-switch-link"
            onClick={onSwitchToRegister}
          >
            Create an account
          </button>
        </div>
      </div>
    </div>
  )
}
