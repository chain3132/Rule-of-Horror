// ============================================================================
//  TreeIt Wind — URP shader สำหรับต้นไม้ที่ export จาก TreeIt
//
//  TreeIt ฝัง "น้ำหนักลม" ไว้ใน Vertex Color ของ mesh (ไม่ได้ใช้ UV2 แบบ SpeedTree)
//  จากการอ่านไฟล์ BananaTree.fbx:
//      trunk (banana_plant_0) : R = 0 คงที่   G = 12 ระดับ (ตามความสูง)   B = ไล่ตามความสูง
//      leaves(banana_plant_1) : R = 0–0.48     G = 0–0.96                  B = 0–0.64 (ตามความสูง)
//  → ใช้เป็น  B = โยกทั้งต้น (trunk)   G = แกว่งก้าน (branch)   R = ใบสั่น (leaf flutter)
//  ถ้าดูแล้วผิดส่วน เปลี่ยน channel ได้จาก dropdown ใน Material โดยไม่ต้องแก้โค้ด
//
//  Wind Zone ของ Unity ไม่ส่งค่าเข้า shader ทั่วไปเอง (ทำงานเฉพาะ Tree Creator/SpeedTree)
//  ต้องมี TreeItWindZone.cs ติดที่ WindZone เพื่อยิงค่าเป็น global:
//      _TreeItWindDir    xyz = ทิศลม (world)
//      _TreeItWindParams x = main  y = turbulence  z = pulseMagnitude  w = pulseFrequency
//  ถ้าไม่มี script ก็ยังไหว — ใช้ค่า default ด้านล่าง
//
//  หมายเหตุ: ห้ามเปิด Static Batching กับต้นไม้ที่ใช้ shader นี้ (pivot จะกลายเป็นจุดกลาง batch
//  ทำให้ทุกต้นโยกพร้อมกันเป๊ะ) — SRP Batcher / GPU Instancing ใช้ได้ปกติ
// ============================================================================
Shader "Rule of Horror/TreeIt Wind"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Base Map (RGB) Alpha (A)", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap   ("Normal Map", 2D) = "bump" {}
        _BumpScale          ("Normal Strength", Range(0, 2)) = 1
        _Smoothness         ("Smoothness", Range(0, 1)) = 0.2
        _Cutoff             ("Alpha Cutoff", Range(0, 1)) = 0.4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off = สองด้าน)", Float) = 0

        [Header(Translucency (leaf backlight))]
        _TransmissionMap ("Transmission Map (R)", 2D) = "white" {}
        _Translucency    ("Translucency", Range(0, 2)) = 0.6
        _TranslucencyPower ("Translucency Focus", Range(1, 16)) = 4

        [Header(Wind Weights from Vertex Color)]
        [Enum(R,0,G,1,B,2,A,3)] _TrunkChannel  ("Trunk bend channel",  Float) = 2
        [Enum(R,0,G,1,B,2,A,3)] _BranchChannel ("Branch sway channel", Float) = 1
        [Enum(R,0,G,1,B,2,A,3)] _LeafChannel   ("Leaf flutter channel", Float) = 0
        [Toggle] _IgnoreVertexColor ("Ignore vertex color (ใช้ความสูงแทน)", Float) = 0

        [Header(Wind Motion)]
        _WindStrength    ("Wind Strength (× WindZone main)", Range(0, 5)) = 1
        _WindSpeed       ("Wind Speed", Range(0, 5)) = 1
        _TrunkBend       ("Trunk Bend (m)", Range(0, 2)) = 0.35
        _BranchSway      ("Branch Sway (m)", Range(0, 1)) = 0.15
        _LeafFlutter     ("Leaf Flutter (m)", Range(0, 0.5)) = 0.05
        _FlutterFrequency("Leaf Flutter Frequency", Range(0, 30)) = 8

        [Header(Fallback when no TreeItWindZone script)]
        _DefaultWindDir  ("Default Wind Dir", Vector) = (1, 0, 0.3, 0)
        _DefaultWindMain ("Default Wind Main", Range(0, 3)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "TransparentCutout"
            "Queue"           = "AlphaTest"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // ───────────────────────── ส่วนที่ทุก pass ใช้ร่วมกัน ─────────────────────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);          SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);          SAMPLER(sampler_BumpMap);
        TEXTURE2D(_TransmissionMap);  SAMPLER(sampler_TransmissionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _BumpScale;
            half   _Smoothness;
            half   _Cutoff;
            half   _Translucency;
            half   _TranslucencyPower;
            float  _TrunkChannel, _BranchChannel, _LeafChannel, _IgnoreVertexColor;
            float  _WindStrength, _WindSpeed, _TrunkBend, _BranchSway, _LeafFlutter, _FlutterFrequency;
            float4 _DefaultWindDir;
            float  _DefaultWindMain;
        CBUFFER_END

        // global — ตั้งโดย TreeItWindZone.cs (ไม่อยู่ใน CBUFFER เพราะเป็นค่ากลางทุก material)
        float4 _TreeItWindDir;     // xyz ทิศ, w = 1 ถ้ามี script ตั้งค่า
        float4 _TreeItWindParams;  // x main, y turbulence, z pulseMagnitude, w pulseFrequency

        float PickChannel(float4 c, float idx)
        {
            // dropdown ใน material เก็บเป็น float 0–3 → เลือก channel ตรงๆ
            if (idx < 0.5) return c.r;
            if (idx < 1.5) return c.g;
            if (idx < 2.5) return c.b;
            return c.a;
        }

        // ── หัวใจของ shader: ขยับ vertex ตามลม (world space) ──
        // ใช้ทั้ง forward / shadow / depth เพื่อให้เงากับ depth ตรงกับตัวที่มองเห็น
        float3 ApplyWind(float3 positionWS, float3 normalWS, float4 color, float heightOS)
        {
            // ── ค่าลม: จาก WindZone ถ้ามี script ไม่งั้นใช้ค่า default ของ material ──
            bool   hasZone = _TreeItWindDir.w > 0.5;
            float3 windDir = hasZone ? _TreeItWindDir.xyz : _DefaultWindDir.xyz;
            windDir.y = 0;
            windDir = normalize(windDir + float3(1e-4, 0, 0));
            float  main       = (hasZone ? _TreeItWindParams.x : _DefaultWindMain) * _WindStrength;
            float  turbulence = hasZone ? _TreeItWindParams.y : 0.3;
            float  pulseMag   = hasZone ? _TreeItWindParams.z : 0.3;
            float  pulseFreq  = hasZone ? _TreeItWindParams.w : 0.1;

            if (main <= 0.0001) return positionWS;

            // ── น้ำหนักจาก vertex color (หรือความสูง ถ้าโมเดลไม่มี) ──
            float trunkW, branchW, leafW;
            if (_IgnoreVertexColor > 0.5)
            {
                float h = saturate(heightOS);
                trunkW = h; branchW = h * 0.6; leafW = h * 0.4;
            }
            else
            {
                trunkW  = PickChannel(color, _TrunkChannel);
                branchW = PickChannel(color, _BranchChannel);
                leafW   = PickChannel(color, _LeafChannel);
            }

            // ── phase ต่อต้น: เอาตำแหน่ง pivot มา hash ให้แต่ละต้นไม่โยกพร้อมกัน ──
            float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
            float  phase = dot(pivot, float3(0.37, 0.19, 0.53));
            float  t     = _Time.y * _WindSpeed;

            // ── ลมกระโชก (pulse ของ WindZone) — คูณเข้าไปกับความแรงหลัก ──
            float gust = 1.0 + pulseMag * sin(t * pulseFreq * 6.2831 + phase)
                             + turbulence * 0.25 * sin(t * 2.3 + phase * 1.7);
            float strength = main * max(gust, 0.0);

            float3 side = float3(-windDir.z, 0, windDir.x);

            // 1) ทั้งต้นโยกไปตามลม — ยกกำลังสองให้โคนนิ่ง ปลายไปเยอะ + โยกช้าๆ ไปกลับ
            float bend = trunkW * trunkW * _TrunkBend * strength
                       * (0.75 + 0.25 * sin(t * 0.9 + phase));
            positionWS += windDir * bend;
            positionWS.y -= bend * bend * 0.35;   // กดลงนิดให้ดูเหมือนโค้ง ไม่ใช่เลื่อนขนาน

            // 2) ก้าน/ใบแกว่งไปมา — ผสมตามลมกับขวางลม คนละความถี่ให้ดูไม่เป็นจังหวะ
            float sway = branchW * _BranchSway * strength * (0.6 + turbulence * 0.8);
            positionWS += windDir * sin(t * 1.7 + phase + positionWS.y * 0.4) * sway;
            positionWS += side    * sin(t * 1.3 + phase * 1.3 + positionWS.x * 0.3) * sway * 0.5;

            // 3) ใบสั่นเร็ว — ดันตามแนว normal (ใบพลิกได้) ใช้ตำแหน่งเป็น seed ให้แต่ละใบต่างกัน
            float flutter = leafW * _LeafFlutter * strength * (0.5 + turbulence);
            float seed    = dot(positionWS, float3(1.3, 2.1, 0.7)) + phase;
            positionWS += normalWS * sin(t * _FlutterFrequency + seed) * flutter;

            return positionWS;
        }

        // ความสูงในโมเดล normalize คร่าวๆ — ใช้เฉพาะโหมด IgnoreVertexColor
        float ApproxHeight01(float3 positionOS)
        {
            // ไม่รู้ความสูงจริงของ mesh ใน shader → เดาจาก scale ของ object (แกน Y)
            float scaleY = length(UNITY_MATRIX_M._m01_m11_m21);
            return positionOS.y * scaleY / 4.0;   // ต้นกล้วยสูง ~3.4 m → ยอด ≈ 0.85
        }
        ENDHLSL

        // ═══════════════════════════ Forward ═══════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            // URP lighting keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;   // w = sign
                half3  vertexSH   : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
                float4 shadowCoord: TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = ApplyWind(posWS, n.normalWS, IN.color, ApproxHeight01(IN.positionOS.xyz));

                OUT.positionWS  = posWS;
                OUT.positionCS  = TransformWorldToHClip(posWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS    = n.normalWS;
                OUT.tangentWS   = half4(n.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.vertexSH    = SampleSHVertex(n.normalWS);
                OUT.fogFactor   = ComputeFogFactor(OUT.positionCS.z);
                OUT.shadowCoord = TransformWorldToShadowCoord(posWS);
                return OUT;
            }

            half4 Frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(baseTex.a - _Cutoff);

                // normal map + พลิก normal ด้านหลังใบ (ใบบาง render สองด้าน)
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                half3 bitangent = IN.tangentWS.w * cross(IN.normalWS, IN.tangentWS.xyz);
                half3 normalWS  = normalize(TransformTangentToWorld(nTS, half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS)));
                if (!IS_FRONT_VFACE(face, true, false)) normalWS = -normalWS;

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS            = IN.positionWS;
                inputData.positionCS            = IN.positionCS;
                inputData.normalWS              = normalWS;
                inputData.viewDirectionWS       = viewDirWS;
                inputData.shadowCoord           = IN.shadowCoord;
                inputData.fogCoord              = IN.fogFactor;
                inputData.bakedGI               = SampleSHPixel(IN.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask            = half4(1,1,1,1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = baseTex.rgb;
                surfaceData.alpha      = 1;
                surfaceData.metallic   = 0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1;
                surfaceData.normalTS   = nTS;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // ── แสงทะลุใบ: มองย้อนแสงแล้วใบสว่างขึ้น ให้ใบกล้วยดูบาง ──
                Light mainLight = GetMainLight(IN.shadowCoord);
                half  transmission = SAMPLE_TEXTURE2D(_TransmissionMap, sampler_TransmissionMap, IN.uv).r;
                half  backLight = pow(saturate(dot(viewDirWS, -mainLight.direction)), _TranslucencyPower);
                color.rgb += baseTex.rgb * mainLight.color * mainLight.shadowAttenuation
                           * backLight * transmission * _Translucency;

                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ═══════════════════════════ Shadow ═══════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = ApplyWind(posWS, normalWS, IN.color, ApproxHeight01(IN.positionOS.xyz));

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                // ใบเป็นแผ่นบางเรนเดอร์สองด้าน — ครึ่งหนึ่งของใบ normal หันหนีแสง
                // ApplyShadowBias จะดัน vertex "เข้าหา" แสงแทนที่จะออก → ใบเงาตัวเองเป็นลายคลื่น
                // และพอใบสั่นลายนั้นก็เลื่อนตาม (เงาวิ่งบนใบ) แก้โดยพลิก normal ให้หันหาแสงก่อน bias เสมอ
                if (dot(normalWS, lightDir) < 0) normalWS = -normalWS;

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = posCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ═══════════════════════════ Depth ═══════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = ApplyWind(posWS, normalWS, IN.color, ApproxHeight01(IN.positionOS.xyz));
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ═══════════════════════════ DepthNormals (SSAO / decals) ═══════════════════════════
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = ApplyWind(posWS, normalWS, IN.color, ApproxHeight01(IN.positionOS.xyz));
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS   = normalWS;
                return OUT;
            }

            half4 Frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                half3 n = normalize(IN.normalWS);
                if (!IS_FRONT_VFACE(face, true, false)) n = -n;
                return half4(NormalizeNormalPerPixel(n), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
