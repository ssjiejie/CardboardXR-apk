// The surface a 360 or 180 video is drawn on, with an optional green-screen key: the green in the
// picture is cut out, so whatever is behind the sphere shows through where it was, such as the
// camera image. Key 0 draws every pixel, as an ordinary unlit video surface does.
// The same shader draws the camera image behind that sphere: _Rotation turns the frame the way the
// phone's camera reports it, and _MirrorX flips it for a front camera.
// No [Tooltip] attributes: the ShaderLab parser failed on them here (parse error at the first one),
// which left the shader without a name and drew the sphere magenta instead of the video.
Shader "CardboardXR/VideoChromaKey"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ChromaKey ("Green Screen", Range(0, 1)) = 0
        _Tolerance ("Green Tolerance", Range(0, 1)) = 0.6
        _ChromaSoftness ("Green Screen Edge", Range(0.01, 0.5)) = 0.06
        _Rotation ("Rotation", Range(0, 3)) = 0
        _MirrorX ("Mirror", Range(0, 1)) = 0
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _ChromaKey;
                float _Tolerance;
                float _ChromaSoftness;
                float _Rotation;
                float _MirrorX;
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

            // How much this colour reads as green, 0 to 1. Measured against the green itself rather
            // than as a plain difference, so a shadowed green screen (0.12, 0.20, 0.12) counts as
            // green just like a lit one (0.1, 0.8, 0.1). A difference alone left the dim parts of a
            // screen behind, which is most of what "the key misses some green" was.
            half Greenness(half3 color)
            {
                half green = color.g;
                half others = max(color.r, color.b);
                return saturate((green - others) / max(green, 0.06));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                // Quarter turns, then a flip: what a phone camera reports about its frame.
                float quarter = floor(_Rotation + 0.5);
                if (quarter > 0.5 && quarter < 1.5)
                    uv = float2(uv.y, 1.0 - uv.x);
                else if (quarter > 1.5 && quarter < 2.5)
                    uv = float2(1.0 - uv.x, 1.0 - uv.y);
                else if (quarter > 2.5 && quarter < 3.5)
                    uv = float2(1.0 - uv.y, uv.x);
                if (_MirrorX > 0.5)
                    uv.x = 1.0 - uv.x;

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                if (_ChromaKey > 0.001)
                {
                    // Tolerance sets the bar: at 0 only a saturated screen goes, at 1 even a faint
                    // green cast does. 0.6 clears a lit screen and leaves skin and khaki alone.
                    half cut = lerp(0.42, 0.04, saturate(_Tolerance));
                    half soft = max(_ChromaSoftness, 0.02);
                    half keep = 1.0 - smoothstep(cut, cut + soft, Greenness(color.rgb));
                    // Cut halfway through the edge: clipping at keep > 0.02, as this did, let every
                    // faintly green pixel draw in full, which is what left a green haze behind.
                    clip(keep - 0.5);

                    // Spill: the green light a screen throws on hair and shoulders. Pull the green
                    // back towards the other channels on what stays, more of it at higher tolerance.
                    half spill = Greenness(color.rgb) * saturate(0.5 + 0.5 * _Tolerance);
                    color.g = lerp(color.g, min(color.g, max(color.r, color.b) + 0.02), spill * 0.9);
                }
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
