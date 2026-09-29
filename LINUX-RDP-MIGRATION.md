Pin Creator Linux RDP Migration

Current state

- The original WPF desktop project is archived and excluded from the solution.
- WPF does not run on Linux, including Linux RDP desktops.
- The batch rendering logic is usable as a base, but the UI layer must change for Linux.

Implemented migration path

1. `PinSharp.Core` contains the cross-platform batch and SkiaSharp rendering logic.
2. `PinSharp.Web` provides the ASP.NET Core dashboard for Linux RDP and VPS use.
3. `PinCreator.sln` now builds only the Linux-compatible application.

Linux run

```bash
dotnet build PinCreator.sln -c Release
dotnet run --project PinSharp.Web/PinSharp.Web.csproj -c Release --urls http://0.0.0.0:5099
```

The web and core projects require .NET 10.
