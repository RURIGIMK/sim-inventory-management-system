import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SIM Inventory",
  description: "SIM inventory, allocation readiness and audit visibility"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
