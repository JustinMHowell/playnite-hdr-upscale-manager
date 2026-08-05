# HDR & Upscale Manager

> Per-game HDR and upscaler (FSR/OptiScaler) setup for Playnite.

Configure ReShade, RenoDX/Luma HDR mods, DXVK, and OptiScaler (FSR/XeSS) per game from inside Playnite. AMD-focused today, with NVIDIA support planned. Inspired by and kept in sync with RankFTW/RHI.

## Building

1. Visual Studio 2022 with the ".NET desktop development" workload (or the standalone Build Tools).
2. Open `HdrUpscaleManager.csproj`, restore NuGet.
3. Build Release → `bin\Release\net462\`.
4. Copy `HdrUpscaleManager.dll`, dependency DLLs, `extension.yaml`, and `Resources\icon.png` into `%AppData%\Playnite\Extensions\hdr-upscale-manager\`.
5. Restart Playnite.

Or run [`build-and-run.ps1`](build-and-run.ps1) to do steps 2–5 (plus restarting Playnite) in one go.

Prebuilt `.pext` / `.zip` files are also published on the [Releases page](../../releases) once a version is tagged — see [RELEASING.md](RELEASING.md).

## Where to find it

- **Main menu** → **HDR & Upscale Manager** → **Say hello** (the starter entry point — replace with whatever this add-on actually does: a sidebar item, a game menu item, a library/metadata provider, etc.)
- **Add-ons → Extensions settings** for the configurable greeting.

## License

GPL-3.0
