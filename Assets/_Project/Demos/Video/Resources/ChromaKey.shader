// Cuts a green screen out of the video the sphere is drawn with, so passthrough shows through
// where the video was green: the subject then stands in the room instead of on a green wall.
//
// The key is the pixel's chromaticity (its RGB with brightness divided out), not a fixed colour, so
// it holds for a lit green wall and for one in shadow, and for the slightly different greens of
// different studios. _Threshold is how green a pixel must be, in chromaticity, before it is cut;
// _Softness feathers the edge over that much more, which hides the stair-stepping a hard cut leaves.
// _Spill pulls the green bounce off the subject's edges, so no halo is left against the room.
//
// The texture property is named _BaseMap, and marked [MainTexture], because that is what the sphere
// is textured through: VideoSphere sets Material.mainTexture / mainTextureScale / mainTextureOffset,
// and the material it copies is URP/Unlit, whose main texture is _BaseMap. Swapping a material's
// shader keeps the values of properties both shaders declare, so naming this one the same is what
// lets the video keep its frame and its per-eye scale and offset across the swap. A shader that
// declared _MainTex instead would come up with no texture at all.
//
// ZTest Always and Transparent: PassthroughController draws the camera image on a quad 2 m from the
// eyes with ZWrite on, while this sphere is 15 m out. With the ordinary depth test every fragment of
// the sphere would lose to that quad and the video would never appear. Drawing the sphere over
// everything, and blending only where it is opaque, keeps both: the video's subject, and the room
// behind the holes. The controls panel is a nearer Canvas in the same Transparent queue, drawn after
// it, so it still sits on top of the video.
Shader "CardboardXR/ChromaKey"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video", 2D) = "white" {}
        _Threshold ("Key threshold", Range(0.02, 0.6)) = 0.12
        _Softness ("Edge softness", Range(0.005, 0.4)) = 0.08
        _Spill ("Green spill removal", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ChromaKey"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Threshold;
                float _Softness;
                float _Spill;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 video = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // Chromaticity: the colour with brightness divided out, so a green wall reads the
                // same lit or in shadow.
                float3 rgb = max((float3)video.rgb, 1e-4);
                float3 chroma = rgb / (rgb.r + rgb.g + rgb.b);
                float dominance = chroma.g - max(chroma.r, chroma.b);

                float green = smoothstep(_Threshold, _Threshold + _Softness, dominance);
                half alpha = (half)(1.0 - green);

                // Green light bounced onto the subject tints its edges; pull that back where the
                // pixel was keyed, so no halo is left against the room.
                float spill = saturate(video.g - max(video.r, video.b));
                video.g -= spill * _Spill * green;

                return half4(video.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
