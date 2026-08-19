Shader "S4/Flat Transparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
            };

            struct VertexToFragment
            {
                float4 position : SV_POSITION;
            };

            fixed4 _Color;

            VertexToFragment vert(AppData input)
            {
                VertexToFragment output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(VertexToFragment input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
