Shader "Custom/NoteRich"
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

            struct NotesRenderData
            {
                float2 pos;
                float angRad;
                float scale;
                float stretchY;
                uint spriteId;
                float4 color;
                float brightness;
                uint exSprite;
                float4 exColor;
                float2 sliceBorder;   // (topFrac, botFrac), (0,0) = normal
                uint sort;
            };

            StructuredBuffer<NotesRenderData> _NoteBuffer;
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
                float4 exRect : TEXCOORD3;
                float4 exColor : TEXCOORD4;
                float2 sliceBorder : TEXCOORD5;
                float4 caps : TEXCOORD6; // x: topCapFrac, y: botCapFrac, z: middleFrac, w: sliceMid
            };

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                NotesRenderData note = _NoteBuffer[id];
                float4 rect = _SpriteRects[note.spriteId];
                float2 worldSize = (rect.zw - rect.xy) * _AtlasSize / _PixelsPerUnit;
                float2 finalSize = worldSize;
                finalSize.y += note.stretchY;
                finalSize *= note.scale;
                float2 p = v.vertex.xy * finalSize;
                float s = sin(note.angRad); float c = cos(note.angRad);
                float2 r = float2(p.x*c - p.y*s, p.x*s + p.y*c);
                r += note.pos;
                float3 world = mul(_RootMatrix, float4(r,0,1)).xyz;
                o.pos = TransformWorldToHClip(world);  
                o.uv = v.uv;
                o.rect = rect;
                
                // Color with brightness applied
                o.color = note.color;
                o.color.rgb *= note.brightness;
                
                if (note.exSprite != 0) {
                    o.exRect = _SpriteRects[note.exSprite];
                    o.exColor = note.exColor;
                } else {
                    o.exRect = float4(0,0,0,0);
                    o.exColor = float4(0,0,0,0); // a=0 means won't affect color
                }
                
                o.sliceBorder = note.sliceBorder;
                
                if (note.sliceBorder.x + note.sliceBorder.y > 0.0) {
                    float spriteH_uv = rect.w - rect.y;
                    float nativeH = spriteH_uv * _AtlasSize.y / _PixelsPerUnit;
                    float renderedH = (nativeH + note.stretchY) * note.scale;
                    renderedH = max(renderedH, 1e-6);
                    float topCapFrac = (note.sliceBorder.x * nativeH * note.scale) / renderedH;
                    float botCapFrac = (note.sliceBorder.y * nativeH * note.scale) / renderedH;                    
                    float middleFrac = 1.0 - topCapFrac - botCapFrac;
                    middleFrac = max(middleFrac, 1e-6);
                    float sliceMid = 1.0 - note.sliceBorder.x - note.sliceBorder.y;
                    o.caps = float4(topCapFrac, botCapFrac, middleFrac, sliceMid);
                } else {
                    o.caps = float4(0, 0, 1, 1);
                }

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // ---- Calculate shared 3-slice UV ----
                float2 spriteUV = i.uv;

                if (i.sliceBorder.x + i.sliceBorder.y > 0.0)
                {
                    float topCapFrac = i.caps.x;
                    float botCapFrac = i.caps.y;
                    float middleFrac = i.caps.z;
                    float sliceMid = i.caps.w;

                    float uvY = i.uv.y;
                    float remapY;

                    if (uvY >= 1.0 - topCapFrac)
                    {
                        // Top cap
                        float t = (uvY - (1.0 - topCapFrac)) / max(topCapFrac, 1e-6);
                        remapY = (1.0 - i.sliceBorder.x) + t * i.sliceBorder.x;
                    }
                    else if (uvY <= botCapFrac)
                    {
                        // Bottom cap
                        float t = uvY / max(botCapFrac, 1e-6);
                        remapY = t * i.sliceBorder.y;
                    }
                    else
                    {
                        // Middle stretch
                        float t = (uvY - botCapFrac) / max(middleFrac, 1e-6);
                        remapY = i.sliceBorder.y + t * sliceMid;
                    }

                    spriteUV.y = remapY;
                }

                // ---- Main sprite ----
                float2 uv = float2(
                    lerp(i.rect.x, i.rect.z, spriteUV.x),
                    lerp(i.rect.y, i.rect.w, spriteUV.y)
                );

                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // ---- EX overlay (uses same 3-slice UV) ----
                if (i.exColor.a > 0.0)
                {
                    float2 uvFrame = float2(
                        lerp(i.exRect.x, i.exRect.z, spriteUV.x),
                        lerp(i.exRect.y, i.exRect.w, spriteUV.y)
                    );

                    float4 frame = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvFrame);

                    frame.rgb *= i.exColor.rgb;
                    frame.a   *= i.exColor.a;

                    // Standard alpha blend
                    col.rgb = frame.rgb * frame.a + col.rgb * (1.0 - frame.a);
                    col.a   = frame.a + col.a * (1.0 - frame.a);
                }

                // ---- Vertex color + brightness ----
                col.rgb *= i.color.rgb;
                col.a   *= i.color.a;

                // Premultiply alpha
                col.rgb *= col.a;

                return col;
            }
            ENDHLSL
        }
    }
}
