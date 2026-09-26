import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  function handleLogout() {
    logout();
    navigate('/login');
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header-left">
          <span className="app-logo">🧾 FisAsistan</span>
          <nav className="app-nav">
            <NavLink to="/" end>
              Panel
            </NavLink>
            <NavLink to="/upload">Fiş Yükle</NavLink>
          </nav>
        </div>
        <div className="app-header-right">
          <span className="app-user">{user?.fullName}</span>
          <button className="btn btn-ghost" onClick={handleLogout}>
            Çıkış Yap
          </button>
        </div>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
