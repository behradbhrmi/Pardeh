# Logo master

`pardeh-logo-curtain.png` (1024 px) is the master of the Pardeh logo: two blue ribbon curtains around a white play triangle.
It is embedded by `Pardeh.UI.csproj` and drawn by `FullMode/LogoImage.cs`. The art is AI-generated.

Everything else (Android icon layers, status icons, `src/Pardeh.App/pardeh.ico`, the Windows 11 widget pictures,
`docs/img/pardeh-logo-512.png`) is generated from this master by `scripts/export-logo-image.ps1`. Do not edit the generated
files by hand (the two Android 7 fallback icons in `mipmap-anydpi` are hand-written and only point at the generated picture).

The export (`tests/Pardeh.Skinning.Tests/LogoExportImage.cs`) also does two things of its own:

- **Android launcher foreground:** made from the full-bleed art (`tools/logo-master/source-curtain-fullbleed-1024.png`),
  60 dp wide with its edge faded into the navy background layer, so that round icon masks do not cut the ribbons.
- **One-colour icons** (status bar, themed launcher icon): cut from the master by brightness (the ribbons and the white
  triangle are bright, the tile is not).

The code-drawn designs in `FullMode/PardehLogoArt*.cs` are vector alternatives.
