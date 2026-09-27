import React, { useState, useMemo } from 'react'
import { useAuth } from './context/AuthContext'
import { CodeCompassLogo } from './CodeCompassLogo'

interface RegisterPageProps {
  onSwitchToLogin: () => void
}

export const RegisterPage: React.FC<RegisterPageProps> = ({ onSwitchToLogin }) => {
  const { register } = useAuth()
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [errorMessage, setErrorMessage] = useState('')

  // Password strength calculation
  const strength = useMemo(() => {
    if (!password) return { score: 0, label: '', color: '' }
    let score = 0
    if (password.length >= 8) score += 1
    if (/[A-Z]/.test(password)) score += 1
    if (/[a-z]/.test(password)) score += 1
    if (/[0-9]/.test(password)) score += 1
    if (/[^A-Za-z0-9]/.test(password)) score += 1

    if (score <= 2) return { score, label: 'Weak', color: '#f85149' }
    if (score <= 4) return { score, label: 'Good', color: '#d29922' }
    return { score, label: 'Strong', color: '#3fb950' }
  }, [password])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!displayName.trim()) {
      setErrorMessage('Please enter your display name.')
      return
    }

    if (!email.trim()) {
      setErrorMessage('Please enter your email address.')
      return
    }

    if (password.length < 8) {
      setErrorMessage('Password must be at least 8 characters long.')
      return
    }

    if (!/[A-Za-z]/.test(password) || !/[0-9]/.test(password)) {
      setErrorMessage('Password must contain at least one letter and one number.')
      return
    }

    if (password !== confirmPassword) {
      setErrorMessage('Passwords do not match.')
      return
    }

    setErrorMessage('')
    setIsLoading(true)

    try {
      await register(email, password, displayName)
    } catch (err: any) {
      setErrorMessage(err.message || 'Registration failed. Please try again.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="auth-container">
      <div className="auth-card">
        {/* Header with Logo */}
        <div className="auth-header">
          <div className="auth-logo-badge">
            <CodeCompassLogo size={48} />
          </div>
          <h1 className="auth-title">Create your Account</h1>
          <p className="auth-subtitle">
            Join CodeCompass to navigate codebases, inspect architectures, and onboard rapidly.
          </p>
        </div>

        {/* Error Alert */}
        {errorMessage && (
          <div className="auth-error-alert" role="alert">
            <span className="auth-error-icon">⚠️</span>
            <span>{errorMessage}</span>
          </div>
        )}

        {/* Register Form */}
        <form onSubmit={handleSubmit} className="auth-form" noValidate>
          <div className="auth-field">
            <label className="auth-label" htmlFor="reg-name">
              Full Name / Display Name
            </label>
            <input
              id="reg-name"
              type="text"
              className="auth-input"
              placeholder="Alex Chen"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              disabled={isLoading}
              autoFocus
              required
            />
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="reg-email">
              Work or Personal Email
            </label>
            <input
              id="reg-email"
              type="email"
              className="auth-input"
              placeholder="alex@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={isLoading}
              autoComplete="email"
              required
            />
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="reg-password">
              Password
            </label>
            <div className="auth-input-wrapper">
              <input
                id="reg-password"
                type={showPassword ? 'text' : 'password'}
                className="auth-input auth-input--has-toggle"
                placeholder="At least 8 characters with letters & numbers"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoading}
                autoComplete="new-password"
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

            {/* Strength meter */}
            {password && (
              <div className="auth-strength-meter">
                <div className="auth-strength-bars">
                  {[1, 2, 3, 4, 5].map((lvl) => (
                    <div
                      key={lvl}
                      className="auth-strength-bar"
                      style={{
                        backgroundColor: lvl <= strength.score ? strength.color : 'rgba(255,255,255,0.1)',
                      }}
                    />
                  ))}
                </div>
                <div className="auth-strength-label" style={{ color: strength.color }}>
                  {strength.label}
                </div>
              </div>
            )}
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="reg-confirm-password">
              Confirm Password
            </label>
            <input
              id="reg-confirm-password"
              type={showPassword ? 'text' : 'password'}
              className="auth-input"
              placeholder="Re-enter your password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              disabled={isLoading}
              autoComplete="new-password"
              required
            />
          </div>

          <button
            type="submit"
            className="auth-submit-btn"
            disabled={isLoading}
          >
            {isLoading ? (
              <span className="auth-btn-spinner-content">
                <span className="auth-spinner" />
                Creating account...
              </span>
            ) : (
              'Create Account & Start Exploring →'
            )}
          </button>
        </form>

        {/* Footer Link to Login */}
        <div className="auth-footer">
          <span>Already have an account? </span>
          <button
            type="button"
            className="auth-switch-link"
            onClick={onSwitchToLogin}
          >
            Sign in
          </button>
        </div>
      </div>
    </div>
  )
}
