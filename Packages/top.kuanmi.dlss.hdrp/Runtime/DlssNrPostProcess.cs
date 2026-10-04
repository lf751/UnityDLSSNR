using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityRhi.Dlss.Hdrp
{
    public sealed partial class DlssNrVolume : IDisposable
    {
        private Material _prepareMaterial;
        private readonly Dictionary<Camera, DlssNrCameraContext> _contexts = new();
        private readonly List<Camera> _deadCameras = new();
        private readonly RenderTargetIdentifier[] _targets = new RenderTargetIdentifier[4];
        private bool _warnedUnavailable;
        private bool _warnedUnsupported;
        private bool _warnedFailure;

        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;
        public override bool visibleInSceneView => false;

        public override void Setup()
        {
            var shader = Resources.Load<UnityEngine.Shader>("DlssNrHdrp");
            if (shader != null && shader.isSupported)
                _prepareMaterial = CoreUtils.CreateEngineMaterial(shader);
            RhiDomainReload.RegisterOwner(this);
        }

        public override void Render(CommandBuffer cmd, HDCamera hdCamera, RTHandle source, RTHandle destination)
        {
            Camera camera = hdCamera.camera;
            // The native NR API accepts SDR 2D textures. Never feed it an XR array
            // or the HDR display encoding produced by HDRP's HDR output path.
            if (camera.cameraType != CameraType.Game || camera.stereoEnabled ||
                (HDROutputSettings.main != null && HDROutputSettings.main.active))
            {
                if (!_warnedUnsupported)
                {
                    _warnedUnsupported = true;
                    Debug.LogWarning("[UnityRHI.DLSS-NR.HDRP] This adapter supports SDR, non-XR Game cameras; bypassing this camera.");
                }
                HDUtils.BlitCameraTexture(cmd, source, destination);
                return;
            }

            var settings = new DlssNrSettings(this);
            bool debug = settings.DebugMode != DlssNrDebugMode.Off;
            try
            {
                if (!debug && (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 ||
                    !RhiCore.IsD3D12Active || !RhiCore.IsDlssNrAvailable))
                {
                    if (!_warnedUnavailable)
                    {
                        _warnedUnavailable = true;
                        Debug.LogWarning($"[UnityRHI.DLSS-NR.HDRP] Native runtime unavailable (NR init=0x{unchecked((uint)RhiCore.DlssNrInitResult):X8}). Restart Unity after installing the preloaded native package.");
                    }
                    HDUtils.BlitCameraTexture(cmd, source, destination);
                    return;
                }

                int width = Mathf.Max(1, (int)hdCamera.postProcessScreenSize.x);
                int height = Mathf.Max(1, (int)hdCamera.postProcessScreenSize.y);
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_DlssNrSource", source);
                properties.SetVector("_DlssNrColorScale", source.rtHandleProperties.rtHandleScale);
                properties.SetInt("_DlssNrDebugMode", (int)settings.DebugMode);
                properties.SetFloat("_DlssNrOutputBlend", outputBlend.value);
                properties.SetVector("_DlssNrMotionScale", new Vector4(-0.5f * width * settings.MotionVectorScale.x,
                    -0.5f * height * settings.MotionVectorScale.y, settings.DebugMotionRange, settings.DebugDepthRange));

                // Input visualization also works before the native runtime is available.
                if (debug)
                {
                    HDUtils.DrawFullScreen(cmd, _prepareMaterial, destination, properties, 2);
                    return;
                }

                PruneDeadCameras();
                Camera key = camera;
                if (_contexts.TryGetValue(key, out var context) &&
                    (context.Width != width || context.Height != height || context.IterationCount != settings.IterationCount))
                {
                    RhiCore.WaitForGpuIdle();
                    context.Dispose();
                    _contexts.Remove(key);
                    context = null;
                }
                if (context == null)
                {
                    context = new DlssNrCameraContext(width, height, camera.name, settings.IterationCount);
                    _contexts.Add(key, context);
                }

                _targets[0] = context.ColorRt;
                _targets[1] = context.MotionRt;
                _targets[2] = context.DepthRt;
                _targets[3] = context.OutputRt;
                // Initialize the output with the original image, so a native evaluation
                // failure retains a valid frame. All four native wrappers start/end in RT state.
                cmd.SetRenderTarget(_targets, BuiltinRenderTextureType.None);
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.DrawProcedural(Matrix4x4.identity, _prepareMaterial, 0, MeshTopology.Triangles, 3, 1, properties);
                var dispatch = context.BeginFrame(Time.frameCount, camera.transform.position,
                    camera.transform.rotation, camera.nonJitteredProjectionMatrix, settings);
                context.Record(cmd, dispatch);
                properties.SetTexture("_DlssNrOutput", context.OutputRt);
                HDUtils.DrawFullScreen(cmd, _prepareMaterial, destination, properties, 1);
            }
            catch (Exception exception)
            {
                if (!_warnedFailure)
                {
                    _warnedFailure = true;
                    Debug.LogWarning($"[UnityRHI.DLSS-NR.HDRP] Bypassing after setup/dispatch failure: {exception}");
                }
                HDUtils.BlitCameraTexture(cmd, source, destination);
            }
        }

        private void PruneDeadCameras()
        {
            _deadCameras.Clear();
            foreach (var pair in _contexts)
                if (pair.Key == null) _deadCameras.Add(pair.Key);
            if (_deadCameras.Count > 0) RhiCore.WaitForGpuIdle();
            foreach (Camera key in _deadCameras)
            {
                _contexts[key].Dispose();
                _contexts.Remove(key);
            }
        }

        public void ResetHistory()
        {
            foreach (var context in _contexts.Values) context.ResetHistory();
        }

        public override void Cleanup() => Dispose();

        public void Dispose()
        {
            if (_contexts.Count > 0) RhiCore.WaitForGpuIdle();
            foreach (var context in _contexts.Values) context.Dispose();
            _contexts.Clear();
            CoreUtils.Destroy(_prepareMaterial);
            _prepareMaterial = null;
            RhiDomainReload.UnregisterOwner(this);
        }
    }
}
