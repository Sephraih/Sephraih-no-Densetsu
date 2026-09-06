Shader "Sephraih/ShoreGradient"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShoreDistanceTex ("Baked shore-distance field (R = normalized distance, bilinear)", 2D) = "white" {}
        _ShoreOrigin ("World-space min corner of the baked distance field", Vector) = (0, 0, 0, 0)
        _ShoreSize ("World-space size of the baked distance field", Vector) = (1, 1, 0, 0)
        _ShoreColor ("Shore (near, shallow) tint", Color) = (1, 1, 1, 1)
        _DeepColor ("Deep (far from shore) tint", Color) = (0.4, 0.5, 0.6, 1)
        _ShoreFalloffCurve ("Falloff exponent applied to sampled distance before lerp", Range(0.1, 5)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _ShoreDistanceTex;
            float4 _ShoreOrigin;
            float4 _ShoreSize;
            fixed4 _ShoreColor;
            fixed4 _DeepColor;
            float _ShoreFalloffCurve;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                // World-space XY, used below to look up this fragment's position in the baked
                // distance field independently of the tile's own sprite UV (which only ever
                // addresses that one tile's small sub-rect of the shared 16-sprite sheet, not a
                // stable map-wide coordinate - same reasoning SpriteSubmersion.shader documents for
                // why it can't use texcoord/UV for its own world-position-relative math).
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xy;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                float2 shoreUV = (IN.worldPos - _ShoreOrigin.xy) / max(_ShoreSize.xy, 0.0001);
                float dist = tex2D(_ShoreDistanceTex, saturate(shoreUV)).r;
                float t = saturate(pow(dist, _ShoreFalloffCurve));

                c.rgb *= lerp(_ShoreColor.rgb, _DeepColor.rgb, t);
                c.rgb *= c.a;
                clip(c.a - 0.001);
                return c;
            }
            ENDCG
        }
    }
}
