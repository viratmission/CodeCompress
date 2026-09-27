export interface User {
  id: number
  email: string
  displayName: string
  role: string
  createdAt: string
}

export interface AuthResponse {
  token: string
  user: User
}
