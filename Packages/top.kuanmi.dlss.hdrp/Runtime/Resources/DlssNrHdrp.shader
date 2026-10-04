Shader "Hidden/UnityRHI/DLSS-NR/HDRP"
{
    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Builtin/BuiltinData.hlsl"

    TEXTURE2D_X(_DlssNrSource);
    TEXTURE2D(_DlssNrOutput);
    float4 _DlssNrColorScale;
    float4 _DlssNrMotionScale;
    int _DlssNrDebugMode;

    struct Attributes { uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }
    float4 ReadColor(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X_LOD(_DlssNrSource, s_linear_clamp_sampler, uv * _DlssNrColorScale.xy, 0);
    }
    uint2 InputPixel(float2 uv) { return min(uint2(uv * _ScreenSize.xy), uint2(_ScreenSize.xy) - 1); }
    float2 ReadMotion(uint2 pixel)
    {
        float2 motion;
        DecodeMotionVector(LOAD_TEXTURE2D_X(_CameraMotionVectorsTexture, pixel), motion);
        return motion;
    }
    struct Outputs
    {
        float4 color : SV_Target0;
        float2 motion : SV_Target1;
        float depth : SV_Target2;
        float4 fallback : SV_Target3;
    };
    Outputs Prepare(Varyings input)
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        Outputs output;
        uint2 pixel = InputPixel(input.uv);
        output.color = ReadColor(input.uv);
        output.motion = ReadMotion(pixel);
        output.depth = LoadCameraDepth(pixel);
        output.fallback = output.color;
        return output;
    }
    float4 Copy(Varyings input) : SV_Target
    {
        return SAMPLE_TEXTURE2D_LOD(_DlssNrOutput, s_linear_clamp_sampler, input.uv, 0);
    }
    float4 DebugInputs(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        if (_DlssNrDebugMode == 5)
            return ReadColor(input.uv);
        uint2 pixel = InputPixel(input.uv);
        if (_DlssNrDebugMode == 1 || _DlssNrDebugMode == 2)
        {
            float2 motion = ReadMotion(pixel) * _DlssNrMotionScale.xy;
            float range = max(_DlssNrMotionScale.z, 0.001);
            if (_DlssNrDebugMode == 1)
                return float4(saturate(0.5 + motion / (2 * range)), saturate(length(motion) / range), 1);
            return float4(saturate(length(motion) / range).xxx, 1);
        }
        float depth = LoadCameraDepth(pixel);
        if (_DlssNrDebugMode == 4)
            depth = saturate(LinearEyeDepth(depth, _ZBufferParams) / max(_DlssNrMotionScale.w, 0.001));
        return float4(depth.xxx, 1);
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Prepare Inputs"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Prepare
            ENDHLSL
        }
        Pass
        {
            Name "Copy NR Output"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Copy
            ENDHLSL
        }
        Pass
        {
            Name "Debug Inputs"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DebugInputs
            ENDHLSL
        }
    }
    Fallback Off
}
