Shader "Custom/NoteMask"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct MaskRenderData
            {
                float2 pos;
                float angRad;
                float2 scale;
                uint spriteId;
                float4 color;
                float maskCutoff;
                uint sort;
            };

            StructuredBuffer<MaskRenderData> _NoteBuffer;
            StructuredBuffer<float4> _SpriteRects;
            float2 _AtlasSize;
            float _PixelsPerUnit;
            float4x4 _RootMatrix;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f   { 
                float4 pos : SV_POSITION; 
                float2 uv : TEXCOORD0; 
                float4 rect : TEXCOORD1;
                float4 color : TEXCOORD2;
                float maskCutoff : TEXCOORD3;
            };

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                MaskRenderData note = _NoteBuffer[id];
                float4 rect = _SpriteRects[note.spriteId];
                float2 worldSize = (rect.zw - rect.xy) * _AtlasSize / _PixelsPerUnit;
                float2 p = v.vertex.xy * note.scale * worldSize;
                float s = sin(note.angRad); float c = cos(note.angRad);
                float2 r = float2(p.x*c - p.y*s, p.x*s + p.y*c);
                r += note.pos;
                float3 world = mul(_RootMatrix, float4(r,0,1)).xyz;
                o.pos = TransformWorldToHClip(world);  
                o.uv = v.uv;
                o.rect = rect;
                o.color = note.color;
                o.maskCutoff = note.maskCutoff;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = lerp(i.rect.xy, i.rect.zw, i.uv);
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                float2 dir = i.uv - float2(0.5, 0.5);

                // atan2(x, y)：把顶部作为0点，并且顺时针增加
                float angle = atan2(dir.x, dir.y);

                // 转换到 0~1
                float normalizedAngle = angle / TWO_PI;

                if (normalizedAngle < 0)
                    normalizedAngle += 1;

                if (normalizedAngle > i.maskCutoff)
                    discard;
    
                col.rgb *= i.color.rgb;
                col.a   *= i.color.a;
                col.rgb *= col.a; // premultiply
                return col;
            }
            ENDHLSL
        }
    }
}
