// The surface a 360 or 180 video is drawn on, with an optional green-screen key: the green in the
// picture is cut out, so whatever is behind the sphere shows through where it was, such as the
// passthrough camera image. Key 0 draws every pixel, as an ordinary unlit video surface does.
Shader "CardboardXR/VideoChromaKey"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Tooltip("How much of the green to cut out: 0 keeps it all, 1 cuts the most.")]
        _ChromaKey ("Green Screen", Range(0, 1)) = 0
        [Tooltip("How far a colour has to be past the cut to go fully: an edge of this width is what's left.")]
        _ChromaSoftness ("Green Screen Edge", Range(0.01, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Video"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _ChromaKey;
                float _ChromaSoftness;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                // Greenness: how far green stands above the stronger of red and blue, so greys and
                // whites, which have green level with them, never count.
                half green = color.g - max(color.r, color.b);
                // At 0 the cut sits above any colour, so nothing goes; at 1 it cuts nearly every green.
                half cut = lerp(1.05h, 0.02h, _ChromaKey);
                half edge = max(1e-4h, _ChromaSoftness * _ChromaKey);
                clip(cut + edge * 0.5h - green);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
