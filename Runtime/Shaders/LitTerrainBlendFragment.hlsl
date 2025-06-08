#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"


CBUFFER_START(TerrainBlend)
TEXTURE2D(_controlMap); SAMPLER(sampler_controlMap);

TEXTURE2D(_layer0); SAMPLER(sampler_layer0);
TEXTURE2D(_layer1);
TEXTURE2D(_layer2);

TEXTURE2D(_normal0); SAMPLER(sampler_normal0);
TEXTURE2D(_normal1);
TEXTURE2D(_normal2);

float3 _terrainSize;
float3 _terrainPositionWS;
half _blendRange;

half2 _size0;
half2 _size1;
half2 _size2;

CBUFFER_END

struct TerrainBlendData {
	
	float4 color;
	float3 positionWS;
	half3 normalWS;
};


float hash(float n) { return frac(sin(n) * 1e4); }
float hash(float2 p) { return frac(1e4 * sin(17.0 * p.x + p.y * 0.1) * (0.1 + abs(sin(p.y * 13.0 + p.x)))); }
float noise(float2 x) {
	float2 i = floor(x);
	float2 f = frac(x);

	// Four corners in 2D of a tile
	float a = hash(i);
	float b = hash(i + float2(1.0, 0.0));
	float c = hash(i + float2(0.0, 1.0));
	float d = hash(i + float2(1.0, 1.0));

	// Simple 2D lerp using smoothstep envelope between the values.
	// return float3(lerp(lerp(a, b, smoothstep(0.0, 1.0, f.x)),
	//			lerp(c, d, smoothstep(0.0, 1.0, f.x)),
	//			smoothstep(0.0, 1.0, f.y)));

	// Same code, with the clamps in smoothstep and common subexpressions
	// optimized away.
	float2 u = f * f * (3.0 - 2.0 * f);
	return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void NormalMapMix(half2 uv, inout half3 splatControl, inout half3 mixedNormal)
{

    half3 nrm = half(0.0);
    nrm += splatControl.r * UnpackNormalScale(SAMPLE_TEXTURE2D(_normal0, sampler_normal0, uv / _size0), 1);
    nrm += splatControl.g * UnpackNormalScale(SAMPLE_TEXTURE2D(_normal1, sampler_normal0, uv / _size1), 1);
    nrm += splatControl.b * UnpackNormalScale(SAMPLE_TEXTURE2D(_normal2, sampler_normal0, uv / _size2), 1);

    // avoid risk of NaN when normalizing.
    #if !HALF_IS_FLOAT
        nrm.z += half(0.01);
    #else
        nrm.z += 1e-5f;
    #endif

    mixedNormal = normalize(nrm.xyz);
}

void ComputeTriplanar(
    float3 normalWS,
    float3 positionLocal,
    out float2 uv,
    out float3x3 TBN)
{
    float3 blend = abs(normalWS);
    blend = pow(blend, 4.0);
    blend /= (blend.x + blend.y + blend.z + 1e-5); // avoid divide-by-zero

    float2 uvX = positionLocal.zy;
    float2 uvY = positionLocal.xz;
    float2 uvZ = positionLocal.xy;

    uv = uvX * blend.x + uvY * blend.y + uvZ * blend.z;

    float3 tX = float3(0, 0, -1), bX = float3(0, 1, 0), nX = float3(1, 0, 0);
    float3 tY = float3(1, 0, 0),  bY = float3(0, 0, 1), nY = float3(0, 1, 0);
    float3 tZ = float3(1, 0, 0),  bZ = float3(0, 1, 0), nZ = float3(0, 0, 1);

    float3 tangentWS =
        tX * blend.x +
        tY * blend.y +
        tZ * blend.z;

    float3 bitangentWS =
        bX * blend.x +
        bY * blend.y +
        bZ * blend.z;

    float3 normalBlendWS =
        nX * blend.x +
        nY * blend.y +
        nZ * blend.z;

    TBN = float3x3(normalize(tangentWS), normalize(bitangentWS), normalize(normalBlendWS));
}


half4 TerrainBlendFragment(TerrainBlendData data, InputData inputData)
{
	float3 positionLocal = data.positionWS - _terrainPositionWS;

	half2 uv;
	uv.x = positionLocal.x / _terrainSize.x;
	uv.y = positionLocal.z / _terrainSize.z;

	half4 controlSample = SAMPLE_TEXTURE2D(_controlMap, sampler_controlMap, uv);

	half yPositionDifference = data.positionWS.y - controlSample.w * _terrainSize.y;

	half blendYRange = _blendRange;

	half t = saturate(yPositionDifference / blendYRange);

	if (t >= 1) return data.color;

	t /= clamp(noise(uv * _terrainSize * 2), 0.01, 1);
	t = saturate(t);

	t = pow(t, 2);

	//return float4(controlSample.rgb, 1);

	//float3x3 TBN;
	//ComputeTriplanar(data.normalWS, positionLocal, uv, TBN);

	uv.x = positionLocal.x;
	uv.y = positionLocal.z;

	half3 weights = normalize(controlSample.rgb);

	half4 mixedDiffuse = 0.0h;
    mixedDiffuse += SAMPLE_TEXTURE2D(_layer0, sampler_layer0, uv / _size0) * half4(weights.rrr, 1.0h);
    mixedDiffuse += SAMPLE_TEXTURE2D(_layer1, sampler_layer0, uv / _size1) * half4(weights.ggg, 1.0h);
    mixedDiffuse += SAMPLE_TEXTURE2D(_layer2, sampler_layer0, uv / _size2) * half4(weights.bbb, 1.0h);

	half3 mixedNormal = 0;
	NormalMapMix(uv, weights, mixedNormal);

	float3 bitangent = cross(half3(0,1,0), half3(0,0,1));
    half3x3 tangentToWorld = half3x3(half3(0,0,1), bitangent.xyz, half3(0,1,0));
	half3 mixedNormalWS = TransformTangentToWorld(mixedNormal, tangentToWorld);
	//inputData.normalWS = lerp(half3(0,1,0), mixedNormalWS, 1 - t * t * t);
	inputData.normalWS = mixedNormalWS;

	half4 terrainColor = UniversalFragmentPBR(inputData, mixedDiffuse, 0, /* specular */ half3(0.0h, 0.0h, 0.0h), 0, 1, /* emission */ half3(0, 0, 0), 1);

	//terrainColor += SampleTerrainLayer(SAMPLE_TEXTURE2D(_layer0, sampler_layer0, uv), _normal0, controlSample.r, data, inputData, t);
	//terrainColor += SampleTerrainLayer(SAMPLE_TEXTURE2D(_layer1, sampler_layer1, uv), _normal1, controlSample.g, data, inputData, t);
	//terrainColor += SampleTerrainLayer(SAMPLE_TEXTURE2D(_layer2, sampler_layer2, uv), _normal2, controlSample.b, data, inputData, t);

	return lerp(terrainColor, data.color, t);

}