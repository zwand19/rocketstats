"use client";

import { usePathname } from "next/navigation";

export function NavLink({ href, children }: { href: string; children: React.ReactNode }) {
  const pathname = usePathname();
  const isActive = href === "/" ? pathname === "/" : pathname?.startsWith(href);

  return (
    <a href={href} className={isActive ? "active" : undefined}>
      {children}
    </a>
  );
}
