Shader "Rule of Horror/Book Ink"
{
    // หมึกบนหน้ากระดาษหนังสือ — วาดเฟรมวิดีโอ (พื้นขาว ตัวหนังสือเข้ม) ทับหน้ากระดาษแบบ "คูณ"
    //
    // ทำไมเป็นการคูณ ไม่ใช่วางภาพทับ:
    //   ผลลัพธ์ = สีหน้ากระดาษที่วาดไว้แล้ว × สีของวิดีโอ
    //   พื้นขาว (1) คูณอะไรก็ได้ค่าเดิม → ลายกระดาษ เงา และแสงในฉากยังอยู่ครบ
    //   หมึกดำ (0) คูณแล้วได้ดำ → เห็นแต่ตัวหนังสือ
    // วิดีโอจึงไม่ต้องมี alpha channel เลย และไม่ต้อง bake ลายกระดาษลงในวิดีโอ
    //
    // White Cutoff ตัดพื้นที่ "เกือบขาว" ทิ้ง — H.264 ไม่ได้เก็บขาวเป๊ะๆ (มี noise 250-254)
    // ถ้าไม่ตัด ทั้งหน้าจะคล้ำลงนิดๆ เป็นกรอบสี่เหลี่ยมให้เห็นบนกระดาษ

    //
    // วิดีโอสองแบบ (BookNoteSurface ▸ Ink Style, Auto = ดูจากเฟรมแรกให้เอง):
    //   Dark On Light  หมึกเข้มบนพื้นขาว — หมึกใช้สีจากวิดีโอเลย (หรือ Ink Color ถ้าติ๊ก Mono)
    //   Light On Dark  ตัวหนังสือขาวบนพื้นดำ (After Effects export comp ที่ไม่มีพื้นหลังจะได้แบบนี้)
    //                  ความสว่างของวิดีโอกลายเป็น "ที่ที่มีหมึก" แล้วระบายด้วย Ink Color
    //                  — สีขาวของตัวหนังสือในวิดีโอเอามาคูณกระดาษตรงๆ ไม่ได้ จะมองไม่เห็นอะไรเลย

    Properties
    {
        [NoScaleOffset] _InkTex ("Ink (video RenderTexture)", 2D) = "white" {}
        _InkColor     ("Ink Color", Color) = (0.06, 0.04, 0.04, 1)
        [ToggleUI] _InkMono ("Mono Ink (Dark On Light: use Ink Color instead of video colour)", Float) = 0
        _InkAmount    ("Ink Amount (fade)", Range(0, 1)) = 1
        _InkBold      ("Bold (thicken strokes)", Range(0, 4)) = 1.5
        _InkGain      ("Ink Strength", Range(1, 4)) = 1.75
        _WhiteCutoff  ("White Cutoff", Range(0, 0.5)) = 0.08
        _Softness     ("Cutoff Softness", Range(0.001, 0.5)) = 0.12
        _EdgeFade     ("Border Fade", Range(0, 0.2)) = 0.02
        [HDR] _GlowColor ("Glow (added light, black = off)", Color) = (0, 0, 0, 0)

        // ตั้งจากสคริปต์ (BookNoteSurface) — scale.xy offset.zw ใช้กลับด้านภาพ
        [HideInInspector] _InkUV ("Ink UV", Vector) = (1, 1, 0, 0)

        // ตั้งจากสคริปต์ — 0 = Dark On Light, 1 = Light On Dark
        [HideInInspector] _InkInvert ("Ink Invert", Float) = 0

        // ตั้งจากสคริปต์ — 1/กว้าง, 1/สูง ของ RenderTexture (ใช้กับ Bold)
        [HideInInspector] _InkTexel ("Ink Texel", Vector) = (0.001, 0.001, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D(_InkTex);
        SAMPLER(sampler_InkTex);

        CBUFFER_START(UnityPerMaterial)
            half4  _InkColor;
            half   _InkMono;
            half   _InkInvert;
            half   _InkAmount;
            half   _InkBold;
            half   _InkGain;
            float4 _InkTexel;
            half   _WhiteCutoff;
            half   _Softness;
            half   _EdgeFade;
            half4  _GlowColor;
            float4 _InkUV;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.uv         = input.uv;
            return o;
        }

        // ห่างจากสีพื้นแค่ไหน (0 = สีพื้นเป๊ะ, 1 = ตัวหนังสือเต็ม)
        half Signal(float2 uv, out half3 video)
        {
            video = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, uv).rgb;

            // วัดความเข้มในค่าแบบ sRGB (แบบที่ตาเห็น/แบบที่ After Effects เห็น) ไม่ใช่ linear
            // ไม่งั้น cutoff 0.08 ในโปรเจกต์ Linear จะกินเทาอ่อนไปเยอะกว่าที่ตั้งใจ
            half luma = dot(FastLinearToSRGB(video), half3(0.2126, 0.7152, 0.0722));
            return lerp(1.0h - luma, luma, step(0.5h, _InkInvert));
        }

        // ink = ความเข้มของหมึกที่จุดนี้ (0 = กระดาษเปล่า, 1 = หมึกเต็ม), video = สีวิดีโอ
        half SampleInk(float2 quadUV, out half3 video)
        {
            float2 uv = quadUV * _InkUV.xy + _InkUV.zw;

            // Bold: เอาค่าที่เข้มที่สุดจากจุดรอบๆ = ขยายเส้นออกทุกทิศ
            // ตัวหนังสือในวิดีโอ 2480 px พอย่อเหลือหน้ากระดาษร้อยกว่า pixel บนจอ เส้นจะบางกว่า
            // 1 pixel แล้วหายไปเป็นจุดๆ — หนาขึ้นนิดเดียวก็อ่านออกจากระยะยืน
            float2 o = _InkTexel.xy * _InkBold;
            half3 unused;
            half signal = Signal(uv, video);
            signal = max(signal, Signal(uv + float2( o.x,  o.y), unused));
            signal = max(signal, Signal(uv + float2(-o.x,  o.y), unused));
            signal = max(signal, Signal(uv + float2( o.x, -o.y), unused));
            signal = max(signal, Signal(uv + float2(-o.x, -o.y), unused));

            // Strength: mipmap เฉลี่ยเส้นบางกับพื้นจนจางลง — คูณกลับให้เข้มหลังตัดพื้นทิ้งแล้ว
            // (คูณก่อนตัด noise ของพื้นจะเกิน cutoff กลายเป็นฝ้าทั้งหน้า)
            half ink = saturate(smoothstep(_WhiteCutoff, _WhiteCutoff + _Softness, signal) * _InkGain);

            // จางที่ขอบ quad — กัน pixel ขอบวิดีโอ (มักมีเส้นเทาบางๆ จากการ encode) เป็นกรอบ
            float2 edge = min(quadUV, 1.0 - quadUV);
            ink *= smoothstep(0.0, max(_EdgeFade, 1e-4), min(edge.x, edge.y));

            return ink * _InkAmount;
        }
        ENDHLSL

        // Pass 1: คูณลงบนกระดาษ — ตัวหนังสือ
        Pass
        {
            Name "InkMultiply"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMultiply
            #pragma multi_compile_instancing

            half4 FragMultiply(Varyings input) : SV_Target
            {
                half3 video;
                half  ink = SampleInk(input.uv, video);

                // Light On Dark ใช้ Ink Color เสมอ — สีขาวของตัวหนังสือในวิดีโอคูณแล้วไม่เห็นอะไร
                half  useColor = max(step(0.5h, _InkMono), step(0.5h, _InkInvert));
                half3 inkRGB   = lerp(video, _InkColor.rgb, useColor);

                return half4(lerp(half3(1, 1, 1), inkRGB, ink), 1);
            }
            ENDHLSL
        }

        // Pass 2: แสงเรืองของตัวหนังสือ (บวกแสง) — Glow สีดำ = ไม่มีผลอะไร
        // แยก pass เพราะการคูณทำให้สว่างขึ้นไม่ได้ ตัวหนังสือเวทมนตร์ที่เรืองในที่มืดต้องบวกแสงเพิ่ม
        Pass
        {
            Name "InkGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlow
            #pragma multi_compile_instancing

            half4 FragGlow(Varyings input) : SV_Target
            {
                half3 video;
                half  ink = SampleInk(input.uv, video);
                return half4(_GlowColor.rgb * ink, 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
