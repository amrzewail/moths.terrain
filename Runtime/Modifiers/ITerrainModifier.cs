using Unity.Collections;
using UnityEngine;

namespace Moths.Terrain.Modifiers
{
    public interface ITerrainModifier
    {
        bool IsActive { get; }

        bool IsApplyRequired();
        void ApplyModifier(UnmanagedTerrainData data, NativeList<TreeInstance> treeInstances);
    }
}