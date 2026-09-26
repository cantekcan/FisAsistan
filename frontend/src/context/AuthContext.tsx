import { createContext, useContext, useState, type ReactNode } from 'react';
import * as api from '../api/receiptsApi';

interface AuthUser {
  email: string;
  fullName: string;
}

interface AuthContextValue {
  user: AuthUser | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, fullName: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function loadStoredUser(): AuthUser | null {
  const raw = localStorage.getItem('fisasistan_user');
  return raw ? (JSON.parse(raw) as AuthUser) : null;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(loadStoredUser());

  function persist(token: string, email: string, fullName: string) {
    localStorage.setItem('fisasistan_token', token);
    const authUser = { email, fullName };
    localStorage.setItem('fisasistan_user', JSON.stringify(authUser));
    setUser(authUser);
  }

  async function login(email: string, password: string) {
    const res = await api.login(email, password);
    persist(res.token, res.email, res.fullName);
  }

  async function register(email: string, password: string, fullName: string) {
    const res = await api.register(email, password, fullName);
    persist(res.token, res.email, res.fullName);
  }

  function logout() {
    localStorage.removeItem('fisasistan_token');
    localStorage.removeItem('fisasistan_user');
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth, AuthProvider içinde kullanılmalıdır.');
  }
  return ctx;
}
