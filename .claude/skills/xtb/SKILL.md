---
name: xtb
description: Build Events2Code and open it in a private local XrmToolBox instance
---

Open this tool in a local XrmToolBox for a hands-on look.

Run from the repo root, in pwsh — the XtbSandbox module is installed for PowerShell 7,
not Windows PowerShell:

```
.\tests\xtb.ps1
```

Append any arguments the user gave to `/xtb` straight onto that call.

What it does: builds the tool, assembles a private XrmToolBox instance under `tests\.xtb`
(its own Plugins, settings and connection list — the user's real XrmToolBox is untouched),
and starts XrmToolBox with the tool and its connection preselected. The script returns as
soon as XrmToolBox is launched; the window keeps running on its own. Give the call a
generous timeout (~3 minutes) for the build and setup, and relay the `Instance` /
`Tool` / `Connection` lines it prints.

Flags worth knowing: `-Environment <url>` aims at a different org than the active
`pac auth` profile; `-Reset` rebuilds the instance from scratch; `-NoLaunch` sets up
without starting.

If it throws that XtbSandbox is not installed, run
`Install-Module XtbSandbox -Scope CurrentUser` and retry. If XrmToolBox.exe cannot be
found, ask the user where it lives (or to set `XTB_PATH`) rather than guessing.
