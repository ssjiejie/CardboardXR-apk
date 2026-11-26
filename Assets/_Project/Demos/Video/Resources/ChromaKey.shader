// The surface a 360 or 180 video is drawn on, with an optional green-screen key: the green in the
// picture is cut out, so whatever is behind the sphere shows through where it was, such as the
// passthrough camera image. Key 0 draws every pixel, as an ordinary unlit video surface does.
Shader "CardboardXR/VideoChromaKey"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Tooltip("How much of the green is cut out: 0 keeps it all, 1 cuts the most.")]
        _ChromaKey ("Green Screen", Range(0, 1)) = 0
        [Tooltip("How soft the cut edge is: a larger value leaves a wider fringe.")]
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
            #pragma vertex vert
            #pragma fragment frag

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
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_ChromaKey > 0.001)
                {
                    // How far the green channel leads both others: high on a green screen, near zero
                    // on skin and clothes. The cut level falls as the knob rises, so more is removed.
                    half green = color.g - max(color.r, color.b);
                    half cut = lerp(0.65, 0.06, saturate(_ChromaKey));
                    half keep = 1.0 - smoothstep(cut, cut + max(_ChromaSoftness, 0.01), green);
                    clip(keep - 0.02);
                }
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
