# MSIX packaging

The existing Inno Setup scripts continue to produce the GitHub Release `.exe` installers. This directory produces a separate MSIX package for Microsoft Store submission.

The package declares four supported resource languages: Simplified Chinese (`zh-CN`), Traditional Chinese (`zh-TW`), English (`en-US`), and Japanese (`ja-JP`).

Run from the repository root on Windows with Node.js 22.12+, Corepack, .NET 10 SDK, and the Windows SDK (`makeappx.exe`). Build the shared renderer and native Webview before packaging:

```powershell
corepack pnpm install:editor-web
corepack pnpm build:editor-web
powershell -File apps/windows/msix/build.ps1
```

The script uses the MarkLeaf Store identity configured in `build.ps1`. Override it only for another Store listing or a local development identity:

```powershell
powershell -File apps/windows/msix/build.ps1 `
  -IdentityName 'YOUR_PARTNER_CENTER_IDENTITY' `
  -Publisher 'CN=YOUR_PARTNER_CENTER_PUBLISHER' `
  -PublisherDisplayName 'MarkLeaf'
```

The version comes from `apps/windows/MarkLeaf/MarkLeaf.csproj` unless `-Version` is supplied; the manifest uses four version components. The default output is `apps/windows/release/MarkLeaf-<version>-x64.msix` and `MarkLeaf-<version>-arm64.msix`. Select architectures with `-Architectures 'win-x64;win-arm64'` and change the destination with `-OutputDirectory`. The script produces self-contained packages; WebView2 Evergreen Runtime is still required on the target machine.

Store submission requires identity values matching the Partner Center listing. For local installation, use a development certificate matching the chosen publisher. Pass `-Certificate` and, when needed, `-CertificatePassword` to sign locally; otherwise the script leaves the package unsigned.
