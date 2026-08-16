using System;
using System.Collections.Generic;

namespace CS2MCP
{
    internal static class PrefabOperationalAreaGraph
    {
        public static bool GuaranteesExtractorArea<TNode>(
            TNode root,
            Func<TNode, bool> isExtractorArea,
            Func<TNode, IReadOnlyList<TNode>> getPlaceholderCandidates)
        {
            return GuaranteesExtractorArea(
                root,
                isExtractorArea,
                getPlaceholderCandidates,
                new HashSet<TNode>());
        }

        private static bool GuaranteesExtractorArea<TNode>(
            TNode node,
            Func<TNode, bool> isExtractorArea,
            Func<TNode, IReadOnlyList<TNode>> getPlaceholderCandidates,
            HashSet<TNode> visited)
        {
            if (!visited.Add(node))
            {
                return false;
            }
            try
            {
                if (isExtractorArea(node))
                {
                    return true;
                }
                IReadOnlyList<TNode> candidates = getPlaceholderCandidates(node);
                if (candidates == null || candidates.Count == 0)
                {
                    return false;
                }
                foreach (TNode candidate in candidates)
                {
                    if (!GuaranteesExtractorArea(
                        candidate,
                        isExtractorArea,
                        getPlaceholderCandidates,
                        visited))
                    {
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                visited.Remove(node);
            }
        }
    }
}
