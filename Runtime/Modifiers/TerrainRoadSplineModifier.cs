using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Moths.Terrain.Modifiers
{
    [RequireComponent(typeof(SplineContainer))]
    public class TerrainRoadSplineModifier : MonoBehaviour, ITerrainModifier
    {
        [BurstCompile]
        private struct CalculateNearestSplinePoints : IJobParallelFor
        {
            public UnmanagedTerrainData TerrainData;
            [ReadOnly]
            public NativeArray<float3> SplinePositions;
            [ReadOnly]
            public NativeArray<float3> SplineTangents;
            [ReadOnly]
            public NativeHashSet<int> SplineEnds;
            public NativeArray<float3> NearestSplinePositions; 
            public NativeArray<float3> NearestSplineTangents;
            public NativeArray<float> NearestSplineRandomness;

            public void Execute(int heightmapIndex)
            {
                float3 worldPosition = TerrainData.GetWorldPositionAtHeightmapIndex(heightmapIndex, TerrainData.Heightmap);
                SampleNearestSplinePoint(worldPosition, out float3 splinePosition, out float3 splineTangent, out float randomness);

                NearestSplinePositions[heightmapIndex] = splinePosition;
                NearestSplineTangents[heightmapIndex] = splineTangent;
                NearestSplineRandomness[heightmapIndex] = randomness;
            }

            private float3 SampleNearestSplinePoint(float3 worldPosition, out float3 position, out float3 tangent, out float randomness)
            {
                int count = SplinePositions.Length;

                float nearestDistance = float.MaxValue;
                int nearestIndex = 0;

                // Find nearest spline point.
                for (int i = 0; i < count; i++)
                {
                    float distance = math.distancesq(worldPosition, SplinePositions[i]);

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = i;
                    }
                }

                // Test the two adjacent segments.
                float3 bestPosition = SplinePositions[nearestIndex];
                float3 bestTangent = SplineTangents[nearestIndex];
                float bestDistance = nearestDistance;
                float bestRandomness = GetRandomness(nearestIndex);

                int sampleLength = 2;

                if (SplineEnds.Contains(nearestIndex)) sampleLength = 1;

                for (int i = 0; i < sampleLength; i++)
                {
                    int a = nearestIndex + i - 1;
                    int b = a + 1;

                    if (a < 0 || b >= count)
                        continue;

                    float3 start = SplinePositions[a];
                    float3 end = SplinePositions[b];

                    float3 segment = end - start;
                    float lengthSq = math.lengthsq(segment);

                    float t = lengthSq > 0f
                        ? math.saturate(math.dot(worldPosition - start, segment) / lengthSq)
                        : 0f;

                    float3 candidate = start + segment * t;
                    float distance = math.distancesq(worldPosition, candidate);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestPosition = candidate;
                        bestTangent = math.normalizesafe(math.lerp(SplineTangents[a], SplineTangents[b], t));
                        bestRandomness = math.lerp(GetRandomness(a), GetRandomness(b), t);
                    }
                }

                position = bestPosition;
                tangent = bestTangent;
                randomness = bestRandomness;

                return bestPosition;
            }

            private static float GetRandomness(int index)
            {
                uint hash = (uint)index;

                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;

                return (hash / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        [BurstCompile]
        private struct HeightMapJob : IJobParallelFor
        {
            public UnmanagedTerrainData TerrainData;
            [ReadOnly]
            public NativeArray<float3> NearestSplinePositions; 
            
            [ReadOnly]
            public NativeArray<float> NearestSplineRandomness;


            public float RoadWidth;
            public float RoadDepth;
            public float RoadWidthRandomization;

            public float MidwayWidth;
            public float MidwayHeight;

            public void Execute(int index)
            {
                float3 worldPosition = TerrainData.GetWorldPositionAtHeightmapIndex(index, TerrainData.Heightmap);
                float3 splinePosition = NearestSplinePositions[index];

                worldPosition.y = 0;
                splinePosition.y = 0;

                float distancesq = math.distancesq(worldPosition, splinePosition);

                float distance = math.sqrt(distancesq);

                float random = 1f + NearestSplineRandomness[index] * RoadWidthRandomization;

                float roadWidth = RoadWidth * random;
                float midwayWidth = MidwayWidth * random;

                float currentHeight = TerrainData.Heightmap[index];
                float roadHeight = currentHeight - RoadDepth / TerrainData.Size.y;

                float roadBlend = 1f - math.smoothstep(roadWidth * 0.8f, roadWidth, distance);

                currentHeight = math.lerp(currentHeight, roadHeight, roadBlend);

                float midwayBlend = 1f - math.smoothstep(midwayWidth * 0.8f, midwayWidth, distance);

                currentHeight += MidwayHeight / TerrainData.Size.y * midwayBlend;

                TerrainData.Heightmap[index] = currentHeight;
            }

        }

        [BurstCompile]
        private struct AlphamapJob : IJobParallelFor
        {
            public UnmanagedTerrainData TerrainData;

            public int RoadLayer;
            public float RoadWidth;
            public float RoadWidthRandomization;

            public int MidwayLayer;
            public float MidwayWidth;

            public bool AddTrack;
            public int TrackLayer;
            public float TrackDistance;
            public float TrackWidth;
            public float TrackWeightRandomization;

            [ReadOnly]
            public NativeArray<float3> NearestSplinePositions;

            [ReadOnly]
            public NativeArray<float> NearestSplineRandomness;

            public void Execute(int alphamapIndex)
            {
                int heightmapIndex = TerrainData.AlphamapIndexToHeightmapIndex(alphamapIndex);
                float3 worldPosition = TerrainData.GetWorldPositionAtHeightmapIndex(heightmapIndex);
                float3 splinePosition = NearestSplinePositions[heightmapIndex];

                worldPosition.y = 0;
                splinePosition.y = 0;

                float distancesq = math.distancesq(worldPosition, splinePosition);
                float rawRandom = NearestSplineRandomness[heightmapIndex];
                float widthRandom = 1f + rawRandom * RoadWidthRandomization;

                float width = RoadWidth * widthRandom;

                if (distancesq < width * width)
                {
                    float weight = 1f - math.saturate(distancesq / (width * width));
                    if (TerrainData.AlphamapIndexToLayerIndex(alphamapIndex) == RoadLayer) weight = math.max(TerrainData.Alphamaps[alphamapIndex], weight);
                    TerrainData.SetAlphamapLayerWeight(alphamapIndex, RoadLayer, weight);
                }

                width = MidwayWidth * widthRandom;

                if (distancesq < width * width)
                {
                    float weight = 1f - math.saturate(distancesq / (width * width));
                    if (TerrainData.AlphamapIndexToLayerIndex(alphamapIndex) == MidwayLayer) weight = math.max(TerrainData.Alphamaps[alphamapIndex], weight);
                    TerrainData.SetAlphamapLayerWeight(alphamapIndex, MidwayLayer, weight);
                }

                if (AddTrack && TrackWidth > 0f)
                {
                    float distance = math.sqrt(distancesq);
                    float innerEdge = TrackDistance * widthRandom;
                    float bandWidth = TrackWidth * widthRandom;
                    float outerEdge = innerEdge + bandWidth;

                    if (distance > innerEdge && distance < outerEdge)
                    {
                        float halfWidth = bandWidth * 0.5f;
                        float center = innerEdge + halfWidth;
                        float normalizedOffset = math.abs(distance - center) / halfWidth; // 0 at track center, 1 at edges
                        float falloff = 1f - math.smoothstep(0f, 1f, normalizedOffset);

                        float random01 = 0.5f + 0.5f * rawRandom;
                        float weightScale = math.lerp(1f - TrackWeightRandomization, 1f, random01);
                        float weight = falloff * weightScale;

                        if (TerrainData.AlphamapIndexToLayerIndex(alphamapIndex) == TrackLayer) weight = math.max(TerrainData.Alphamaps[alphamapIndex], weight);
                        TerrainData.SetAlphamapLayerWeight(alphamapIndex, TrackLayer, weight);
                    }
                }
            }
        }

        public bool IsActive => this.isActiveAndEnabled;

        public SplineContainer Container => _splineContainer ? _splineContainer : (_splineContainer = GetComponent<SplineContainer>());

        [SerializeField, Range(0.01f, 1f)] float _splineSamplePrecision = 0.05f;

        [Header("Road")]
        [SerializeField] float _roadWidth = 2f;
        [SerializeField, Range(0f, 1f)] float _roadWidthRandomization = 0.2f; 
        [SerializeField] float _roadDepth = 0.25f;
        [SerializeField] int _roadLayerIndex = 0;

        [Header("Midway")]
        [SerializeField] float _midwayWidth = 0.5f;
        [SerializeField] float _midwayHeight = 0.125f;
        [SerializeField] int _midwayLayerIndex = 0;

        [Header("Track")]
        [SerializeField] bool _addTrack = false;
        [SerializeField] int _trackLayerIndex = 0;
        [SerializeField] float _trackDistance = 0.6f;
        [SerializeField] float _trackWidth = 0.5f;
        [SerializeField, Range(0f, 1f)] float _trackWeightRandomization = 0.3f;

        [Header("Trees")]
        [SerializeField] bool _addTrees = false;
        [SerializeField] int[] _treePrototypeIndices = new[] { 0 };
        [SerializeField, Range(0.01f, 5)] float _treeSpacing = 0.5f;
        [SerializeField, Range(0, 1)] float _treeSpacingRandomization = 0.2f;

        private SplineContainer _splineContainer;
        private int _applyFrame;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private int _lastSplineHash;

        private float _lastSplineSamplePrecision;
        private float _lastRoadWidth;
        private float _lastRoadWidthRandomization;
        private float _lastRoadDepth;
        private int _lastRoadLayerIndex;
        private float _lastMidwayWidth;
        private float _lastMidwayHeight;
        private int _lastMidwayLayerIndex;
        private bool _lastAddTrack;
        private int _lastTrackLayerIndex;
        private float _lastTrackDistance;
        private float _lastTrackWidth;
        private float _lastTrackWeightRandomization;

        private bool _lastAddTrees;
        private int _lastTreePrototypeIndicesHash;
        private float _lastTreeSpacing;
        private float _lastTreeSpacingRandomization;

        public void ApplyModifier(UnmanagedTerrainData data, NativeList<TreeInstance> treeInstances)
        {
            _applyFrame = Time.frameCount;
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;

            _lastSplineSamplePrecision = _splineSamplePrecision;
            _lastRoadWidth = _roadWidth;
            _lastRoadWidthRandomization = _roadWidthRandomization;
            _lastRoadDepth = _roadDepth;
            _lastRoadLayerIndex = _roadLayerIndex;

            _lastMidwayWidth = _midwayWidth;
            _lastMidwayHeight = _midwayHeight;
            _lastMidwayLayerIndex = _midwayLayerIndex;

            _lastAddTrack = _addTrack;
            _lastTrackLayerIndex = _trackLayerIndex;
            _lastTrackDistance = _trackDistance;
            _lastTrackWidth = _trackWidth;
            _lastTrackWeightRandomization = _trackWeightRandomization;

            _lastAddTrees = _addTrees;
            _lastTreePrototypeIndicesHash = ComputeIndicesHash(_treePrototypeIndices);
            _lastTreeSpacing = _treeSpacing;
            _lastTreeSpacingRandomization = _treeSpacingRandomization;

            var splines = Container.Splines;

            // Calculate the total number of samples across all splines.
            int sampleCount = 0;

            foreach (Spline spline in splines)
            {
                for (float t = 0; t <= 1f; t += _splineSamplePrecision)
                    sampleCount++;
            }

            NativeArray<float3> splinePositions = new(sampleCount, Allocator.TempJob);
            NativeArray<float3> splineTangents = new(sampleCount, Allocator.TempJob);
            NativeHashSet<int> splineEnds = new(splines.Count, Allocator.TempJob);

            int index = 0;
            Transform containerTransform = Container.transform;

            foreach (Spline spline in splines)
            {
                for (float t = 0; t <= 1f; t += _splineSamplePrecision)
                {
                    SplineUtility.Evaluate(spline, t, out float3 position, out float3 tangent, out _);

                    splinePositions[index] = containerTransform.TransformPoint(position);
                    splineTangents[index] = containerTransform.TransformDirection(tangent);

                    index++;
                }

                splineEnds.Add(index - 1);
            }

            CalculateNearestSplinePoints calculateSplinePointsJob = new()
            {
                TerrainData = data,
                SplinePositions = splinePositions,
                SplineTangents = splineTangents,
                SplineEnds = splineEnds,
                NearestSplinePositions = new(data.Heightmap.Length, Allocator.TempJob),
                NearestSplineTangents = new(data.Heightmap.Length, Allocator.TempJob),
                NearestSplineRandomness = new(data.Heightmap.Length, Allocator.TempJob),
            };
            var handle = calculateSplinePointsJob.Schedule(data.Heightmap.Length, 32);
            handle.Complete();

            HeightMapJob heightmapJob = new()
            {
                TerrainData = data,

                RoadWidth = _roadWidth,
                RoadDepth = _roadDepth,
                RoadWidthRandomization = _roadWidthRandomization,

                MidwayWidth = _midwayWidth,
                MidwayHeight = _midwayHeight,

                NearestSplinePositions = calculateSplinePointsJob.NearestSplinePositions,
                NearestSplineRandomness = calculateSplinePointsJob.NearestSplineRandomness,
            };

            handle = heightmapJob.Schedule(data.Heightmap.Length, 32);
            handle.Complete();

            AlphamapJob alphamapJob = new()
            {
                TerrainData = data,
                NearestSplinePositions = calculateSplinePointsJob.NearestSplinePositions,
                NearestSplineRandomness = calculateSplinePointsJob.NearestSplineRandomness,

                RoadWidth = _roadWidth,
                RoadLayer = _roadLayerIndex,
                RoadWidthRandomization = _roadWidthRandomization,

                MidwayWidth = _midwayWidth,
                MidwayLayer = _midwayLayerIndex,

                AddTrack = _addTrack,
                TrackLayer = _trackLayerIndex,
                TrackDistance = _trackDistance,
                TrackWidth = _trackWidth,
                TrackWeightRandomization = _trackWeightRandomization,
            };

            handle = alphamapJob.Schedule(data.Alphamaps.Length, 32);
            handle.Complete();

            if (_addTrees)
            {
                PlaceTrees(data, splinePositions, splineTangents, splineEnds, treeInstances);
            }

            splinePositions.Dispose();
            splineTangents.Dispose();
            splineEnds.Dispose();

            calculateSplinePointsJob.NearestSplinePositions.Dispose();
            calculateSplinePointsJob.NearestSplineTangents.Dispose();
            calculateSplinePointsJob.NearestSplineRandomness.Dispose();
        }

        private void PlaceTrees(UnmanagedTerrainData data, NativeArray<float3> splinePositions, NativeArray<float3> splineTangents, NativeHashSet<int> splineEnds, NativeList<TreeInstance> treeInstances)
        {
            if (_treePrototypeIndices == null || _treePrototypeIndices.Length == 0 || data.TreePrototypes <= 0)
                return;

            uint randomState = 12345;
            var random = new Unity.Mathematics.Random(randomState);

            float nextSpacing = _treeSpacing * random.NextFloat(1f - _treeSpacingRandomization, 1f + _treeSpacingRandomization);
            float distance = 0f;

            for (int i = 1; i < splinePositions.Length; i++)
            {
                float3 start = splinePositions[i - 1];
                float3 end = splinePositions[i];

                if (splineEnds.Contains(i - 1)) continue;

                float segmentLength = math.distance(start, end);

                while (distance + segmentLength >= nextSpacing)
                {
                    float t = (nextSpacing - distance) / segmentLength;
                    float3 position = math.lerp(start, end, t);

                    // Note: bounds check uses position.z against data.Position.z + data.Size.z
                    if (position.x >= data.Position.x && position.x <= data.Position.x + data.Size.x &&
                        position.z >= data.Position.z && position.z <= data.Position.z + data.Size.z)
                    {
                        float scale = random.NextFloat(0.85f, 1.15f);
                        int rawPrototypeIndex = _treePrototypeIndices[random.NextInt(0, _treePrototypeIndices.Length)];
                        int prototypeIndex = math.clamp(rawPrototypeIndex, 0, data.TreePrototypes - 1);

                        treeInstances.Add(new TreeInstance
                        {
                            prototypeIndex = prototypeIndex,
                            position = data.WorldToLocationPosition(position),
                            widthScale = scale,
                            heightScale = scale,
                            rotation = random.NextFloat(0f, math.PI * 2f),
                            color = Color.white,
                            lightmapColor = Color.white
                        });
                    }

                    segmentLength -= nextSpacing - distance;
                    start = position;
                    distance = 0f;

                    nextSpacing = _treeSpacing * random.NextFloat(
                        1f - _treeSpacingRandomization,
                        1f + _treeSpacingRandomization);
                }

                distance += segmentLength;
            }
        }

        public bool IsApplyRequired()
        {
            return _applyFrame == Time.frameCount || _lastPosition != transform.position || _lastRotation != transform.rotation || AreSettingsChanged() || IsSplineChanged();
        }

        private bool IsSplineChanged()
        {
            var splines = Container.Splines;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + splines.Count;

                foreach (Spline spline in splines)
                {
                    hash = hash * 31 + spline.Count;
                    hash = hash * 31 + spline.Closed.GetHashCode();

                    for (int i = 0; i < spline.Count; i++)
                    {
                        BezierKnot knot = spline[i];

                        hash = hash * 31 + knot.Position.GetHashCode();
                        hash = hash * 31 + knot.TangentIn.GetHashCode();
                        hash = hash * 31 + knot.TangentOut.GetHashCode();
                        hash = hash * 31 + knot.Rotation.GetHashCode();
                    }
                }

                if (hash != _lastSplineHash)
                {
                    _lastSplineHash = hash;
                    return true;
                }
            }

            return false;
        }

        private static int ComputeIndicesHash(int[] indices)
        {
            if (indices == null) return 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + indices.Length;
                for (int i = 0; i < indices.Length; i++)
                {
                    hash = hash * 31 + indices[i];
                }
                return hash;
            }
        }

        private bool AreSettingsChanged()
        {
            return
                _lastSplineSamplePrecision != _splineSamplePrecision ||
                _lastRoadWidth != _roadWidth ||
                _lastRoadWidthRandomization != _roadWidthRandomization ||
                _lastRoadDepth != _roadDepth ||
                _lastRoadLayerIndex != _roadLayerIndex ||
                _lastMidwayWidth != _midwayWidth ||
                _lastMidwayHeight != _midwayHeight ||
                _lastMidwayLayerIndex != _midwayLayerIndex ||
                _lastAddTrack != _addTrack ||
                _lastTrackLayerIndex != _trackLayerIndex ||
                _lastTrackDistance != _trackDistance ||
                _lastTrackWidth != _trackWidth ||
                _lastTrackWeightRandomization != _trackWeightRandomization ||
                _lastAddTrees != _addTrees ||
                _lastTreePrototypeIndicesHash != ComputeIndicesHash(_treePrototypeIndices) ||
                _lastTreeSpacing != _treeSpacing ||
                _lastTreeSpacingRandomization != _treeSpacingRandomization;

        }

    }
}