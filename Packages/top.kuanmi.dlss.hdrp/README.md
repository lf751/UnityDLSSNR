# UnityRHI DLSS Neural Rendering for HDRP

Unity 6.7 / HDRP 17.7 adapter for the UnityRHI native NR API. The effect runs **After Post
Process**, consuming tone-mapped SDR color plus HDRP depth and motion vectors.
HDRP motion is decoded and converted from previous-to-current NDC to
current-to-previous pixels with `(-width / 2, -height / 2)`.

## Installation

Requires Unity 6.7, HDRP 17.7, Windows x64, Direct3D 12, and a supported NVIDIA
GPU/driver. Tested on Unity 6000.7.0b2.

1. Clone or download this repository's **HDRP** branch.
2. Download the [prebuilt native package v1.0.3](https://github.com/Kuan-Mi/UnityDLSSNR/releases/tag/v1.0.3)
   and extract it into the project's `Packages` folder. The final path must be
   `Packages/top.kuanmi.unityrhi.native/package.json`.
3. Put your separately obtained NVIDIA-signed `nvngx_dlssnr.dll` into
   `Packages/top.kuanmi.unityrhi.native/Plugins/x86_64/`.
4. Copy `Packages/top.kuanmi.unityrhi` and `Packages/top.kuanmi.dlss.hdrp` from
   this repository into your project's `Packages` folder. Preserve `.meta` files.
   Alternatively, use local UPM paths to both packages in the checkout.
5. Select Direct3D 12 as the Windows graphics API and restart Unity after
   installing/replacing the preloaded native plugins. The native package must
   be embedded so its initialization can run before device creation.

Do not install the URP integration into an HDRP-only project. This adapter
does not require modifying the HDRP package or installing URP. Its shader asset
menu compatibility fix retains the older editor API for earlier Unity versions.

## Optional generated Core command buffers

Unity 6.7's **Edit > Rendering > Generate Core CommandBuffers** can emit invalid
generic types, omit `ref` arguments, omit optional multi-draw arguments, and
leave the nested `BuildSettings` type unqualified. These generator corrections
are not required by NR: the adapter uses HDRP's native `CommandBuffer` and can
run with the stock Core package.

If you want to apply generated buffers, the repository helper embeds the current
Core 17.7.0 package and corrects the seven generated files. It retains the
existing `.meta` files and leaves the project-root originals untouched.

Run from this repository with Python 3:

```powershell
# Preview only; no files are written.
python Tools/fix_hdrp_command_buffers.py "D:/Path/To/YourProject"

# Embed Core if necessary and apply the corrected generated files.
python Tools/fix_hdrp_command_buffers.py "D:/Path/To/YourProject" --apply

# Check the embedded files against the corrected output.
python Tools/fix_hdrp_command_buffers.py "D:/Path/To/YourProject" --check
```

After applying, resolve packages/refresh in Unity and wait for compilation.
The helper refuses Core versions other than 17.7.0. Review the preview before
applying to a Core package containing your own command-buffer modifications.

## Enable and adjust

1. Use **Tools > UnityRHI > HDRP > Register Neural Rendering** to register the
   effect in HDRP's current managed After Post Process order settings.
2. Use **Enable Neural Rendering in Scene Global Volume** to enable the effect
   on the lowest-priority active global Volume profile in the open scene.
   This modifies and saves the shared profile; scenes sharing it inherit the setting.
   It also switches the active HDRP asset to 16-bit post-process buffers.
   For an existing setup, use **Use 16-bit Post Process Buffers** separately.
   This increases post-process buffer memory/bandwidth compared with packed
   R11G11B10. Repeat for each HDRP quality asset you use.
3. Adjust **Post-processing > DLSS Neural Rendering** in that profile.
   Override **Enabled** to toggle it, **Intensity** to adjust it, or
   **Iteration Count** for repeated NR stages. Start with one iteration.
   **Output Blend** mixes NR with the original tone-mapped image (default 0.65).
   Reduce it to retain more original detail; set it to 1 for pure NR output or
   0 for the original image. It does not change native NR intensity or GPU cost.
4. Use **Neural Rendering Diagnostics** to inspect native availability, NGX
   results, replay/drop counts, and device health. NGX success is `0x00000001`.

You can also add the override manually to any global or local Volume profile
affecting your Game camera. HDRP camera Post Process, Custom Post Process, and
Motion Vectors must be enabled. The shader is in Resources so it is retained
in player builds. No game-specific scenes or profiles are shipped in this package.

## Behavior and limits

The final NR copy uses exact texels at the destination resolution and preserves
source alpha. The **Source Color** input debug mode bypasses native evaluation
and displays the original tone-mapped input for comparison. Enable HDRP camera
dithering to reduce final 8-bit display quantization. Higher precision and output
blending can reduce integration-related banding and softness; they do not guarantee
that the experimental NR model preserves every shadow gradient or fine detail.

Per-camera contexts resize when the post-process resolution changes, reset
history after camera cuts, projection/settings changes or skipped frames, and
release their native resources on cleanup/domain reload. Preparation initializes
the output with the original image before native evaluation. Native wrappers
restore RenderTarget states so HDRP can transition the output for sampling.

This first adapter supports SDR, non-XR Game cameras. Scene/preview/reflection
cameras, XR, HDR display output, and unavailable native runtimes are bypassed.
Linear HDR lighting inside HDRP is supported; the restriction is HDR *display*
output. Input debug modes work without an available NR native runtime.
Super Resolution and Frame Generation are not implemented by this adapter.
The project's existing HDRP upscaler remains independent.

## Verification — 2026-10-04

Validated in SCPCB-HDRP with Unity 6000.7.0b2 / HDRP 17.7 / RTX 4060 Ti / D3D12:

- Unity compilation succeeded with zero compilation errors; shader messages empty.
- Native NR initialized and feature creation/evaluation returned `1` (success).
- Main Menu and SampleScene Play Mode frame advancement confirmed with `wait_for`.
- Gameplay reached 9,531 replayed command streams, zero dropped streams, and
  device-removed reason `0`, including a two-iteration test.
- Disabling the runtime Volume override stopped native submissions while frames
  continued. Enabling it resumed successful evaluations.
- Captured and inspected gameplay with NR, without NR, with two iterations, and
  with linear-eye-depth visualization. Restored one iteration and debug Off.
- Standalone player builds, HDR display output, XR, and moving-camera temporal
  quality have not been validated.

Quality update: GPU readback checked alternating pixel edges, original-image
identity at zero output blend, and the 0.65 blend. Maximum measured RGB error
was `5.96e-8`. Compilation and shader diagnostics passed, and native evaluation
remained successful. The reported dark-scene screenshot has not been reproduced
as a controlled before/after capture for this update.
