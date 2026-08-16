using System;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;

namespace CS2MCP
{
    /// <summary>
    /// Classifies the operational-area templates declared by a building
    /// prefab. This is prefab identity only; placement and runtime ownership
    /// remain native-validation and placed-entity concerns.
    /// </summary>
    internal sealed class PrefabOperationalAreaClassifier
    {
        private readonly EntityManager m_EntityManager;

        public PrefabOperationalAreaClassifier(EntityManager entityManager)
        {
            m_EntityManager = entityManager;
        }

        public bool DeclaresExtractorArea(Entity buildingPrefab)
        {
            if (!m_EntityManager.Exists(buildingPrefab)
                || !m_EntityManager.HasBuffer<Game.Prefabs.SubArea>(buildingPrefab))
            {
                return false;
            }
            DynamicBuffer<Game.Prefabs.SubArea> subAreas =
                m_EntityManager.GetBuffer<Game.Prefabs.SubArea>(buildingPrefab, isReadOnly: true);
            foreach (Game.Prefabs.SubArea subArea in subAreas)
            {
                Entity root = subArea.m_Prefab;
                if (PrefabOperationalAreaGraph.GuaranteesExtractorArea(
                    root,
                    HasExtractorAreaData,
                    GetPlaceholderCandidates))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasExtractorAreaData(Entity areaPrefab)
        {
            return areaPrefab != Entity.Null
                && m_EntityManager.Exists(areaPrefab)
                && m_EntityManager.HasComponent<ExtractorAreaData>(areaPrefab)
                && m_EntityManager.HasComponent<AreaGeometryData>(areaPrefab)
                && m_EntityManager.GetComponentData<AreaGeometryData>(areaPrefab).m_Type
                    == Game.Areas.AreaType.Lot;
        }

        private IReadOnlyList<Entity> GetPlaceholderCandidates(Entity areaPrefab)
        {
            if (areaPrefab == Entity.Null
                || !m_EntityManager.Exists(areaPrefab)
                || !m_EntityManager.HasBuffer<PlaceholderObjectElement>(areaPrefab))
            {
                return Array.Empty<Entity>();
            }
            DynamicBuffer<PlaceholderObjectElement> candidates =
                m_EntityManager.GetBuffer<PlaceholderObjectElement>(areaPrefab, isReadOnly: true);
            var result = new Entity[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                result[i] = candidates[i].m_Object;
            }
            return result;
        }
    }
}
