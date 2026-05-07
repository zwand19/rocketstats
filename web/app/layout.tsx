import type { Metadata } from "next";
import "./globals.css";
import { AppShell } from "./_components/app-shell";

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
        <AppShell>{children}</AppShell>
      </body>
    </html>
  );
}
