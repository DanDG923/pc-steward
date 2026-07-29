# PC Steward native Windows design prototype

This is the authoritative interactive prototype for the PC Steward handoff. It is
a separate, unpackaged **WinUI 3 / Windows App SDK desktop application**. It does
not reference production projects, inspect Windows inventory, access the network,
download files, or change the computer.

The browser prototype in `../prototype` is retained only as a superseded visual
reference and must not be used as an implementation base.

## Run

From the repository root:

```powershell
dotnet run --project design/native-prototype/PCSteward.NativePrototype.csproj
```

Use the navigation pane and the **Prototype state** selector to review onboarding,
scanning, no-findings, partial-provider and offline states. Destructive controls
are simulated and clearly say that no system action was performed.

## Validate

```powershell
dotnet build design/native-prototype/PCSteward.NativePrototype.csproj --no-restore
```

This prototype is a design artifact. Production implementation belongs in the
existing `src/PCSteward.Desktop` WinUI project and must follow the phased handoff.
