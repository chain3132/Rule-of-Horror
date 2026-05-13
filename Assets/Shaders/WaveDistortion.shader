Shader "Hidden/WaveDistortion"
{
    Properties
    {
        // ค่า default เหล่านี้ใช้ถ้า C# ยังไม่ได้ Set
        // ปรับได้จาก Material Inspector ใน Play mode เพื่อ debug
        _WaveAmp   ("Wave Amplitude",        Range(0, 0.1)) = 0.02
        _WaveFreq  ("Wave Frequency (cycles)", Range(0, 20)) = 5
        _WaveSpeed ("Wave Speed",            Range(0, 10))  = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "WaveDistortionPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Blit.hlsl ให้ Vert function + Varyings struct + _BlitTexture + sampler_LinearClamp
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ─── Parameters (set by WaveDistortionEffect.cs) ───────────────
            float _WaveAmp;     // ความกว้างของการแกว่ง  (UV space 0–1)
            float _WaveFreq;    // จำนวนคลื่นตลอดความสูงหน้าจอ
            float _WaveSpeed;   // ความเร็วคลื่น (rad/sec scale)

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)

                float2 uv = input.texcoord;

                // _WaveFreq = จำนวนคลื่นที่มองเห็นตลอดความสูงหน้าจอ (cycles)
                // × 2π เพื่อแปลง cycles → radian ก่อนส่งเข้า sin
                float TAU = 6.28318530718;
                uv.x += sin(uv.y * _WaveFreq * TAU + _Time.y * _WaveSpeed) * _WaveAmp;

                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            }
            ENDHLSL
        }
    }
}
