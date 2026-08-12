Shader "Custom/Hit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct HitRenderData
            {
                float2 pos;
                float radius;
                float4 color;
            };

            StructuredBuffer<HitRenderData> _NoteBuffer;
            float4x4 _RootMatrix;

            struct appdata 
            { 
                float4 vertex : POSITION; 
                float2 uv : TEXCOORD0; 
            };
            
            struct v2f 
            { 
                float4 pos : SV_POSITION; 
                float2 uv : TEXCOORD0; 
                float4 color : TEXCOORD1;
            };

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                HitRenderData hit = _NoteBuffer[id];
                
                float2 p = v.vertex.xy * hit.radius;
                p += hit.pos;
                
                float3 world = mul(_RootMatrix, float4(p,0,1)).xyz;
                o.pos = TransformWorldToHClip(world);  
                o.uv = v.uv;
                o.color = hit.color;
                
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 col = i.color;
                
                // UV center is at (0.5, 0.5). Get distance from center.
                float dist = length(i.uv - 0.5) * 2.0;
                
                // Solid circle with slight anti-aliasing at the edge
                float alpha = smoothstep(1.0, 0.95, dist);
                
                col.a *= alpha;
                col.rgb *= col.a; // premultiply
                return col;
            }
            ENDHLSL
        }
    }
}
