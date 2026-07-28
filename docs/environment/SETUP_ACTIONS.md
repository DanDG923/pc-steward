# Setup actions

Only missing or unverified prerequisites from Task 00 are listed. Do not perform these automatically.

1. Provide a supported Windows 11 x64 development/test host or confirm that the current build-26200 environment is an approved equivalent. Official source: Microsoft Windows 11 specifications and requirements.
2. Install the approved Visual Studio or Build Tools workload, including the Windows App SDK/WinUI and Windows SDK components required by the accepted packaging ADR. Official source: Microsoft Visual Studio Installer and Windows App SDK documentation.
3. Recheck WinGet source health, especially `msstore`, using the official Windows Package Manager documentation and source-agreement procedures; do not reset sources as part of this task.
4. Record a supported clean Windows 11 test environment for later manual/UAC/package evidence. It may be physical, virtual, or Windows Sandbox. Official source: Microsoft Windows Sandbox documentation.
5. Provide approved signing/package tooling or an already available development certificate only when a later task explicitly permits it. Official sources: Microsoft SignTool and MakeAppx documentation.
