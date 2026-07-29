import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PC Steward — Interactive design prototype",
  description:
    "A high-fidelity Windows 11 design prototype for a calm, local-first software inventory and update steward.",
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
  openGraph: {
    title: "PC Steward — Evidence before action",
    description:
      "Review the high-fidelity, local-first Windows 11 product-design prototype.",
    images: [
      {
        url: "/og.png",
        width: 1200,
        height: 630,
        alt: "PC Steward interactive design prototype",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "PC Steward — Evidence before action",
    description:
      "Review the high-fidelity, local-first Windows 11 product-design prototype.",
    images: ["/og.png"],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
