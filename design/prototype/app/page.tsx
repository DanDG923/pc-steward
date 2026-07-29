"use client";

import Image from "next/image";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";

type Route =
  | "overview"
  | "scan"
  | "results"
  | "review"
  | "applications"
  | "updates"
  | "startup"
  | "storage"
  | "activity"
  | "privacy"
  | "settings"
  | "support";

type Scenario =
  | "first-launch"
  | "ready"
  | "scanning"
  | "complete"
  | "no-findings"
  | "partial"
  | "offline";

type IconName =
  | "overview"
  | "scan"
  | "results"
  | "applications"
  | "updates"
  | "startup"
  | "storage"
  | "activity"
  | "privacy"
  | "settings"
  | "support"
  | "sun"
  | "moon"
  | "arrow"
  | "close"
  | "check"
  | "attention"
  | "info"
  | "search"
  | "filter"
  | "pause"
  | "play"
  | "stop"
  | "download"
  | "shield"
  | "clock"
  | "external";

type Tone = "brand" | "success" | "warning" | "danger" | "neutral" | "future";

type Finding = {
  id: string;
  title: string;
  summary: string;
  severity: string;
  confidence: string;
  tone: Tone;
  source: string;
  actionable: boolean;
  kind: string;
  effect: string;
  preserves: string;
};

const navGroups: Array<{
  label: string;
  items: Array<{ route: Route; label: string; icon: IconName; badge?: string; future?: boolean }>;
}> = [
  {
    label: "Home",
    items: [
      { route: "overview", label: "Overview", icon: "overview" },
      { route: "scan", label: "Scan", icon: "scan" },
    ],
  },
  {
    label: "Review",
    items: [
      { route: "applications", label: "Applications", icon: "applications" },
      { route: "updates", label: "Updates", icon: "updates", badge: "3" },
      { route: "startup", label: "Startup", icon: "startup", future: true },
      { route: "storage", label: "Storage", icon: "storage" },
    ],
  },
  {
    label: "Records",
    items: [
      { route: "activity", label: "Activity", icon: "activity", badge: "1" },
      { route: "privacy", label: "Privacy & data", icon: "privacy" },
    ],
  },
];

const findings: Finding[] = [
  {
    id: "update-editor",
    title: "Contoso Editor 4.2 is ready to review",
    summary: "An exact WinGet package identity matches the installed user-scope application.",
    severity: "Opportunity",
    confidence: "Verified match",
    tone: "brand",
    source: "WinGet · live 4 min ago",
    actionable: true,
    kind: "Approved update",
    effect: "Update Contoso Editor from 4.1.3 to 4.2.0 after a separate confirmation.",
    preserves: "Documents, settings and every unrelated application.",
  },
  {
    id: "update-archive",
    title: "Archive Studio needs your decision",
    summary: "Two trusted sources disagree about the newest stable version.",
    severity: "Attention",
    confidence: "Review required",
    tone: "warning",
    source: "2 source candidates · live",
    actionable: false,
    kind: "Source disagreement",
    effect: "No automatic action is available until a preferred source is explicitly reviewed.",
    preserves: "The installed version remains unchanged.",
  },
  {
    id: "unmanaged-notes",
    title: "Northwind Notes has no approved update source",
    summary: "The application is inventoried locally, but no trusted package identity was found.",
    severity: "Information",
    confidence: "Unmatched · no action",
    tone: "neutral",
    source: "Local inventory · 9 min ago",
    actionable: false,
    kind: "Unmanaged application",
    effect: "PC Steward can keep the application visible or store a reviewed manual mapping.",
    preserves: "Nothing is downloaded, installed or searched on the open web.",
  },
  {
    id: "download-cleanup",
    title: "640 MB of PC Steward downloads can be removed",
    summary: "Only inactive update files owned by PC Steward are included.",
    severity: "Opportunity",
    confidence: "Exact app-owned scope",
    tone: "success",
    source: "PC Steward download ledger · now",
    actionable: true,
    kind: "App-owned cleanup",
    effect: "Delete inactive payloads from PC Steward’s fixed download-cache root.",
    preserves: "Installed apps, active files, scan data, history and all Windows, Store and WinGet caches.",
  },
  {
    id: "startup-concept",
    title: "Cloud Sync starts with Windows",
    summary: "A future read-only concept showing how startup evidence could be reviewed.",
    severity: "Information",
    confidence: "Future concept",
    tone: "future",
    source: "Design prototype only",
    actionable: false,
    kind: "Future startup review",
    effect: "No production action is defined.",
    preserves: "The startup entry remains unchanged.",
  },
];

const applications = [
  {
    name: "Contoso Editor",
    version: "4.1.3",
    status: "Verified update",
    tone: "brand" as Tone,
    publisher: "Contoso Software",
    scope: "User",
    source: "WinGet · live",
  },
  {
    name: "Fabrikam Player",
    version: "12.8.1",
    status: "Current",
    tone: "success" as Tone,
    publisher: "Fabrikam Media",
    scope: "Machine",
    source: "WinGet · live",
  },
  {
    name: "Archive Studio",
    version: "7.4",
    status: "Review required",
    tone: "warning" as Tone,
    publisher: "Adventure Works",
    scope: "Machine",
    source: "2 sources · live",
  },
  {
    name: "Northwind Notes",
    version: "2.3.7",
    status: "Unmanaged",
    tone: "neutral" as Tone,
    publisher: "Northwind Labs",
    scope: "User",
    source: "Registry · local",
  },
  {
    name: "Visual C++ 2015–2022 Runtime",
    version: "14.40.33810",
    status: "Current",
    tone: "success" as Tone,
    publisher: "Microsoft Corporation",
    scope: "Machine",
    source: "Registry · local",
  },
  {
    name: "Wingtip Screenshot Tool",
    version: "5.0.2",
    status: "Pinned",
    tone: "neutral" as Tone,
    publisher: "Wingtip",
    scope: "User",
    source: "WinGet · cached",
  },
  {
    name: "Tailspin PDF",
    version: "3.8.0",
    status: "Current",
    tone: "success" as Tone,
    publisher: "Tailspin Toys",
    scope: "Machine",
    source: "MSIX · local",
  },
];

const approvedUpdates = [
  {
    name: "Contoso Editor",
    currentVersion: "4.1.3",
    targetVersion: "4.2.0",
    publisher: "Contoso Software",
    package: "Contoso.Editor",
    scope: "User",
    architecture: "x64",
    source: "WinGet",
  },
  {
    name: "Woodgrove Terminal",
    currentVersion: "1.18.2",
    targetVersion: "1.19.0",
    publisher: "Woodgrove",
    package: "Woodgrove.Terminal",
    scope: "User",
    architecture: "x64",
    source: "WinGet",
  },
  {
    name: "Tailspin PDF",
    currentVersion: "3.8.0",
    targetVersion: "3.8.4",
    publisher: "Tailspin Toys",
    package: "9P9TailspinPDF",
    scope: "User",
    architecture: "Neutral",
    source: "WinGet",
  },
];

const activities = [
  {
    title: "Software inventory and update check",
    detail: "68 applications · 3 ready to review · 1 provider warning",
    time: "Today, 10:42",
    status: "Completed with note",
    tone: "warning" as Tone,
    icon: "scan" as IconName,
  },
  {
    title: "Fabrikam Player updated",
    detail: "12.7.6 → 12.8.1 · exact post-update verification",
    time: "Yesterday, 18:16",
    status: "Verified success",
    tone: "success" as Tone,
    icon: "updates" as IconName,
  },
  {
    title: "Current version pinned",
    detail: "Wingtip Screenshot Tool · reason: compatibility",
    time: "25 Jul, 14:03",
    status: "Decision",
    tone: "neutral" as Tone,
    icon: "privacy" as IconName,
  },
  {
    title: "Downloaded files cleared",
    detail: "412 MB recovered · Windows and provider caches preserved",
    time: "21 Jul, 09:28",
    status: "Completed",
    tone: "success" as Tone,
    icon: "storage" as IconName,
  },
];

const dataCategories = [
  {
    id: "scan-data",
    title: "Scan data",
    amount: "342 MB · 68 applications",
    detail: "Inventory, raw provider records, candidates and scan runs.",
    action: "Clear scan data",
    icon: "scan" as IconName,
  },
  {
    id: "downloads",
    title: "Downloaded update files",
    amount: "640 MB recoverable",
    detail: "Inactive payloads and temporary extraction files owned by PC Steward.",
    action: "Clear downloaded files",
    icon: "download" as IconName,
  },
  {
    id: "source",
    title: "Source metadata",
    amount: "198 MB · checked 4 min ago",
    detail: "Replaceable cached package information from enabled trusted sources.",
    action: "Manage retention",
    icon: "updates" as IconName,
  },
  {
    id: "history",
    title: "Logs & update history",
    amount: "160 MB · 24 events",
    detail: "Bounded, sanitized records of scans, decisions and update outcomes.",
    action: "View activity",
    icon: "activity" as IconName,
  },
];

