# Authon VB.NET SDK

<p align="center">
  <img src="https://authon.pro/logo.png" alt="Authon" width="80" />
  <br/>
  <strong>Official VB.NET SDK for Authon — Software Licensing & Authentication Platform</strong>
</p>

<p align="center">
  <a href="https://authon.pro">Website</a> •
  <a href="https://authon.pro/docs">Docs</a> •
  <a href="https://discord.gg/jMZCTKPsmE">Discord</a> •
  <a href="https://authon.pro/status">Status</a>
</p>

---

## Requirements

- .NET 6+ or .NET Framework 4.6.1+
- No external NuGet packages

## Installation

Copy `Authon.vb` into your project.

## Quick Start

```vb
Imports AuthonSDK

Dim auth As New Authon("your-app-id", "your-api-key")
Await auth.InitAsync()

If Await auth.LoginAsync("username", "password") Then
    Console.WriteLine($"Level: {auth.Level}")
End If

Await auth.LogoutAsync()
```

## Links

- 🌐 Website: https://authon.pro
- 📖 Docs: https://authon.pro/docs
- 💬 Discord: https://discord.gg/jMZCTKPsmE
- 📊 Status: https://authon.pro/status
- 🔗 API Health: https://api.authon.pro/health

## License

MIT
