// A plain color with a fixed light from above. It has no LightMode tag and does not use the
// pipeline's lights, so it looks the same in the Built-in Render Pipeline and in URP
Shader "Deliverable3/SimpleLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float light = saturate(dot(normalize(i.normal), normalize(float3(0.4, 1.0, -0.3))));
                return fixed4(_Color.rgb * (0.45 + 0.55 * light), 1.0);
            }
            ENDCG
        }
    }
}
