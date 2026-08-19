Shader "S4/Lit Transparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
        _Brightness ("Brightness", Range(0, 2)) = 1
        _DiffuseStrength ("Diffuse Strength", Range(0, 2)) = 1
        _ConstantLight ("Constant Light", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 150
        Cull Off
        ZWrite Off

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade noshadow
        #pragma target 3.0

        fixed4 _Color;
        half _Brightness;
        half _DiffuseStrength;
        half _ConstantLight;

        struct Input
        {
            float3 worldNormal;
        };

        void surf(Input input, inout SurfaceOutput output)
        {
            half3 baseColor = _Color.rgb * _Brightness;
            output.Albedo = baseColor * _DiffuseStrength;
            output.Emission = baseColor * _ConstantLight;
            output.Alpha = _Color.a;
        }
        ENDCG
    }

    Fallback "Transparent/VertexLit"
}
