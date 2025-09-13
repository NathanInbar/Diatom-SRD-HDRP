Shader "Custom/InstancedColorUnlit"
{
    Properties { _BaseMap("Base Map", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionHCS : SV_Position;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _BaseColor);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                return albedo * IN.color;
            }
            ENDHLSL
        }
    }
}
