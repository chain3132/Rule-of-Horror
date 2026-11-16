// ภาพฟิล์มเก่าสำหรับ flashback — ใช้กับ RawImage ที่โชว์ RenderTexture ของกล้อง flashback
//
// ทำทุกอย่างในตัวเดียว ไม่ผ่าน Volume ของเกม: Global Volume ถูกสลับ profile ตาม Relax/Tension
// และช่วงเวียนหัวก็แก้ค่าใน profile อยู่ ถ้าใช้ Volume ร่วมจะตีกัน
//
//   ขาวดำ/ซีเปีย · contrast · ภาพกะพริบ · grain · รอยขีดแนวตั้ง · ฝุ่น/รอยจาก overlay (Multiply)
//   · ขอบมืด · ภาพสั่น (gate weave)
//
// ทุกอย่างที่สุ่มเปลี่ยนตาม _FilmFrame (FlashbackPlayer เพิ่มทีละ 1 ตาม Film FPS) ไม่ใช่ตามเฟรมเกม
// grain และฝุ่นจึงกระตุกเป็นจังหวะเดียวกับภาพ เหมือนฟิล์มจริง
Shader "Rule of Horror/Old Film (UI)"
{
    Properties
    {
        [PerRendererData] _MainTex ("Flashback Image", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Colour)]
        _SepiaColor ("Sepia Colour", Color) = (1.0, 0.9, 0.74, 1)
        _Desaturate ("Desaturate", Range(0, 1)) = 1
        _Contrast ("Contrast", Range(0.5, 2)) = 1.2
        _Brightness ("Brightness", Range(0.3, 1.5)) = 0.95

        [Header(Film)]
        _Grain ("Grain", Range(0, 0.5)) = 0.12
        _Flicker ("Flicker", Range(0, 0.5)) = 0.08
        _Vignette ("Vignette", Range(0, 2)) = 1
        _Scratches ("Scratch Lines", Range(0, 1)) = 0.5
        _Jitter ("Gate Weave", Range(0, 0.01)) = 0.002

        [Header(Dust Overlay)]
        _DustTex ("Dust / Scratch Overlay (multiply)", 2D) = "white" {}
        _DustStrength ("Dust Strength", Range(0, 1)) = 0.85
        _DustWander ("Dust Wander", Range(0, 0.5)) = 0.12

        [HideInInspector] _FilmFrame ("Film Frame (set by FlashbackPlayer)", Float) = 0

        // ── UI ตามมาตรฐาน (Mask / RectMask2D) ──
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "OldFilm"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _DustTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _SepiaColor;
            float _Desaturate, _Contrast, _Brightness;
            float _Grain, _Flicker, _Vignette, _Scratches, _Jitter;
            float _DustStrength, _DustWander;
            float _FilmFrame;

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float f = _FilmFrame;

                // ── ภาพสั่น: ทั้งเฟรมขยับนิดหน่อยทุกเฟรมฟิล์ม (แนวตั้งมากกว่าแนวนอน) ──
                float2 weave = (float2(Hash11(f * 1.7), Hash11(f * 2.3 + 5.0)) - 0.5) * _Jitter * float2(1.0, 2.0);
                float2 uv = IN.texcoord + weave;

                half3 col = (tex2D(_MainTex, uv) + _TextureSampleAdd).rgb;

                // ── ขาวดำ → ซีเปีย ──
                half lum = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(col, lum * _SepiaColor.rgb, _Desaturate);

                // ── contrast / ความสว่าง / กะพริบ ──
                col = (col - 0.5) * _Contrast + 0.5;
                col *= _Brightness * (1.0 + (Hash11(f * 3.1 + 11.0) - 0.5) * 2.0 * _Flicker);

                // ── grain (เปลี่ยนเม็ดทุกเฟรมฟิล์ม) ──
                float2 grainUV = floor(IN.texcoord * _ScreenParams.xy * 0.5);
                col += (Hash21(grainUV + f * 17.13) - 0.5) * _Grain;

                // ── รอยขีดแนวตั้ง: สูงสุด 3 เส้น สุ่มโผล่ทีละเฟรม ──
                for (int i = 0; i < 3; i++)
                {
                    float seed = f + i * 37.0;
                    if (Hash11(seed * 0.73 + 3.0) > _Scratches * 0.6) continue;

                    float x     = Hash11(seed * 1.31 + 7.0);
                    float width = 0.0007 + Hash11(seed * 2.11) * 0.0015;
                    float scratch = 1.0 - smoothstep(0.0, width, abs(IN.texcoord.x - x));
                    // ขาดเป็นช่วงๆ ตามแนวตั้ง ไม่ใช่เส้นยาวตลอดจอ
                    scratch *= step(0.3, Hash11(floor(IN.texcoord.y * 8.0) + seed));
                    col *= 1.0 - scratch * 0.55;
                }

                // ── ฝุ่น/รอยจาก overlay — สุ่มตำแหน่ง+พลิกทุกเฟรมฟิล์ม แล้วคูณทับ ──
                float2 duv = IN.texcoord;
                if (Hash11(f * 5.7) > 0.5) duv.x = 1.0 - duv.x;
                if (Hash11(f * 6.3) > 0.5) duv.y = 1.0 - duv.y;
                duv += (float2(Hash11(f * 8.9), Hash11(f * 9.7)) - 0.5) * _DustWander;
                half3 dust = tex2D(_DustTex, duv).rgb;
                col *= lerp(half3(1, 1, 1), dust, _DustStrength);

                // ── ขอบมืด ──
                float d = distance(IN.texcoord, float2(0.5, 0.5));
                col *= 1.0 - smoothstep(0.3, 0.85, d) * _Vignette;

                half4 o = half4(saturate(col), IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                o.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(o.a - 0.001);
                #endif

                return o;
            }
            ENDCG
        }
    }
}
