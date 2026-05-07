import type { Metadata } from "next";
import "./globals.css";
import { NavLink } from "./_components/nav-link";

export const metadata: Metadata = {
  title: "Rocket Stats",
  description: "Tracked stats from Rocket League's local Stats API.",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>
        <div className="app-shell">
          <aside className="sidebar">
            <div className="brand">
              <span className="brand-mark">RS</span>
              <span>Rocket Stats</span>
            </div>
            <nav>
              <NavLink href="/">Dashboard</NavLink>
              <NavLink href="/players">Players</NavLink>
              <NavLink href="/games">Games</NavLink>
              <NavLink href="/connection">Connection</NavLink>
              <NavLink href="/settings">Settings</NavLink>
            </nav>
          </aside>
          <section className="content">{children}</section>
        </div>
      </body>
    </html>
  );
}
