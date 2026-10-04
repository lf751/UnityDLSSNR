using System;
using UnityEngine;
using UnityEngine.Rendering;

public static class DlssNrQualityVerification
{
    public static string Main()
    {
        const int width = 128, height = 4;
        const int sourceWidth = 192, processedWidth = 90;
        var original = new Texture2DArray(sourceWidth, height, 1, TextureFormat.RGBAFloat, false, true);
        var processed = new Texture2D(processedWidth, height, TextureFormat.RGBAFloat, false, true);
        var readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var material = new Material(Resources.Load<Shader>("DlssNrHdrp"));
        var cmd = new CommandBuffer();
        var previous = RenderTexture.active;
        float maxTexelError = 0;
        try
        {
            var source = new Color[sourceWidth * height];
            var output = new Color[processedWidth * height];
            for (int y = 0; y < height; ++y)
            for (int x = 0; x < sourceWidth; ++x)
            {
                int i = y * sourceWidth + x;
                float ramp = x / (float)(width - 1);
                source[i] = new Color(ramp, ramp, ramp, 0.75f);
            }
            for (int y = 0; y < height; ++y)
            for (int x = 0; x < processedWidth; ++x)
            {
                int i = y * processedWidth + x;
                output[i] = new Color(x % 2, x % 2, x % 2, 1);
            }
            original.SetPixels(source, 0); original.Apply();
            processed.SetPixels(output); processed.Apply();
            target.Create();
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_DlssNrSource", original);
            properties.SetTexture("_DlssNrOutput", processed);
            properties.SetVector("_DlssNrColorScale", new Vector4(width / (float)sourceWidth, 1, 1, 1));
            properties.SetVector("_DlssNrOutputSize", new Vector4(processedWidth, height, 1f / processedWidth, 1f / height));
            // Verify direct NR output across the entire mismatched viewport.
            {

                cmd.Clear();
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, properties);
                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                var actual = readback.GetPixels();
                for (int i = 0; i < actual.Length; ++i)
                {
                    int x = i % width;
                    int nrX = Mathf.Min((int)((x + 0.5f) / width * processedWidth), processedWidth - 1);
                    float error = Mathf.Abs(actual[i].r - nrX % 2);

                    maxTexelError = Mathf.Max(maxTexelError, error);

                    if (Mathf.Abs(actual[i].a - 0.75f) > 0.001f) throw new Exception($"Source alpha was not preserved: actual={actual[i]}, pixel={i}.");
                }
            }
            if (maxTexelError > 0.0001f)
                throw new Exception($"Copy verification failed: texel={maxTexelError}");
            return $"PASS: oversized source (192), smaller NR (90), destination viewport (128): direct NR output with no inset; error={maxTexelError}";
        }
        finally
        {
            RenderTexture.active = previous;
            cmd.Dispose(); target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(original);
            UnityEngine.Object.DestroyImmediate(processed);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }
}