function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  const common = {
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.8,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
  };

  const paths: Record<IconName, ReactNode> = {
    overview: (
      <>
        <rect x="3" y="3" width="7" height="7" rx="2" />
        <rect x="14" y="3" width="7" height="11" rx="2" />
        <rect x="3" y="14" width="7" height="7" rx="2" />
        <rect x="14" y="18" width="7" height="3" rx="1.5" />
      </>
    ),
    scan: (
      <>
        <circle cx="11" cy="11" r="7" />
        <path d="m16.5 16.5 4 4M11 7v4l3 2" />
      </>
    ),
    results: (
      <>
        <path d="M5 4h14v16H5z" />
        <path d="m8 9 2 2 4-4M8 15h8" />
      </>
    ),
    applications: (
      <>
        <rect x="3" y="4" width="18" height="16" rx="3" />
        <path d="M7 8h10M7 12h10M7 16h6" />
      </>
    ),
    updates: (
      <>
        <path d="M12 3v12M7 10l5 5 5-5" />
        <path d="M5 20h14" />
      </>
    ),
    startup: (
      <>
        <path d="M5 20h14M7 20V9l5-5 5 5v11" />
        <path d="M10 14h4" />
      </>
    ),
    storage: (
      <>
        <ellipse cx="12" cy="6" rx="8" ry="3" />
        <path d="M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" />
      </>
    ),
    activity: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3 2" />
      </>
    ),
    privacy: (
      <>
        <path d="M12 3 20 6v6c0 5-3.4 8.1-8 9.5C7.4 20.1 4 17 4 12V6z" />
        <path d="m8.5 12 2.2 2.2 4.8-5" />
      </>
    ),
    settings: (
      <>
        <circle cx="12" cy="12" r="3" />
        <path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1" />
      </>
    ),
    support: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M9.8 9a2.4 2.4 0 0 1 4.6 1c0 1.8-2.4 2.1-2.4 4M12 17h.01" />
      </>
    ),
    sun: (
      <>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5 19 19M19 5l-1.5 1.5M6.5 17.5 5 19" />
      </>
    ),
    moon: <path d="M20 15.2A8.4 8.4 0 0 1 8.8 4 8.4 8.4 0 1 0 20 15.2Z" />,
    arrow: <path d="m9 18 6-6-6-6" />,
    close: <path d="m6 6 12 12M18 6 6 18" />,
    check: <path d="m5 12 4 4 10-10" />,
    attention: (
      <>
        <path d="M12 3 22 20H2z" />
        <path d="M12 9v4M12 17h.01" />
      </>
    ),
    info: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 11v5M12 8h.01" />
      </>
    ),
    search: (
      <>
        <circle cx="10.5" cy="10.5" r="6.5" />
        <path d="m15.5 15.5 5 5" />
      </>
    ),
    filter: <path d="M3 5h18l-7 8v5l-4 2v-7z" />,
    pause: (
      <>
        <path d="M8 6v12M16 6v12" />
      </>
    ),
    play: <path d="m8 5 11 7-11 7z" />,
    stop: <rect x="6" y="6" width="12" height="12" rx="2" />,
    download: (
      <>
        <path d="M12 3v11M8 10l4 4 4-4" />
        <path d="M5 20h14" />
      </>
    ),
    shield: (
      <>
        <path d="M12 3 20 6v6c0 5-3.4 8.1-8 9.5C7.4 20.1 4 17 4 12V6z" />
        <path d="M9 12h6" />
      </>
    ),
    clock: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3 2" />
      </>
    ),
    external: <path d="M14 4h6v6M20 4l-9 9M19 13v6H5V5h6" />,
  };

  return <svg {...common}>{paths[name]}</svg>;
}

function Button({
  children,
  kind = "secondary",
  icon,
  onClick,
  disabled,
  type = "button",
  className = "",
}: {
  children: ReactNode;
  kind?: "primary" | "secondary" | "subtle" | "danger";
  icon?: IconName;
  onClick?: () => void;
  disabled?: boolean;
  type?: "button" | "submit";
  className?: string;
}) {
  return (
    <button
      type={type}
      className={`button button-${kind} ${className}`}
      onClick={onClick}
      disabled={disabled}
    >
      {icon ? <Icon name={icon} size={18} /> : null}
      <span>{children}</span>
    </button>
  );
}

function IconButton({
  label,
  icon,
  onClick,
}: {
  label: string;
  icon: IconName;
  onClick: () => void;
}) {
  return (
    <button type="button" className="icon-button" aria-label={label} title={label} onClick={onClick}>
      <Icon name={icon} />
    </button>
  );
}

function StatusChip({ children, tone = "neutral" }: { children: ReactNode; tone?: Tone }) {
  const icon = tone === "success" ? "check" : tone === "warning" ? "attention" : tone === "danger" ? "attention" : "info";
  return (
    <span className={`status-chip status-${tone}`}>
      <Icon name={icon} size={14} />
      <span>{children}</span>
    </span>
  );
}

function PageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string;
  title: string;
  description: string;
  actions?: ReactNode;
}) {
  return (
    <header className="page-header">
      <div>
        {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
        <h1 tabIndex={-1}>{title}</h1>
        <p>{description}</p>
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </header>
  );
}

function Banner({
  tone,
  title,
  children,
  action,
}: {
  tone: Tone;
  title: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <section className={`banner banner-${tone}`} role={tone === "danger" ? "alert" : "status"}>
      <div className="banner-icon">
        <Icon name={tone === "warning" || tone === "danger" ? "attention" : "info"} />
      </div>
      <div className="banner-copy">
        <strong>{title}</strong>
        <span>{children}</span>
      </div>
      {action ? <div className="banner-action">{action}</div> : null}
    </section>
  );
}

function Metric({
  value,
  label,
  detail,
  tone = "neutral",
}: {
  value: string;
  label: string;
  detail: string;
  tone?: Tone;
}) {
  return (
    <article className={`metric metric-${tone}`} aria-label={`${value}, ${label}`}>
      <div className="metric-value">{value}</div>
      <div className="metric-label">{label}</div>
      <div className="metric-detail">{detail}</div>
    </article>
  );
}

function Dialog({
  title,
  description,
  children,
  onClose,
  size = "standard",
}: {
  title: string;
  description?: string;
  children: ReactNode;
  onClose: () => void;
  size?: "small" | "standard" | "large";
}) {
  return (
    <div className="dialog-backdrop" role="presentation">
      <section
        className={`dialog dialog-${size}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        aria-describedby={description ? "dialog-description" : undefined}
      >
        <div className="dialog-heading">
          <div>
            <h2 id="dialog-title">{title}</h2>
            {description ? <p id="dialog-description">{description}</p> : null}
          </div>
          <IconButton label="Close dialog" icon="close" onClick={onClose} />
        </div>
        {children}
      </section>
    </div>
  );
}

function EmptyState({
  image,
  title,
  body,
  primary,
  secondary,
}: {
  image: string;
  title: string;
  body: string;
  primary: ReactNode;
  secondary?: ReactNode;
}) {
  return (
    <section className="empty-state">
      {/* The illustration is decorative because the adjacent text contains its meaning. */}
      <Image src={image} width={320} height={220} alt="" aria-hidden="true" />
      <h2>{title}</h2>
      <p>{body}</p>
      <div className="button-row">
        {primary}
        {secondary}
      </div>
    </section>
  );
}

export default function Home() {
  const hasCompletedInitialRoute = useRef(false);
  const [route, setRoute] = useState<Route>("overview");
  const [scenario, setScenario] = useState<Scenario>("ready");
  const [theme, setTheme] = useState<"light" | "dark">("light");
  const [progress, setProgress] = useState(42);
  const [paused, setPaused] = useState(false);
  const [cancelled, setCancelled] = useState(false);
  const [selectedFinding, setSelectedFinding] = useState<Finding | null>(null);
  const [selectedUpdate, setSelectedUpdate] = useState(approvedUpdates[0]);
  const [selectedFindings, setSelectedFindings] = useState<string[]>([
    "update-editor",
    "download-cleanup",
  ]);
  const [dialog, setDialog] = useState<
    | null
    | "onboarding"
    | "review"
    | "update"
    | "clear-scan"
    | "clear-downloads"
    | "factory-one"
    | "factory-two"
    | "reset-done"
    | "action-done"
  >(null);
  const [onboardingStep, setOnboardingStep] = useState(0);
  const [firstScanMode, setFirstScanMode] = useState<"local" | "online">("local");
  const [resetPhrase, setResetPhrase] = useState("");
  const [toast, setToast] = useState<string | null>(null);
  const [completionMode, setCompletionMode] = useState<"queue" | "update">("queue");
  const [appSearch, setAppSearch] = useState("");
  const [appStatus, setAppStatus] = useState("All statuses");
  const [settingsTheme, setSettingsTheme] = useState<"system" | "light" | "dark">("system");
  const [schedule, setSchedule] = useState<"manual" | "daily" | "weekly">("weekly");
  const [notifications, setNotifications] = useState(true);

  const filteredApplications = useMemo(
    () =>
      applications.filter((app) => {
        const queryMatches =
          app.name.toLowerCase().includes(appSearch.toLowerCase()) ||
          app.publisher.toLowerCase().includes(appSearch.toLowerCase());
        const statusMatches = appStatus === "All statuses" || app.status === appStatus;
        return queryMatches && statusMatches;
      }),
    [appSearch, appStatus],
  );

  const selectedActionFindings = findings.filter(
    (finding) => selectedFindings.includes(finding.id) && finding.actionable,
  );

  useEffect(() => {
    if (scenario !== "scanning" || paused || cancelled) return;
    const timer = window.setInterval(() => {
      setProgress((current) => {
        if (current >= 100) {
          window.clearInterval(timer);
          window.setTimeout(() => {
            setScenario("complete");
            setRoute("results");
          }, 300);
          return 100;
        }
        return Math.min(100, current + 2);
      });
    }, 600);
    return () => window.clearInterval(timer);
  }, [scenario, paused, cancelled]);

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        if (selectedFinding) setSelectedFinding(null);
        else if (dialog && dialog !== "reset-done") setDialog(null);
      }
      if (event.ctrlKey && event.key.toLowerCase() === "f" && route === "applications") {
        event.preventDefault();
        document.getElementById("application-search")?.focus();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [selectedFinding, dialog, route]);

  useEffect(() => {
    if (!hasCompletedInitialRoute.current) {
      hasCompletedInitialRoute.current = true;
      return;
    }
    const heading = document.querySelector<HTMLElement>("#main-content h1");
    heading?.focus({ preventScroll: true });
    window.scrollTo({ top: 0, left: 0, behavior: "auto" });
  }, [route]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), 6000);
    return () => window.clearTimeout(timer);
  }, [toast]);

  function beginScan() {
    setRoute("scan");
    setScenario("scanning");
    setProgress(18);
    setPaused(false);
    setCancelled(false);
  }

  function cancelScan() {
    setCancelled(true);
    setPaused(false);
    window.setTimeout(() => {
      setScenario("ready");
      setToast("Scan cancelled. Your previous completed inventory is still available.");
    }, 700);
  }

  function toggleFinding(id: string) {
    setSelectedFindings((current) =>
      current.includes(id) ? current.filter((candidate) => candidate !== id) : [...current, id],
    );
  }

  function completeAction(message: string, mode: "queue" | "update") {
    setCompletionMode(mode);
    setDialog("action-done");
    setToast(message);
  }

  const scanPhase =
    progress < 30
      ? "Reading Windows inventory"
      : progress < 55
        ? "Normalising installed applications"
        : progress < 82
          ? "Comparing approved sources"
          : "Preparing your summary";

  const currentNavLabel =
    navGroups.flatMap((group) => group.items).find((item) => item.route === route)?.label ??
    (route === "results" ? "Scan results" : route === "review" ? "Review actions" : "PC Steward");

  function renderOverview() {
    if (scenario === "no-findings") {
      return (
        <>
          <PageHeader
            eyebrow="Overview"
            title="Nothing needs your attention"
            description="The completed scan found no supported action that requires a decision."
            actions={<StatusChip tone="success">Live · checked 4 min ago</StatusChip>}
          />
          <EmptyState
            image="/empty-no-findings.svg"
            title="Your checked applications are current"
            body="PC Steward checked 68 applications against enabled trusted sources. Six unmanaged applications remain visible in the inventory; they are not treated as unsafe."
            primary={
              <Button kind="primary" onClick={() => setRoute("applications")}>
                View applications
              </Button>
            }
            secondary={<Button onClick={beginScan}>Scan again</Button>}
          />
        </>
      );
    }

    if (scenario === "offline") {
      return (
        <>
          <PageHeader
            eyebrow="Overview"
            title="Local information is still available"
            description="You are offline. PC Steward is showing the last local inventory and clearly dated source results."
            actions={<StatusChip tone="neutral">Offline · cached sources</StatusChip>}
          />
          <Banner
            tone="warning"
            title="Approved sources could not be contacted"
            action={<Button onClick={() => setRoute("scan")}>View scan options</Button>}
          >
            Local inventory is current from 9 minutes ago. Update information is cached from 2 days ago
            and cannot be used to start an update.
          </Banner>
          <div className="metric-grid">
            <Metric value="68" label="Applications found" detail="Local inventory · 9 min ago" />
            <Metric value="3" label="Cached update candidates" detail="Review only · 2 days old" tone="warning" />
            <Metric value="6" label="Unmanaged" detail="Visible, not classified as unsafe" />
          </div>
          <section className="section">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Available offline</p>
                <h2>Keep working with local records</h2>
              </div>
            </div>
            <div className="card-grid three">
              <button className="action-card" onClick={() => setRoute("applications")}>
                <Icon name="applications" />
                <strong>Browse applications</strong>
                <span>View installed versions, publishers and scopes.</span>
                <Icon name="arrow" />
              </button>
              <button className="action-card" onClick={() => setRoute("activity")}>
                <Icon name="activity" />
                <strong>Review activity</strong>
                <span>Open previous scans and update outcomes.</span>
                <Icon name="arrow" />
              </button>
              <button className="action-card" onClick={() => setRoute("privacy")}>
                <Icon name="privacy" />
                <strong>Manage local data</strong>
                <span>Exports and data clearing work without a connection.</span>
                <Icon name="arrow" />
              </button>
            </div>
          </section>
        </>
      );
    }

    if (scenario === "partial") {
      return (
        <>
          <PageHeader
            eyebrow="Overview"
            title="Most checks completed"
            description="Useful results are available. One source did not finish and has not been treated as successful."
            actions={<StatusChip tone="warning">Partial · 1 source unavailable</StatusChip>}
          />
          <Banner
            tone="warning"
            title="Microsoft Store source timed out"
            action={<Button onClick={beginScan}>Retry unavailable check</Button>}
          >
            WinGet and all local inventory providers completed. The last Store result from 2 days ago
            remains visible as cached.
          </Banner>
          <div className="metric-grid">
            <Metric value="2" label="Verified updates" detail="Fresh WinGet evidence" tone="brand" />
            <Metric value="1" label="Needs a decision" detail="Source disagreement" tone="warning" />
            <Metric value="61" label="Current" detail="Completed checks" tone="success" />
            <Metric value="1" label="Source unavailable" detail="Cached result retained" tone="warning" />
          </div>
          <FindingsPreview onOpen={setSelectedFinding} />
        </>
      );
    }

    return (
      <>
        <PageHeader
          eyebrow="Overview"
          title="Three updates are ready to review"
          description="PC Steward found clear next steps without changing anything on this PC."
          actions={
            <>
              <StatusChip tone="success">Live · checked 4 min ago</StatusChip>
              <Button kind="primary" onClick={() => setRoute("updates")}>
                Review 3 updates
              </Button>
            </>
          }
        />
        <section className="hero-card">
          <div className="hero-copy">
            <div className="hero-kicker">
              <span className="hero-dot" />
              Evidence before action
            </div>
            <h2>Your PC is accounted for</h2>
            <p>
              68 applications were found. Three verified updates can be reviewed, two items need a
              decision, and six applications remain unmanaged rather than hidden.
            </p>
            <div className="button-row">
              <Button kind="primary" onClick={() => setRoute("updates")}>
                Review updates
              </Button>
              <Button onClick={beginScan}>Scan again</Button>
            </div>
          </div>
          <div className="hero-evidence" aria-label="Scan evidence summary">
            <div className="evidence-row">
              <span>Scan mode</span>
              <strong>Inventory + trusted sources</strong>
            </div>
            <div className="evidence-row">
              <span>Completed</span>
              <strong>Today, 10:42 · 8.6 sec</strong>
            </div>
            <div className="evidence-row">
              <span>Network</span>
              <strong>WinGet and Store only</strong>
            </div>
            <div className="evidence-row">
              <span>Changes made</span>
              <strong>None</strong>
            </div>
          </div>
        </section>
        <div className="metric-grid">
          <Metric value="3" label="Ready to review" detail="Verified package identity" tone="brand" />
          <Metric value="2" label="Need a decision" detail="No automatic action" tone="warning" />
          <Metric value="61" label="Current" detail="No action expected" tone="success" />
          <Metric value="6" label="Unmanaged" detail="Visible, not labelled unsafe" />
        </div>
        <FindingsPreview onOpen={setSelectedFinding} />
        <section className="section split-section">
          <div className="panel">
            <div className="section-heading compact">
              <div>
                <p className="eyebrow">Recent activity</p>
                <h2>What happened recently</h2>
              </div>
              <Button kind="subtle" onClick={() => setRoute("activity")}>
                View all
              </Button>
            </div>
            <div className="mini-timeline">
              {activities.slice(0, 3).map((item) => (
                <div className="mini-event" key={item.title}>
                  <span className={`event-icon event-${item.tone}`}>
                    <Icon name={item.icon} size={18} />
                  </span>
                  <div>
                    <strong>{item.title}</strong>
                    <span>{item.time}</span>
                  </div>
                  <StatusChip tone={item.tone}>{item.status}</StatusChip>
                </div>
              ))}
            </div>
          </div>
          <div className="panel quiet-panel">
            <Icon name="privacy" size={28} />
            <p className="eyebrow">Privacy & resources</p>
            <h2>Local by default. Quiet while idle.</h2>
            <p>
              No account, inventory upload, tray app or always-running service. The next scheduled
              check is scan-only.
            </p>
            <Button kind="subtle" onClick={() => setRoute("privacy")}>
              Review locally stored data
            </Button>
          </div>
        </section>
      </>
    );
  }

  function renderScan() {
    if (scenario === "scanning" || cancelled) {
      if (cancelled) {
        return (
          <>
            <PageHeader
              eyebrow="Scan"
              title="Stopping safely…"
              description="PC Steward has asked active providers to stop. Your previous completed inventory remains available."
            />
            <section className="scan-panel">
              <div className="scan-orbit static">
                <Icon name="stop" size={30} />
              </div>
              <div className="scan-main">
                <StatusChip tone="warning">Cancelling</StatusChip>
                <h2>Waiting for the current provider</h2>
                <p>Cancellation was acknowledged. No partial inventory will replace your previous result.</p>
                <div className="progress-track" aria-label="Cancellation progress">
                  <span style={{ width: "74%" }} />
                </div>
              </div>
            </section>
          </>
        );
      }

      return (
        <>
          <PageHeader
            eyebrow="Scan in progress"
            title={paused ? "Scan paused" : scanPhase}
            description={
              paused
                ? "No scan work is advancing. Resume when you are ready or cancel safely."
                : "PC Steward is reading bounded inventory and trusted source information. Nothing is being changed."
            }
            actions={<StatusChip tone={paused ? "warning" : "brand"}>{paused ? "Paused" : "Reading only"}</StatusChip>}
          />
          <section className="scan-panel">
            <div className={`scan-orbit ${paused ? "static" : ""}`} aria-hidden="true">
              <Image src="/logo-mark.svg" width={68} height={68} alt="" />
              <span className="orbit-dot one" />
              <span className="orbit-dot two" />
              <span className="orbit-dot three" />
            </div>
            <div className="scan-main">
              <div className="scan-topline">
                <div>
                  <span className="progress-label">Overall progress</span>
                  <strong>{progress}%</strong>
                </div>
                <span>Elapsed 00:{Math.max(8, Math.round(progress / 3.2)).toString().padStart(2, "0")}</span>
              </div>
              <div
                className="progress-track"
                role="progressbar"
                aria-label={`Scan progress, ${scanPhase}`}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={progress}
              >
                <span style={{ width: `${progress}%` }} />
              </div>
              <div className="phase-list">
                {[
                  ["Windows inventory", 20],
                  ["Installed application identity", 42],
                  ["Approved source comparison", 70],
                  ["Result summary", 96],
                ].map(([label, threshold]) => {
                  const thresholdNumber = Number(threshold);
                  const state =
                    progress > thresholdNumber + 16
                      ? "complete"
                      : progress >= thresholdNumber - 10
                        ? "current"
                        : "pending";
                  return (
                    <div className={`phase-row phase-${state}`} key={label}>
                      <span className="phase-marker">
                        {state === "complete" ? <Icon name="check" size={14} /> : null}
                      </span>
                      <div>
                        <strong>{label}</strong>
                        <span>
                          {state === "complete"
                            ? "Completed"
                            : state === "current"
                              ? paused
                                ? "Paused"
                                : "In progress"
                              : "Waiting"}
                        </span>
                      </div>
                    </div>
                  );
                })}
              </div>
              <div className="scan-actions">
                <Button
                  icon={paused ? "play" : "pause"}
                  onClick={() => setPaused((value) => !value)}
                >
                  {paused ? "Resume scan" : "Pause"}
                </Button>
                <Button kind="subtle" icon="stop" onClick={cancelScan}>
                  Cancel scan
                </Button>
              </div>
              <p className="supporting-note">
                Pause is shown as a design concept. Production should expose it only after the scan
                orchestrator supports a safe pause state.
              </p>
            </div>
          </section>
        </>
      );
    }

    return (
      <>
        <PageHeader
          eyebrow="Scan"
          title="Choose what to check"
          description="Scanning reads information and prepares evidence. It never installs, removes or deletes anything."
          actions={<StatusChip tone="neutral">Ready · no active work</StatusChip>}
        />
        <div className="choice-grid">
          <label className="choice-card selected">
            <input type="radio" name="scan-mode" defaultChecked />
            <span className="choice-control" />
            <span className="choice-icon">
              <Icon name="applications" />
            </span>
            <strong>Local inventory</strong>
            <span>Recommended for privacy or offline use. No update source is contacted.</span>
            <span className="choice-meta">Target under 15 sec on the baseline PC</span>
          </label>
          <label className="choice-card">
            <input type="radio" name="scan-mode" />
            <span className="choice-control" />
            <span className="choice-icon">
              <Icon name="updates" />
            </span>
            <strong>Inventory + trusted sources</strong>
            <span>Also checks enabled WinGet and Store package sources after consent.</span>
            <span className="choice-meta">Target under 90 sec when sources are healthy</span>
          </label>
        </div>
        <section className="panel section">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Providers</p>
              <h2>What will be read</h2>
            </div>
            <Button kind="subtle">Restore recommended</Button>
          </div>
          <div className="provider-list">
            {[
              ["Windows uninstall registry", "Local · essential", true, true],
              ["Current-user AppX/MSIX catalog", "Local · enabled", true, false],
              ["WinGet installed inventory", "Local command · consented", true, false],
              ["WinGet package sources", "Online metadata · consented", false, false],
            ].map(([name, meta, checked, locked]) => (
              <label className="provider-row" key={String(name)}>
                <span className="provider-icon">
                  <Icon name={String(name).includes("source") ? "updates" : "applications"} />
                </span>
                <span className="provider-copy">
                  <strong>{name}</strong>
                  <span>{meta}</span>
                </span>
                <input
                  type="checkbox"
                  defaultChecked={Boolean(checked)}
                  disabled={Boolean(locked)}
                  aria-label={`${checked ? "Disable" : "Enable"} ${name}`}
                />
              </label>
            ))}
          </div>
        </section>
        <section className="action-footer">
          <div>
            <Icon name="privacy" />
            <span>
              <strong>No account or scan elevation.</strong>
              Results stay on this PC unless you export them.
            </span>
          </div>
          <Button kind="primary" onClick={beginScan}>
            Scan this PC
          </Button>
        </section>
      </>
    );
  }

  function renderResults() {
    if (scenario === "no-findings") {
      return (
        <>
          <PageHeader
            eyebrow="Scan results"
            title="No supported actions found"
            description="The scan completed successfully against enabled sources."
            actions={<StatusChip tone="success">Live · 8.6 sec</StatusChip>}
          />
          <EmptyState
            image="/empty-no-findings.svg"
            title="Nothing needs your attention"
            body="PC Steward checked 68 installed applications. Unmanaged items remain available in Applications and are not presented as threats."
            primary={
              <Button kind="primary" onClick={() => setRoute("applications")}>
                View applications
              </Button>
            }
            secondary={<Button onClick={beginScan}>Scan again</Button>}
          />
        </>
      );
    }

    return (
      <>
        <PageHeader
          eyebrow="Scan results"
          title="Five findings, two supported actions"
          description="Findings are grouped by what you can safely do next. Nothing has been selected automatically."
          actions={
            <>
              <StatusChip tone={scenario === "partial" ? "warning" : "success"}>
                {scenario === "partial" ? "Partial · 1 source unavailable" : "Live · checked 4 min ago"}
              </StatusChip>
              <Button
                kind="primary"
                disabled={selectedActionFindings.length === 0}
                onClick={() => setRoute("review")}
              >
                Review selected ({selectedActionFindings.length})
              </Button>
            </>
          }
        />
        {scenario === "partial" ? (
          <Banner tone="warning" title="One source did not complete">
            Results from completed providers remain available. Microsoft Store is represented by a
            clearly dated cached result.
          </Banner>
        ) : null}
        <div className="result-toolbar">
          <div className="search-field compact-search">
            <Icon name="search" />
            <input aria-label="Search findings" placeholder="Search findings" />
          </div>
          <Button icon="filter">Filters</Button>
          <span className="selection-note" aria-live="polite">
            {selectedActionFindings.length} supported action
            {selectedActionFindings.length === 1 ? "" : "s"} selected
          </span>
        </div>
        <section className="finding-group">
          <div className="group-heading">
            <div>
              <h2>Ready to review</h2>
              <p>Supported actions with bounded scope and clear evidence.</p>
            </div>
            <span>2</span>
          </div>
          {findings
            .filter((finding) => finding.actionable)
            .map((finding) => (
              <FindingRow
                finding={finding}
                key={finding.id}
                selected={selectedFindings.includes(finding.id)}
                onToggle={() => toggleFinding(finding.id)}
                onOpen={() => setSelectedFinding(finding)}
              />
            ))}
        </section>
        <section className="finding-group">
          <div className="group-heading">
            <div>
              <h2>Needs a decision</h2>
              <p>Evidence is incomplete or conflicting. No automatic action is available.</p>
            </div>
            <span>2</span>
          </div>
          {findings
            .filter((finding) => !finding.actionable && finding.tone !== "future")
            .map((finding) => (
              <FindingRow
                finding={finding}
                key={finding.id}
                selected={false}
                onOpen={() => setSelectedFinding(finding)}
              />
            ))}
        </section>
        <section className="finding-group">
          <div className="group-heading">
            <div>
              <h2>Future concept</h2>
              <p>Designed for completeness, but not approved for production action.</p>
            </div>
            <span>1</span>
          </div>
          {findings
            .filter((finding) => finding.tone === "future")
            .map((finding) => (
              <FindingRow
                finding={finding}
                key={finding.id}
                selected={false}
                onOpen={() => setSelectedFinding(finding)}
              />
            ))}
        </section>
      </>
    );
  }

  function renderReview() {
    return (
      <>
        <PageHeader
          eyebrow="Review actions"
          title={`${selectedActionFindings.length} supported actions selected`}
          description="Review the exact target, effect and preservation boundary before anything can begin."
          actions={<StatusChip tone="brand">Sequential review</StatusChip>}
        />
        <Banner tone="brand" title="Nothing has been changed">
          The queue remains editable. Updates run sequentially and app-owned cleanup stays separate.
        </Banner>
        <section className="review-layout">
          <div className="review-list">
            {selectedActionFindings.map((finding, index) => (
              <article className="review-item" key={finding.id}>
                <div className="review-order">{index + 1}</div>
                <div className="review-item-main">
                  <div className="review-title-line">
                    <div>
                      <p className="eyebrow">{finding.kind}</p>
                      <h2>{finding.title}</h2>
                    </div>
                    <StatusChip tone={finding.tone}>{finding.confidence}</StatusChip>
                  </div>
                  <dl className="review-definitions">
                    <div>
                      <dt>Effect</dt>
                      <dd>{finding.effect}</dd>
                    </div>
                    <div>
                      <dt>Preserves</dt>
                      <dd>{finding.preserves}</dd>
                    </div>
                    <div>
                      <dt>Evidence</dt>
                      <dd>{finding.source}</dd>
                    </div>
                  </dl>
                </div>
                <IconButton
                  label={`Remove ${finding.title} from review`}
                  icon="close"
                  onClick={() => toggleFinding(finding.id)}
                />
              </article>
            ))}
            {selectedActionFindings.length === 0 ? (
              <div className="panel">
                <h2>No supported actions selected</h2>
                <p>Return to results and select an action with verified scope.</p>
                <Button onClick={() => setRoute("results")}>Return to results</Button>
              </div>
            ) : null}
          </div>
          <aside className="review-summary">
            <p className="eyebrow">Queue summary</p>
            <h2>What to expect</h2>
            <div className="summary-line">
              <span>Approved updates</span>
              <strong>{selectedActionFindings.filter((item) => item.kind === "Approved update").length}</strong>
            </div>
            <div className="summary-line">
              <span>App-owned cleanup</span>
              <strong>{selectedActionFindings.filter((item) => item.kind === "App-owned cleanup").length}</strong>
            </div>
            <div className="summary-line">
              <span>Potential UAC prompts</span>
              <strong>0</strong>
            </div>
            <div className="summary-line">
              <span>Recoverable space</span>
              <strong>640 MB</strong>
            </div>
            <hr />
            <p>
              Provider completion is not shown as success until the expected installed version is
              verified.
            </p>
            <Button
              kind="primary"
              disabled={selectedActionFindings.length === 0}
              onClick={() => setDialog("review")}
            >
              Continue review
            </Button>
            <Button kind="subtle" onClick={() => setRoute("results")}>
              Back to results
            </Button>
          </aside>
        </section>
      </>
    );
  }

  function renderApplications() {
    return (
      <>
        <PageHeader
          eyebrow="Applications"
          title="Installed application inventory"
          description="Canonical products are shown once while distinct user, machine and architecture instances remain inspectable."
          actions={
            <>
              <StatusChip tone="success">Local inventory · 9 min ago</StatusChip>
              <Button icon="download">Export</Button>
            </>
          }
        />
        <div className="table-toolbar">
          <label className="search-field">
            <Icon name="search" />
            <span className="sr-only">Search applications</span>
            <input
              id="application-search"
              value={appSearch}
              onChange={(event) => setAppSearch(event.target.value)}
              placeholder="Search applications or publishers"
            />
            {appSearch ? (
              <button type="button" aria-label="Clear search" onClick={() => setAppSearch("")}>
                <Icon name="close" size={16} />
              </button>
            ) : null}
          </label>
          <label className="select-field">
            <span>Status</span>
            <select value={appStatus} onChange={(event) => setAppStatus(event.target.value)}>
              <option>All statuses</option>
              <option>Verified update</option>
              <option>Review required</option>
              <option>Current</option>
              <option>Unmanaged</option>
              <option>Pinned</option>
            </select>
          </label>
          <Button icon="filter">More filters</Button>
          <span className="result-count" aria-live="polite">
            {filteredApplications.length} applications
          </span>
        </div>
        <div className="data-table-shell">
          <table className="data-table">
            <thead>
              <tr>
                <th aria-sort="ascending">
                  Application <span aria-hidden="true">↑</span>
                </th>
                <th>Installed version</th>
                <th>Status</th>
                <th>Publisher</th>
                <th>Scope</th>
                <th>Source</th>
                <th>
                  <span className="sr-only">Open</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {filteredApplications.map((app) => (
                <tr key={app.name}>
                  <td>
                    <button
                      type="button"
                      className="table-primary"
                      onClick={() =>
                        setSelectedFinding({
                          id: `app-${app.name}`,
                          title: app.name,
                          summary: `${app.publisher} · ${app.scope} installation`,
                          severity: app.status,
                          confidence: app.status === "Verified update" ? "Verified match" : "Inventory evidence",
                          tone: app.tone,
                          source: app.source,
                          actionable: app.status === "Verified update",
                          kind: "Application detail",
                          effect:
                            app.status === "Verified update"
                              ? `A reviewed update can move ${app.name} from ${app.version} to its verified candidate.`
                              : "No automatic change is currently proposed.",
                          preserves: "All unrelated applications and user data.",
                        })
                      }
                    >
                      <span className="app-avatar">{app.name.slice(0, 1)}</span>
                      <span>
                        <strong>{app.name}</strong>
                        <small>{app.scope} installation</small>
                      </span>
                    </button>
                  </td>
                  <td className="mono">{app.version}</td>
                  <td>
                    <StatusChip tone={app.tone}>{app.status}</StatusChip>
                  </td>
                  <td>{app.publisher}</td>
                  <td>{app.scope}</td>
                  <td>{app.source}</td>
                  <td>
                    <Icon name="arrow" size={16} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {filteredApplications.length === 0 ? (
            <div className="table-empty">
              <h2>No applications match these filters</h2>
              <p>Change the search or clear the status filter.</p>
              <Button
                onClick={() => {
                  setAppSearch("");
                  setAppStatus("All statuses");
                }}
              >
                Clear filters
              </Button>
            </div>
          ) : null}
        </div>
      </>
    );
  }

  function renderUpdates() {
    return (
      <>
        <PageHeader
          eyebrow="Updates"
          title="Review trusted update evidence"
          description="Only fresh, compatible and explicitly reviewed package identities can move into an update queue."
          actions={
            <>
              <StatusChip tone="success">WinGet · live 4 min ago</StatusChip>
              <Button icon="scan" onClick={beginScan}>
                Check again
              </Button>
            </>
          }
        />
        <div className="tab-list" role="tablist" aria-label="Update status">
          <button type="button" role="tab" aria-selected="true">
            Ready to review <span>3</span>
          </button>
          <button type="button" role="tab" aria-selected="false">
            Needs decision <span>2</span>
          </button>
          <button type="button" role="tab" aria-selected="false">
            Pinned & ignored <span>4</span>
          </button>
          <button type="button" role="tab" aria-selected="false">
            Unavailable <span>1</span>
          </button>
        </div>
        <section className="update-list">
          {approvedUpdates.map((item, index) => (
            <article className="update-row" key={item.name}>
              <label className="update-select">
                <input
                  type="checkbox"
                  defaultChecked={index === 0}
                  aria-label={`Select ${item.name} update`}
                />
              </label>
              <span className="app-avatar">{item.name.slice(0, 1)}</span>
              <div className="update-main">
                <div className="update-title">
                  <h2>{item.name}</h2>
                  <StatusChip tone="brand">Verified match</StatusChip>
                </div>
                <p>{item.publisher}</p>
                <div className="update-meta">
                  <span className="mono">{item.currentVersion} → {item.targetVersion}</span>
                  <span>{item.scope} scope · {item.architecture.toLowerCase()} · stable</span>
                  <span>{item.source} · live</span>
                </div>
                <code>{item.package}</code>
              </div>
              <Button
                onClick={() => {
                  setSelectedUpdate(item);
                  setDialog("update");
                }}
              >
                Review update
              </Button>
            </article>
          ))}
        </section>
        <Banner tone="warning" title="Archive Studio has source disagreement">
          WinGet reports 7.5 while Store reports 7.4.1. Both source timestamps remain visible and no
          source is selected automatically.
          <Button kind="subtle" onClick={() => setSelectedFinding(findings[1])}>
            Compare sources
          </Button>
        </Banner>
      </>
    );
  }

  function renderStartup() {
    const startupRows = [
      ["Cloud Sync", "Wingtip", "Enabled", "Registry · current user", "Evidence not yet defined"],
      ["Contoso Chat", "Contoso Software", "Enabled", "Startup task", "Evidence not yet defined"],
      ["Audio Console", "Fabrikam Devices", "Disabled", "Registry · machine", "Disabled by user"],
    ];
    return (
      <>
        <PageHeader
          eyebrow="Future concept"
          title="Startup applications"
          description="A read-only design for future evidence review. Startup control is outside the approved PC Steward MVP."
          actions={<StatusChip tone="future">Not a production module</StatusChip>}
        />
        <Banner tone="future" title="Product approval required">
          This prototype does not infer “impact,” disable entries or change Windows. A production
          module needs requirements, rollback, safety and performance evidence first.
        </Banner>
        <div className="table-toolbar">
          <div className="search-field">
            <Icon name="search" />
            <input aria-label="Search startup applications" placeholder="Search startup applications" />
          </div>
          <Button icon="filter">Status: All</Button>
        </div>
        <div className="data-table-shell">
          <table className="data-table">
            <thead>
              <tr>
                <th>Startup entry</th>
                <th>Publisher</th>
                <th>State</th>
                <th>Source</th>
                <th>Evidence</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {startupRows.map((row) => (
                <tr key={row[0]}>
                  <td>
                    <span className="table-primary static">
                      <span className="app-avatar">{row[0].slice(0, 1)}</span>
                      <span>
                        <strong>{row[0]}</strong>
                        <small>Future read-only record</small>
                      </span>
                    </span>
                  </td>
                  <td>{row[1]}</td>
                  <td>
                    <StatusChip tone={row[2] === "Enabled" ? "warning" : "neutral"}>{row[2]}</StatusChip>
                  </td>
                  <td>{row[3]}</td>
                  <td>{row[4]}</td>
                  <td>
                    <Button kind="subtle" onClick={() => setSelectedFinding(findings[4])}>
                      View concept
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </>
    );
  }

  function renderStorage() {
    return (
      <>
        <PageHeader
          eyebrow="Storage"
          title="Storage used by PC Steward"
          description="The approved scope covers only data and inactive update files owned by PC Steward."
          actions={<StatusChip tone="success">App-owned scope verified</StatusChip>}
        />
        <section className="storage-overview panel">
          <div className="storage-ring" role="img" aria-label="PC Steward app-owned storage, 1.34 gigabytes total">
            <div>
              <strong>1.34 GB</strong>
              <span>app-owned data</span>
            </div>
          </div>
          <div className="storage-legend" aria-label="Storage category list">
            <p className="eyebrow">Category breakdown</p>
            {[
              ["Downloaded files", "640 MB", "blue"],
              ["Scan data", "342 MB", "cyan"],
              ["Source metadata", "198 MB", "green"],
              ["Logs & history", "160 MB", "amber"],
            ].map(([label, amount, color]) => (
              <div className="legend-row" key={label}>
                <span className={`legend-swatch swatch-${color}`} />
                <span>{label}</span>
                <strong>{amount}</strong>
              </div>
            ))}
          </div>
          <div className="storage-summary">
            <p className="eyebrow">Recoverable now</p>
            <strong>640 MB</strong>
            <p>Inactive PC Steward download files only.</p>
            <Button kind="primary" onClick={() => setDialog("clear-downloads")}>
              Review cleanup
            </Button>
          </div>
        </section>
        <section className="section">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Boundaries</p>
              <h2>What PC Steward will not touch</h2>
            </div>
          </div>
          <div className="boundary-grid">
            {["Installed applications", "Windows caches", "WinGet and Store caches", "Documents and personal files"].map(
              (label) => (
                <div className="boundary-item" key={label}>
                  <Icon name="shield" />
                  <span>{label}</span>
                  <strong>Preserved</strong>
                </div>
              ),
            )}
          </div>
        </section>
        <section className="future-panel">
          <div>
            <StatusChip tone="future">Future concept</StatusChip>
            <h2>Device storage analysis</h2>
            <p>
              A broader read-only map could show user-approved locations, but it is outside the
              authoritative product scope. A chart would never become a direct delete surface.
            </p>
          </div>
          <div className="treemap" aria-label="Illustrative future device storage categories">
            <span className="tree-apps">Applications</span>
            <span className="tree-user">User files</span>
            <span className="tree-system">Windows</span>
            <span className="tree-other">Other</span>
          </div>
        </section>
      </>
    );
  }

  function renderActivity() {
    return (
      <>
        <PageHeader
          eyebrow="Activity"
          title="A local record of what happened"
          description="Scans, update attempts, decisions and app-owned cleanup remain on this PC."
          actions={<Button icon="download">Export sanitized history</Button>}
        />
        <div className="table-toolbar">
          <Button icon="filter">All activity</Button>
          <Button icon="clock">Last 30 days</Button>
          <span className="result-count">24 events</span>
        </div>
        <section className="timeline">
          {activities.map((item) => (
            <article className="timeline-item" key={item.title}>
              <div className={`timeline-icon event-${item.tone}`}>
                <Icon name={item.icon} />
              </div>
              <div className="timeline-copy">
                <div>
                  <h2>{item.title}</h2>
                  <span>{item.time}</span>
                </div>
                <p>{item.detail}</p>
                <StatusChip tone={item.tone}>{item.status}</StatusChip>
              </div>
              <Button
                kind="subtle"
                onClick={() =>
                  setSelectedFinding({
                    id: item.title,
                    title: item.title,
                    summary: item.detail,
                    severity: item.status,
                    confidence: "Recorded local evidence",
                    tone: item.tone,
                    source: item.time,
                    actionable: false,
                    kind: "Activity detail",
                    effect: "This is a historical record; opening it does not repeat the action.",
                    preserves: "No state is changed.",
                  })
                }
              >
                View details
              </Button>
            </article>
          ))}
        </section>
      </>
    );
  }

  function renderPrivacy() {
    return (
      <>
        <PageHeader
          eyebrow="Privacy & data"
          title="Your PC Steward data stays on this PC"
          description="No account is required. Exports happen only when you choose them, and each data category has a separate lifecycle."
          actions={<StatusChip tone="success">Local-first</StatusChip>}
        />
        <section className="privacy-hero">
          <div className="privacy-icon">
            <Icon name="privacy" size={32} />
          </div>
          <div>
            <h2>No inventory upload or background telemetry</h2>
            <p>
              The last approved network contact was WinGet at 10:42 today. Local data clearing works
              offline and does not send a deletion event.
            </p>
          </div>
          <Button icon="download">Export inventory</Button>
        </section>
        <section className="section">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Locally stored categories</p>
              <h2>Clear only what you intend</h2>
            </div>
          </div>
          <div className="data-card-grid">
            {dataCategories.map((item) => (
              <article className="data-card" key={item.id}>
                <span className="data-icon">
                  <Icon name={item.icon} />
                </span>
                <div>
                  <h3>{item.title}</h3>
                  <strong>{item.amount}</strong>
                  <p>{item.detail}</p>
                </div>
                <Button
                  kind={item.id === "scan-data" || item.id === "downloads" ? "secondary" : "subtle"}
                  onClick={() => {
                    if (item.id === "scan-data") setDialog("clear-scan");
                    else if (item.id === "downloads") setDialog("clear-downloads");
                    else if (item.id === "history") setRoute("activity");
                    else setRoute("settings");
                  }}
                >
                  {item.action}
                </Button>
              </article>
            ))}
          </div>
        </section>
        <section className="danger-zone">
          <div className="danger-zone-icon">
            <Icon name="attention" />
          </div>
          <div>
            <p className="eyebrow">Separate destructive area</p>
            <h2>Factory reset PC Steward</h2>
            <p>
              Removes all local PC Steward state and the current-user schedule. PC Steward remains
              installed, and every third-party application is preserved.
            </p>
          </div>
          <Button kind="danger" onClick={() => setDialog("factory-one")}>
            Factory reset PC Steward
          </Button>
        </section>
      </>
    );
  }

  function renderSettings() {
    return (
      <>
        <PageHeader
          eyebrow="Settings"
          title="Preferences and trusted sources"
          description="Reversible preferences save immediately. Privacy-changing sources ask before they are enabled."
        />
        <div className="settings-layout">
          <aside className="settings-index" aria-label="Settings sections">
            {["Sources", "Schedule & notifications", "Appearance", "Resource & retention", "Decisions"].map(
              (item, index) => (
                <button type="button" className={index === 0 ? "active" : ""} key={item}>
                  {item}
                </button>
              ),
            )}
          </aside>
          <div className="settings-content">
            <section className="settings-section">
              <div>
                <p className="eyebrow">Sources</p>
                <h2>Inventory and update evidence</h2>
                <p>Essential local inventory stays enabled. Online sources require disclosure approval.</p>
              </div>
              <div className="setting-list">
                <SettingRow
                  title="Windows uninstall registry"
                  description="Essential local inventory · never contacts a network source"
                  checked
                  disabled
                />
                <SettingRow
                  title="Current-user AppX/MSIX catalog"
                  description="Local Windows package inventory"
                  checked
                />
                <SettingRow
                  title="WinGet installed inventory"
                  description="Local command inventory · source disclosure approved"
                  checked
                />
                <SettingRow
                  title="WinGet package sources"
                  description="Approved online metadata · last successful check 10:42 today"
                  checked
                />
              </div>
            </section>
            <section className="settings-section">
              <div className="setting-heading-row">
                <div>
                  <p className="eyebrow">Schedule & notifications</p>
                  <h2>Weekly scan-only check</h2>
                  <p>Checks only. Scheduled work never installs updates.</p>
                </div>
                <StatusChip tone="brand">Phase 4 design</StatusChip>
              </div>
              <div className="schedule-choice" role="radiogroup" aria-label="Scan schedule">
                {[
                  ["manual", "Manual only"],
                  ["daily", "Daily"],
                  ["weekly", "Weekly"],
                ].map(([value, label]) => (
                  <label className={schedule === value ? "selected" : ""} key={value}>
                    <input
                      type="radio"
                      name="schedule"
                      value={value}
                      checked={schedule === value}
                      onChange={() => setSchedule(value as typeof schedule)}
                    />
                    <span>{label}</span>
                  </label>
                ))}
              </div>
              {schedule !== "manual" ? (
                <div className="schedule-fields">
                  {schedule === "weekly" ? (
                    <label>
                      <span>Day</span>
                      <select defaultValue="Wednesday">
                        <option>Monday</option>
                        <option>Tuesday</option>
                        <option>Wednesday</option>
                        <option>Thursday</option>
                        <option>Friday</option>
                      </select>
                    </label>
                  ) : null}
                  <label>
                    <span>Time</span>
                    <input type="time" defaultValue="18:00" />
                  </label>
                  <div className="next-run">
                    <Icon name="clock" />
                    <span>
                      <strong>Next check</strong>
                      Wednesday, 18:00 · exits when complete
                    </span>
                  </div>
                </div>
              ) : null}
              <SettingRow
                title="Windows notifications"
                description="Notify only for attention-needed results or repeated scan failure"
                checked={notifications}
                onChange={setNotifications}
              />
            </section>
            <section className="settings-section">
              <div>
                <p className="eyebrow">Appearance</p>
                <h2>Follow Windows or choose a theme</h2>
              </div>
              <div className="schedule-choice" role="radiogroup" aria-label="Appearance theme">
                {[
                  ["system", "Use Windows setting"],
                  ["light", "Light"],
                  ["dark", "Dark"],
                ].map(([value, label]) => (
                  <label className={settingsTheme === value ? "selected" : ""} key={value}>
                    <input
                      type="radio"
                      name="settings-theme"
                      checked={settingsTheme === value}
                      onChange={() => {
                        setSettingsTheme(value as typeof settingsTheme);
                        if (value !== "system") setTheme(value as typeof theme);
                      }}
                    />
                    <span>{label}</span>
                  </label>
                ))}
              </div>
            </section>
          </div>
        </div>
      </>
    );
  }

  function renderSupport() {
    return (
      <>
        <PageHeader
          eyebrow="About & support"
          title="PC Steward"
          description="A local-first software inventory and trusted update steward for Windows 11."
          actions={<StatusChip tone="neutral">Design prototype · no scanning</StatusChip>}
        />
        <section className="about-card">
          <Image src="/logo-mark.svg" width={60} height={60} alt="" aria-hidden="true" />
          <div>
            <h2>PC Steward 0.1 private-beta design</h2>
            <p>Windows 11 x64 · WinUI 3 direction · local-first and account-free</p>
          </div>
          <Button icon="external">View release notes</Button>
        </section>
        <div className="card-grid three section">
          <article className="support-card">
            <Icon name="privacy" />
            <h2>Privacy summary</h2>
            <p>Inventory and history stay local. Network access is limited to enabled trusted sources.</p>
            <Button kind="subtle" onClick={() => setRoute("privacy")}>
              Review local data
            </Button>
          </article>
          <article className="support-card">
            <Icon name="support" />
            <h2>Sanitized diagnostics</h2>
            <p>Create a bounded support bundle without credentials, licence keys or full environment data.</p>
            <Button kind="subtle" onClick={() => setToast("Prototype: sanitized diagnostic review opened.")}>
              Review diagnostic bundle
            </Button>
          </article>
          <article className="support-card">
            <Icon name="info" />
            <h2>Known limitations</h2>
            <p>Unmanaged software is expected. PC Steward does not update drivers, firmware or Windows.</p>
            <Button kind="subtle">View limitations</Button>
          </article>
        </div>
        <section className="panel section">
          <div className="section-heading compact">
            <div>
              <p className="eyebrow">Product boundaries</p>
              <h2>What PC Steward is not</h2>
            </div>
          </div>
          <div className="boundary-grid">
            {["Antivirus", "Registry cleaner", "Driver updater", "Windows Update manager"].map((label) => (
              <div className="boundary-item" key={label}>
                <Icon name="shield" />
                <span>{label}</span>
                <strong>Out of scope</strong>
              </div>
            ))}
          </div>
        </section>
      </>
    );
  }

  function renderRoute() {
    switch (route) {
      case "overview":
        return renderOverview();
      case "scan":
        return renderScan();
      case "results":
        return renderResults();
      case "review":
        return renderReview();
      case "applications":
        return renderApplications();
      case "updates":
        return renderUpdates();
      case "startup":
        return renderStartup();
      case "storage":
        return renderStorage();
      case "activity":
        return renderActivity();
      case "privacy":
        return renderPrivacy();
      case "settings":
        return renderSettings();
      case "support":
        return renderSupport();
      default:
        return renderOverview();
    }
  }

  return (
    <div className="prototype-app" data-theme={theme}>
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>
      <aside className="side-nav" aria-label="PC Steward">
        <button type="button" className="brand" onClick={() => setRoute("overview")} aria-label="PC Steward overview">
          <Image src="/logo-mark.svg" width={36} height={36} alt="" aria-hidden="true" />
          <span>
            <strong>PC Steward</strong>
            <small>Local-first care</small>
          </span>
        </button>
        <nav>
          {navGroups.map((group) => (
            <div className="nav-group" key={group.label}>
              <p>{group.label}</p>
              {group.items.map((item) => (
                <button
                  type="button"
                  key={item.route}
                  className={route === item.route ? "active" : ""}
                  aria-current={route === item.route ? "page" : undefined}
                  aria-label={`${item.label}${item.badge ? `, ${item.badge} items` : ""}${item.future ? ", future concept" : ""}`}
                  title={item.label}
                  onClick={() => setRoute(item.route)}
                >
                  <Icon name={item.icon} />
                  <span>{item.label}</span>
                  {item.future ? <em>Future</em> : null}
                  {item.badge ? <b aria-label={`${item.badge} items`}>{item.badge}</b> : null}
                </button>
              ))}
            </div>
          ))}
        </nav>
        <div className="nav-bottom">
          <button
            type="button"
            className={route === "settings" ? "active" : ""}
            aria-current={route === "settings" ? "page" : undefined}
            aria-label="Settings"
            onClick={() => setRoute("settings")}
          >
            <Icon name="settings" />
            <span>Settings</span>
          </button>
          <button
            type="button"
            className={route === "support" ? "active" : ""}
            aria-current={route === "support" ? "page" : undefined}
            aria-label="About & support"
            onClick={() => setRoute("support")}
          >
            <Icon name="support" />
            <span>About & support</span>
          </button>
        </div>
      </aside>
      <div className="app-column">
        <header className="top-bar">
          <div className="breadcrumb">
            <span>PC Steward</span>
            <Icon name="arrow" size={14} />
            <strong>{currentNavLabel}</strong>
          </div>
          <div className="prototype-controls">
            <label className="scenario-control">
              <span>Prototype state</span>
              <select
                aria-label="Prototype state"
                value={scenario}
                onChange={(event) => {
                  const next = event.target.value as Scenario;
                  setScenario(next);
                  if (next === "first-launch") {
                    setOnboardingStep(0);
                    setDialog("onboarding");
                    setRoute("overview");
                  } else if (next === "scanning") {
                    setRoute("scan");
                    setProgress(42);
                    setCancelled(false);
                  } else if (next === "complete" || next === "partial") {
                    setRoute("overview");
                  } else {
                    setRoute("overview");
                  }
                }}
              >
                <option value="first-launch">First launch</option>
                <option value="ready">Ready / cached</option>
                <option value="scanning">Scanning</option>
                <option value="complete">Scan complete</option>
                <option value="no-findings">No findings</option>
                <option value="partial">Partial failure</option>
                <option value="offline">Offline</option>
              </select>
            </label>
            <IconButton
              label={theme === "light" ? "Use dark theme" : "Use light theme"}
              icon={theme === "light" ? "moon" : "sun"}
              onClick={() => setTheme((value) => (value === "light" ? "dark" : "light"))}
            />
            <span className="local-badge">
              <Icon name="privacy" size={16} />
              Stored locally
            </span>
          </div>
        </header>
        {scenario === "scanning" ? (
          <button type="button" className="global-operation" onClick={() => setRoute("scan")}>
            <span className="operation-pulse" />
            <strong>{paused ? "Scan paused" : scanPhase}</strong>
            <span>{progress}% · View progress</span>
            <Icon name="arrow" size={16} />
          </button>
        ) : null}
        <main id="main-content" className="main-content" tabIndex={-1}>
          {renderRoute()}
        </main>
      </div>

      {selectedFinding ? (
        <aside
          className="detail-pane"
          role="dialog"
          aria-modal="false"
          aria-labelledby="detail-title"
        >
          <div className="detail-heading">
            <div>
              <p className="eyebrow">{selectedFinding.kind}</p>
              <h2 id="detail-title">{selectedFinding.title}</h2>
            </div>
            <IconButton label="Close details" icon="close" onClick={() => setSelectedFinding(null)} />
          </div>
          <div className="detail-status">
            <StatusChip tone={selectedFinding.tone}>{selectedFinding.severity}</StatusChip>
            <StatusChip tone={selectedFinding.tone}>{selectedFinding.confidence}</StatusChip>
          </div>
          <p className="detail-summary">{selectedFinding.summary}</p>
          <section className="detail-section">
            <h3>Why this appears</h3>
            <div className="evidence-box">
              <div>
                <span>Evidence</span>
                <strong>{selectedFinding.source}</strong>
              </div>
              <div>
                <span>Actionability</span>
                <strong>{selectedFinding.actionable ? "Supported after review" : "No automatic action"}</strong>
              </div>
              <div>
                <span>Changes made</span>
                <strong>None</strong>
              </div>
            </div>
          </section>
          <section className="detail-section">
            <h3>What PC Steward can do</h3>
            <p>{selectedFinding.effect}</p>
          </section>
          <section className="detail-section">
            <h3>What is preserved</h3>
            <p>{selectedFinding.preserves}</p>
          </section>
          <details className="technical-details">
            <summary>Technical details</summary>
            <dl>
              <div>
                <dt>Stable finding ID</dt>
                <dd className="mono">{selectedFinding.id}</dd>
              </div>
              <div>
                <dt>Source freshness</dt>
                <dd>{selectedFinding.source}</dd>
              </div>
            </dl>
          </details>
          <div className="detail-actions">
            {selectedFinding.actionable ? (
              <Button
                kind="primary"
                onClick={() => {
                  if (!selectedFindings.includes(selectedFinding.id)) toggleFinding(selectedFinding.id);
                  setSelectedFinding(null);
                  setRoute("review");
                }}
              >
                Add to review
              </Button>
            ) : (
              <Button disabled>No automatic action</Button>
            )}
            <Button kind="subtle" onClick={() => setSelectedFinding(null)}>
              Close
            </Button>
          </div>
        </aside>
      ) : null}

      {dialog === "onboarding" ? (
        <Dialog
          title={
            onboardingStep === 0
              ? "Know what is installed. Decide what changes."
              : onboardingStep === 1
                ? "Choose your first scan"
                : "Review before scanning"
          }
          description={
            onboardingStep === 0
              ? "PC Steward is local-first, account-free and built around explicit approval."
              : undefined
          }
          onClose={() => {
            setDialog(null);
            setScenario("ready");
          }}
          size="large"
        >
          {onboardingStep === 0 ? (
            <>
              <div className="onboarding-hero">
                <Image src="/empty-first-scan.svg" width={520} height={340} alt="" aria-hidden="true" />
                <div className="trust-list">
                  <div>
                    <Icon name="privacy" />
                    <span>
                      <strong>Stored on this PC</strong>
                      No account or inventory upload.
                    </span>
                  </div>
                  <div>
                    <Icon name="scan" />
                    <span>
                      <strong>Scanning reads only</strong>
                      No install, removal or elevation.
                    </span>
                  </div>
                  <div>
                    <Icon name="check" />
                    <span>
                      <strong>You approve every change</strong>
                      Evidence appears before action.
                    </span>
                  </div>
                </div>
              </div>
              <div className="dialog-actions">
                <Button kind="subtle" onClick={() => setRoute("privacy")}>
                  Read privacy summary
                </Button>
                <Button kind="primary" onClick={() => setOnboardingStep(1)}>
                  Continue
                </Button>
              </div>
            </>
          ) : onboardingStep === 1 ? (
            <>
              <div className="onboarding-options">
                <label className={firstScanMode === "local" ? "selected" : ""}>
                  <input
                    type="radio"
                    name="first-scan"
                    checked={firstScanMode === "local"}
                    onChange={() => setFirstScanMode("local")}
                  />
                  <span className="option-icon">
                    <Icon name="applications" />
                  </span>
                  <strong>Local-only inventory</strong>
                  <p>Recommended. Reads Windows inventory sources and makes no network request.</p>
                  <StatusChip tone="success">Private default</StatusChip>
                </label>
                <label className={firstScanMode === "online" ? "selected" : ""}>
                  <input
                    type="radio"
                    name="first-scan"
                    checked={firstScanMode === "online"}
                    onChange={() => setFirstScanMode("online")}
                  />
                  <span className="option-icon">
                    <Icon name="updates" />
                  </span>
                  <strong>Inventory + update check</strong>
                  <p>Also contacts enabled WinGet sources after source-disclosure approval.</p>
                  <StatusChip tone="warning">Network consent</StatusChip>
                </label>
              </div>
              {firstScanMode === "online" ? (
                <Banner tone="warning" title="Source disclosure">
                  WinGet may disclose installed package identifiers to configured package sources. It
                  remains read-only and can be disabled later.
                </Banner>
              ) : null}
              <div className="dialog-actions">
                <Button kind="subtle" onClick={() => setOnboardingStep(0)}>
                  Back
                </Button>
                <Button kind="primary" onClick={() => setOnboardingStep(2)}>
                  Continue
                </Button>
              </div>
            </>
          ) : (
            <>
              <div className="review-summary-card">
                <div>
                  <span>Scan mode</span>
                  <strong>{firstScanMode === "local" ? "Local-only inventory" : "Inventory + trusted sources"}</strong>
                </div>
                <div>
                  <span>Administrator access</span>
                  <strong>Not requested</strong>
                </div>
                <div>
                  <span>Changes to this PC</span>
                  <strong>None</strong>
                </div>
                <div>
                  <span>Stored result</span>
                  <strong>Current Windows profile only</strong>
                </div>
              </div>
              <p className="supporting-note">
                You can cancel while scanning. Your previous completed inventory is replaced only after a
                successful scan.
              </p>
              <div className="dialog-actions">
                <Button kind="subtle" onClick={() => setOnboardingStep(1)}>
                  Back
                </Button>
                <Button
                  kind="primary"
                  onClick={() => {
                    setDialog(null);
                    beginScan();
                  }}
                >
                  Start first scan
                </Button>
              </div>
            </>
          )}
        </Dialog>
      ) : null}

      {dialog === "review" ? (
        <Dialog
          title={`Confirm ${selectedActionFindings.length} actions?`}
          description="Review the frozen queue. If any update evidence changes before execution, PC Steward will stop and ask you to review again."
          onClose={() => setDialog(null)}
          size="large"
        >
          <div className="confirm-list">
            {selectedActionFindings.map((item, index) => (
              <div key={item.id}>
                <span>{index + 1}</span>
                <div>
                  <strong>{item.title}</strong>
                  <p>{item.effect}</p>
                </div>
                <StatusChip tone={item.tone}>{item.confidence}</StatusChip>
              </div>
            ))}
          </div>
          <Banner tone="brand" title="Updates run sequentially">
            PC Steward will verify the installed version before recording success. No application will
            be force-closed.
          </Banner>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="primary"
              onClick={() => completeAction("Prototype actions completed safely.", "queue")}
            >
              Confirm {selectedActionFindings.length} actions
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "update" ? (
        <Dialog
          title="Review approved update"
          description="The installed application and candidate package are compared before the update can begin."
          onClose={() => setDialog(null)}
          size="large"
        >
          <div className="comparison-grid">
            <section>
              <p className="eyebrow">Installed</p>
              <h3>{selectedUpdate.name}</h3>
              <dl>
                <div>
                  <dt>Version</dt>
                  <dd className="mono">{selectedUpdate.currentVersion}</dd>
                </div>
                <div>
                  <dt>Publisher</dt>
                  <dd>{selectedUpdate.publisher}</dd>
                </div>
                <div>
                  <dt>Scope</dt>
                  <dd>{selectedUpdate.scope}</dd>
                </div>
                <div>
                  <dt>Architecture</dt>
                  <dd>{selectedUpdate.architecture}</dd>
                </div>
              </dl>
            </section>
            <div className="comparison-arrow">
              <Icon name="arrow" />
            </div>
            <section>
              <p className="eyebrow">Candidate</p>
              <h3>{selectedUpdate.package}</h3>
              <dl>
                <div>
                  <dt>Version</dt>
                  <dd className="mono">{selectedUpdate.targetVersion}</dd>
                </div>
                <div>
                  <dt>Source</dt>
                  <dd>{selectedUpdate.source}</dd>
                </div>
                <div>
                  <dt>Scope</dt>
                  <dd>{selectedUpdate.scope}</dd>
                </div>
                <div>
                  <dt>Architecture</dt>
                  <dd>{selectedUpdate.architecture}</dd>
                </div>
              </dl>
            </section>
          </div>
          <div className="evidence-callout">
            <StatusChip tone="brand">Verified match</StatusChip>
            <div>
              <strong>Exact package identity and compatible target</strong>
              <p>
                Source `{selectedUpdate.source.toLowerCase()}`, package `{selectedUpdate.package}`,
                stable channel. Live evidence checked 4 minutes ago.
              </p>
            </div>
          </div>
          <Banner tone="brand" title="No administrator prompt expected">
            This is a user-scope update. PC Steward will not close the application and will verify the
            installed version afterward.
          </Banner>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="primary"
              onClick={() =>
                completeAction(`${selectedUpdate.name} update was simulated and verified.`, "update")
              }
            >
              Install approved update
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "clear-scan" ? (
        <Dialog
          title="Clear scan data?"
          description="This action returns Overview to a no-scan state. It does not reset PC Steward."
          onClose={() => setDialog(null)}
        >
          <DeletePreserve
            deletes={[
              "Current inventory",
              "Raw provider records",
              "Update candidates",
              "Scan runs and provider timings",
            ]}
            preserves={[
              "Settings and enabled sources",
              "Schedule and update history",
              "Downloaded files",
              "Mappings, pins and ignores",
            ]}
          />
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="danger"
              onClick={() => {
                setDialog(null);
                setScenario("ready");
                setToast("Scan data cleared. Settings, decisions, history and downloads were preserved.");
              }}
            >
              Clear scan data
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "clear-downloads" ? (
        <Dialog
          title="Delete 640 MB of downloaded files?"
          description="Only inactive files owned by PC Steward in its fixed download cache are included."
          onClose={() => setDialog(null)}
        >
          <DeletePreserve
            deletes={["Inactive PC Steward update payloads", "Inactive temporary extraction files"]}
            preserves={[
              "Installed applications",
              "Active update files",
              "Scan data and update history",
              "Windows, WinGet, Store and other provider caches",
            ]}
          />
          <Banner tone="brand" title="No administrator access">
            Cleanup works offline and reports locked or protected residuals accurately.
          </Banner>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="danger"
              onClick={() => {
                setDialog(null);
                setToast("640 MB of PC Steward-downloaded files removed. Provider caches were preserved.");
              }}
            >
              Delete downloaded files
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "factory-one" ? (
        <Dialog
          title="Review factory reset scope"
          description="PC Steward must be idle. The app will close so the next launch can verify every reset category before onboarding."
          onClose={() => setDialog(null)}
          size="large"
        >
          <DeletePreserve
            deletes={[
              "Database, inventory and scan history",
              "Source metadata, logs and update history",
              "Downloaded files",
              "Mappings, pins, ignores and settings",
              "Consent records and current-user schedule",
            ]}
            preserves={["PC Steward application binaries", "Every installed third-party application", "All provider-owned caches"]}
          />
          <Banner tone="danger" title="This cannot be undone">
            A second, separate confirmation is required. No reset has been scheduled yet.
          </Banner>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="danger"
              onClick={() => {
                setResetPhrase("");
                setDialog("factory-two");
              }}
            >
              Continue to final confirmation
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "factory-two" ? (
        <Dialog
          title="This cannot be undone"
          description="Type RESET to confirm. Pressing Enter in the field will not activate the reset action."
          onClose={() => setDialog(null)}
        >
          <label className="phrase-field">
            <span>Confirmation phrase</span>
            <input
              value={resetPhrase}
              onChange={(event) => setResetPhrase(event.target.value)}
              placeholder="Type RESET"
              autoComplete="off"
            />
          </label>
          <p className="supporting-note">
            PC Steward remains installed. Installed applications and provider-owned caches remain
            unchanged.
          </p>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setDialog(null)}>
              Cancel
            </Button>
            <Button
              kind="danger"
              disabled={resetPhrase !== "RESET"}
              onClick={() => setDialog("reset-done")}
            >
              Factory reset PC Steward
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "reset-done" ? (
        <Dialog
          title="Reset will complete on the next launch"
          description="This prototype does not close the browser or delete data. Production must verify the reset manifest before showing onboarding."
          onClose={() => {
            setDialog(null);
            setScenario("first-launch");
          }}
        >
          <div className="completion-visual">
            <span>
              <Icon name="check" size={30} />
            </span>
            <h3>Reset marker prepared</h3>
            <p>Old application state must not be reopened behind this screen.</p>
          </div>
          <div className="dialog-actions">
            <Button
              kind="primary"
              onClick={() => {
                setDialog(null);
                setScenario("first-launch");
              }}
            >
              Preview next launch
            </Button>
          </div>
        </Dialog>
      ) : null}

      {dialog === "action-done" ? (
        <Dialog
          title="Actions completed"
          description="This is a visual prototype outcome. No system action was performed."
          onClose={() => setDialog(null)}
        >
          <div className="completion-visual">
            <span>
              <Icon name="check" size={30} />
            </span>
            <h3>
              {completionMode === "update"
                ? `1 verified update · ${selectedUpdate.name}`
                : "1 verified update · 640 MB recovered"}
            </h3>
            <p>Each result remains independently recorded in Activity.</p>
          </div>
          <div className="outcome-list">
            <div>
              <StatusChip tone="success">Verified success</StatusChip>
              <span>
                {completionMode === "update"
                  ? `${selectedUpdate.name} ${selectedUpdate.targetVersion} detected after the simulated provider step.`
                  : "Contoso Editor 4.2.0 detected after the simulated provider step."}
              </span>
            </div>
            {completionMode === "queue" ? (
              <div>
                <StatusChip tone="success">Completed</StatusChip>
                <span>PC Steward-owned download files removed; provider caches preserved.</span>
              </div>
            ) : null}
          </div>
          <div className="dialog-actions">
            <Button kind="subtle" onClick={() => setRoute("activity")}>
              View activity
            </Button>
            <Button kind="primary" onClick={() => setDialog(null)}>
              Done
            </Button>
          </div>
        </Dialog>
      ) : null}

      {toast ? (
        <div className="toast" role="status" aria-live="polite">
          <span>
            <Icon name="check" />
          </span>
          <p>{toast}</p>
          <IconButton label="Dismiss notification" icon="close" onClick={() => setToast(null)} />
        </div>
      ) : null}
    </div>
  );
}

function FindingsPreview({ onOpen }: { onOpen: (finding: Finding) => void }) {
  return (
    <section className="section">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Attention</p>
          <h2>What is worth reviewing</h2>
        </div>
        <span className="section-count">4 items</span>
      </div>
      <div className="preview-list">
        {findings.slice(0, 4).map((finding) => (
          <button type="button" className="preview-row" key={finding.id} onClick={() => onOpen(finding)}>
            <span className={`finding-icon finding-${finding.tone}`}>
              <Icon name={finding.tone === "warning" ? "attention" : finding.tone === "success" ? "storage" : "updates"} />
            </span>
            <span className="preview-copy">
              <strong>{finding.title}</strong>
              <span>{finding.summary}</span>
            </span>
            <StatusChip tone={finding.tone}>{finding.confidence}</StatusChip>
            <Icon name="arrow" />
          </button>
        ))}
      </div>
    </section>
  );
}

function FindingRow({
  finding,
  selected,
  onToggle,
  onOpen,
}: {
  finding: Finding;
  selected: boolean;
  onToggle?: () => void;
  onOpen: () => void;
}) {
  return (
    <article className={`finding-row ${selected ? "selected" : ""}`}>
      {finding.actionable ? (
        <label className="finding-select">
          <input
            type="checkbox"
            checked={selected}
            onChange={onToggle}
            aria-label={`Select ${finding.title}`}
          />
        </label>
      ) : (
        <span className="finding-select-spacer" />
      )}
      <span className={`finding-icon finding-${finding.tone}`}>
        <Icon name={finding.tone === "warning" ? "attention" : finding.tone === "success" ? "storage" : "info"} />
      </span>
      <button type="button" className="finding-copy" onClick={onOpen}>
        <strong>{finding.title}</strong>
        <span>{finding.summary}</span>
        <small>{finding.source}</small>
      </button>
      <div className="finding-badges">
        <StatusChip tone={finding.tone}>{finding.severity}</StatusChip>
        <StatusChip tone={finding.tone}>{finding.confidence}</StatusChip>
      </div>
      <IconButton label={`Open ${finding.title} details`} icon="arrow" onClick={onOpen} />
    </article>
  );
}

function SettingRow({
  title,
  description,
  checked,
  disabled,
  onChange,
}: {
  title: string;
  description: string;
  checked: boolean;
  disabled?: boolean;
  onChange?: (value: boolean) => void;
}) {
  return (
    <label className={`setting-row ${disabled ? "disabled" : ""}`}>
      <span>
        <strong>{title}</strong>
        <small>{description}</small>
      </span>
      <input
        className="switch"
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange?.(event.target.checked)}
        aria-label={`${checked ? "Disable" : "Enable"} ${title}`}
      />
    </label>
  );
}

function DeletePreserve({
  deletes,
  preserves,
}: {
  deletes: string[];
  preserves: string[];
}) {
  return (
    <div className="delete-preserve">
      <section>
        <h3>
          <Icon name="attention" /> Deletes
        </h3>
        <ul>
          {deletes.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </section>
      <section>
        <h3>
          <Icon name="shield" /> Preserves
        </h3>
        <ul>
          {preserves.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </section>
    </div>
  );
}
