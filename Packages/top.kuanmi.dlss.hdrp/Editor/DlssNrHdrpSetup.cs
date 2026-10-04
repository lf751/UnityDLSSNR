using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityRhi.Dlss.Hdrp.Editor
{
    public static class DlssNrHdrpSetup
    {
        [MenuItem("Tools/UnityRHI/HDRP/Register Neural Rendering")]
        public static void Register()
        {
            var global = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
            if (global == null) throw new InvalidOperationException("HDRP Global Settings asset is missing.");
            var serialized = new SerializedObject(global);
            var iterator = serialized.GetIterator();
            SerializedProperty list = null;
            while (iterator.Next(true))
            {
                // HDRP 17.7 also keeps a legacy copy at the root of the asset.
                // Register the current managed graphics setting, not that obsolete copy.
                if (iterator.name == "m_AfterPostProcessCustomPostProcesses" &&
                    !iterator.propertyPath.StartsWith("m_CustomPostProcessOrdersSettings."))
                {
                    list = iterator.FindPropertyRelative("m_CustomPostProcessTypesAsString");
                }
            }
            if (list == null) throw new InvalidOperationException("HDRP After Post Process order could not be found.");
            string type = typeof(DlssNrVolume).AssemblyQualifiedName;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).stringValue == type) return;
            Undo.RecordObject(global, "Register HDRP Neural Rendering");
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).stringValue = type;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(global);
            AssetDatabase.SaveAssetIfDirty(global);
            Debug.Log("[UnityRHI.DLSS-NR.HDRP] Registered After Post Process order.");
        }

        [MenuItem("Tools/UnityRHI/HDRP/Enable Neural Rendering in Scene Global Volume")]
        public static void EnableSceneVolumes()
        {
            Register();
            UseHighPrecisionPostProcessing();
            foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>()
                .Where(v => v.isGlobal && v.weight > 0 && v.sharedProfile != null)
                .OrderBy(v => v.priority).Take(1))
            {
                var profile = volume.sharedProfile;
                if (profile == null) continue;
                Undo.RecordObject(profile, "Enable HDRP Neural Rendering");
                if (!profile.TryGet<DlssNrVolume>(out var nr))
                {
                    nr = profile.Add<DlssNrVolume>();
                    nr.name = nameof(DlssNrVolume);
                    AssetDatabase.AddObjectToAsset(nr, profile);
                    Undo.RegisterCreatedObjectUndo(nr, "Add HDRP Neural Rendering");
                }
                Undo.RecordObject(nr, "Enable HDRP Neural Rendering");
                nr.enabled.Override(true);
                EditorUtility.SetDirty(nr);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
        }

        [MenuItem("Tools/UnityRHI/HDRP/Use 16-bit Post Process Buffers")]
        public static void UseHighPrecisionPostProcessing()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("No active HDRP asset.");
            var serialized = new SerializedObject(pipeline);
            var format = serialized.FindProperty("m_RenderPipelineSettings.postProcessSettings.bufferFormat");
            if (format == null) throw new InvalidOperationException("HDRP post-process buffer format not found.");
            int highPrecision = (int)UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            if (format.intValue == highPrecision)
            {
                AssetDatabase.SaveAssetIfDirty(pipeline);
                return;
            }
            Undo.RecordObject(pipeline, "Use 16-bit HDRP Post Process Buffers");
            format.intValue = highPrecision;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            Debug.Log("[UnityRHI.DLSS-NR.HDRP] Active HDRP asset now uses 16-bit post-process buffers to reduce shadow quantization.");
        }

        [MenuItem("Tools/UnityRHI/HDRP/Neural Rendering Diagnostics")]
        public static void Diagnostics()
        {
            Debug.Log($"[UnityRHI.DLSS-NR.HDRP] API={SystemInfo.graphicsDeviceType} GPU={SystemInfo.graphicsDeviceName} " +
                $"D3D12={RhiCore.IsD3D12Active} NR={RhiCore.IsDlssNrAvailable} " +
                $"init=0x{unchecked((uint)RhiCore.DlssNrInitResult):X8} " +
                $"create=0x{unchecked((uint)RhiCore.DlssNrLastCreateResult):X8} " +
                $"evaluate=0x{unchecked((uint)RhiCore.DlssNrLastEvaluateResult):X8} " +
                $"streams={RhiCore.CommandStreamEventCount} dropped={RhiCore.DroppedCommandStreamCount} " +
                $"deviceRemoved=0x{unchecked((uint)RhiCore.DeviceRemovedReason):X8}");
        }
    }
}
