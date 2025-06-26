Shader "Moths/Terrain/Water"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaxHeight ("Wave Height", float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                half4 color : COLOR0;
            };



            sampler2D _MainTex;
            float4 _MainTex_ST;

            half _MaxHeight;

            half2 hash2(half2 p ){
	            p = half2( dot(p,half2(127.1,311.7)),dot(p,half2(269.5,183.3)));
	            return frac(sin(p)*43758.5453);
            }

            float3 wave(float3 origin, float3 vPosition, half range, half maxHeight, half3 direction)
            {
                const float PI = 3.14159265359;
                const float PI_2 = 6.28318530718;

                //sin((x^2) * pi)
                half vDistance = saturate(distance(origin, vPosition) / range);
                vDistance = sin(pow(vDistance, 2) * PI);
                half dotDir = saturate(dot(normalize(vPosition - origin), direction));

                float height = vDistance * dotDir * maxHeight;

                return float3(0, height, 0) + direction * height * 2;
            }

            v2f vert (appdata v)
            {
                half3 direction = half3(0, 0, 1);
                half3 waveOffset = wave((float3)0, v.vertex, 2, _MaxHeight, direction);
                v.vertex.xyz += waveOffset;

                v2f o;

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                o.color = lerp(half4(0,0,1,1), half4(1,1,1,1), length(waveOffset));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
