Shader "Sephraih/SpriteSubmersion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _SubmergeDepth ("World units below pivot the waterline sits (large = disabled)", Float) = 999
        _BandThickness ("Dither Band Thickness, world units", Range(0.001, 1)) = 0.045
        _CurveDepth ("Extra depth at horizontal center vs. edges, world units (meniscus dip)", Float) = 0.035
        _CurveWidth ("Half-width the center dip fades out over, world units", Float) = 0.3
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
                float  localY   : TEXCOORD1;
                float  localX   : TEXCOORD2;
            };

            sampler2D _MainTex;
            float _SubmergeDepth;
            float _BandThickness;
            float _CurveDepth;
            float _CurveWidth;

            // 4x4 Bayer ordered-dither matrix, thresholds in [0,1). Indexed by screen pixel so the
            // dithered band stays crisp at native pixel scale regardless of a sprite's own size -
            // this project's Pixel Perfect Camera guarantees 1 game pixel == 1 screen pixel, so
            // SV_POSITION.xy is stable integer game-pixel coordinates, not a subpixel/zoomed value.
            static const float bayer4x4[16] = {
                0.0/16.0, 8.0/16.0, 2.0/16.0, 10.0/16.0,
                12.0/16.0, 4.0/16.0, 14.0/16.0, 6.0/16.0,
                3.0/16.0, 11.0/16.0, 1.0/16.0, 9.0/16.0,
                15.0/16.0, 7.0/16.0, 13.0/16.0, 5.0/16.0
            };

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                // Object-space (pre-transform) vertex Y, i.e. real world units relative to this
                // sprite's own pivot (local Y = 0) - NOT texture UV (broken: most sprites here are
                // sliced from a multi-frame sheet, so UV only covers a small sub-rectangle of the full
                // sheet) and NOT normalized against this sprite's own rect/bounds either (that rect is
                // padded very differently per combo direction, to fit each one's own sword-swing arc -
                // see SubmersionController's class comment for the two prior approaches this replaced).
                // Comparing this directly against a fixed WORLD-UNIT depth works because every sprite
                // sheet this shader is used on shares the same PPU, so local Y already means the same
                // real distance below pivot for every direction/pose of this character.
                OUT.localY = IN.vertex.y;
                OUT.localX = IN.vertex.x; // world units from pivot, same reasoning as localY above
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                // Meniscus dip: a simple parabola centered on the pivot's own X, 1 at localX=0 fading
                // to 0 by |localX|=_CurveWidth - a fixed world-unit width (same reasoning as
                // _SubmergeDepth: works uniformly across poses without per-sprite width data, and any
                // pixels further out than a typical body width just get zero curve, which is fine since
                // that's usually a wide attack swing rather than the actual character silhouette).
                float centerBias = saturate(1.0 - (IN.localX * IN.localX) / max(_CurveWidth * _CurveWidth, 0.0001));
                float waterlineY = -_SubmergeDepth - _CurveDepth * centerBias;
                float lowerBound = waterlineY - _BandThickness * 0.5;
                float upperBound = waterlineY + _BandThickness * 0.5;

                float visible = 1.0;
                if (IN.localY < lowerBound)
                {
                    visible = 0.0;
                }
                else if (IN.localY < upperBound)
                {
                    float t = saturate((IN.localY - lowerBound) / max(_BandThickness, 0.0001));
                    uint2 screenPixel = uint2(IN.vertex.xy);
                    uint bx = screenPixel.x % 4;
                    uint by = screenPixel.y % 4;
                    float threshold = bayer4x4[by * 4 + bx];
                    visible = (t > threshold) ? 1.0 : 0.0;
                }

                c.a *= visible;
                c.rgb *= c.a;
                clip(c.a - 0.001);
                return c;
            }
            ENDCG
        }
    }
}
