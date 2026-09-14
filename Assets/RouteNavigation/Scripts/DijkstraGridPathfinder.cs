using System.Collections.Generic;
using UnityEngine;

namespace RouteNavigation
{
    /// <summary>
    /// Finds the shortest walkable path between two points with Dijkstra over a grid,
    /// restricted to a SUBSET region around the start and goal (so it never processes the
    /// whole map). A cell is blocked if a tall renderer (a building) covers it, detected
    /// from renderer bounds, so no NavMesh and no colliders are needed.
    ///
    /// Usage: add this component anywhere, tune the region/cell size, then call
    /// FindPath(start, goal) to get the route as a list of world points.
    /// </summary>
    public class DijkstraGridPathfinder : MonoBehaviour
    {
        [Header("Subset search region")]
        [Tooltip("Margin in metres added around the start-goal box to form the search region.")]
        public float regionMargin = 25f;
        [Tooltip("Grid cell size in metres. Larger = faster and coarser.")]
        public float cellSize = 2f;
        [Tooltip("Safety cap on total grid cells so a huge region can never hang.")]
        public int maxCells = 40000;

        [Header("Obstacle detection (buildings)")]
        [Tooltip("Renderers taller than this (metres) count as obstacles, so flat roads stay walkable.")]
        public float obstacleMinHeight = 3f;
        [Tooltip("Extra padding in metres around obstacle footprints so the path keeps clear of walls.")]
        public float obstaclePadding = 1f;

        /// <summary>Returns a shortest walkable route from start to goal, or null if none was found.</summary>
        public List<Vector3> FindPath(Vector3 start, Vector3 goal)
        {
            cellSize = Mathf.Max(0.1f, cellSize);   // guard against 0/negative, which would make grid dims NaN/huge
            float minX = Mathf.Min(start.x, goal.x) - regionMargin;
            float maxX = Mathf.Max(start.x, goal.x) + regionMargin;
            float minZ = Mathf.Min(start.z, goal.z) - regionMargin;
            float maxZ = Mathf.Max(start.z, goal.z) + regionMargin;
            float groundY = start.y;

            int cols = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / cellSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / cellSize));
            if ((long)cols * rows > maxCells)
            {
                Debug.LogError($"[Dijkstra] Region {cols}x{rows} exceeds maxCells {maxCells}. Increase cellSize or reduce regionMargin.");
                return null;
            }

            bool[] blocked = new bool[cols * rows];
            MarkObstacles(blocked, cols, rows, minX, minZ, maxX, maxZ);

            int startIdx = NearestWalkable(Col(start.x, minX), Row(start.z, minZ), blocked, cols, rows);
            int goalIdx = NearestWalkable(Col(goal.x, minX), Row(goal.z, minZ), blocked, cols, rows);
            if (startIdx < 0 || goalIdx < 0)
            {
                Debug.LogError("[Dijkstra] Start or goal has no walkable cell nearby in the region.");
                return null;
            }

            int[] prev = RunDijkstra(startIdx, goalIdx, blocked, cols, rows);
            if (prev == null)
            {
                Debug.LogWarning("[Dijkstra] No walkable path between start and goal inside the region.");
                return null;
            }

            var cells = new List<int>();
            for (int at = goalIdx; at != -1; at = prev[at]) cells.Add(at);
            cells.Reverse();

            var pts = new List<Vector3> { new Vector3(start.x, groundY, start.z) };
            foreach (int idx in cells) pts.Add(CellCenter(idx, cols, minX, minZ, groundY));
            pts.Add(new Vector3(goal.x, groundY, goal.z));
            return Simplify(pts);
        }

        private void MarkObstacles(bool[] blocked, int cols, int rows, float minX, float minZ, float maxX, float maxZ)
        {
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                Bounds b = r.bounds;
                if (b.size.y < obstacleMinHeight) continue;                          // flat = road, keep walkable
                if (b.max.x < minX || b.min.x > maxX || b.max.z < minZ || b.min.z > maxZ) continue; // outside region
                int c0 = Mathf.Clamp(Col(b.min.x - obstaclePadding, minX), 0, cols - 1);
                int c1 = Mathf.Clamp(Col(b.max.x + obstaclePadding, minX), 0, cols - 1);
                int r0 = Mathf.Clamp(Row(b.min.z - obstaclePadding, minZ), 0, rows - 1);
                int r1 = Mathf.Clamp(Row(b.max.z + obstaclePadding, minZ), 0, rows - 1);
                for (int cx = c0; cx <= c1; cx++)
                    for (int cz = r0; cz <= r1; cz++)
                        blocked[cz * cols + cx] = true;
            }
        }

        private int Col(float x, float minX) => Mathf.FloorToInt((x - minX) / cellSize);
        private int Row(float z, float minZ) => Mathf.FloorToInt((z - minZ) / cellSize);

        private Vector3 CellCenter(int idx, int cols, float minX, float minZ, float y)
        {
            int cx = idx % cols, cz = idx / cols;
            return new Vector3(minX + (cx + 0.5f) * cellSize, y, minZ + (cz + 0.5f) * cellSize);
        }

        private int NearestWalkable(int cx, int cz, bool[] blocked, int cols, int rows)
        {
            cx = Mathf.Clamp(cx, 0, cols - 1);
            cz = Mathf.Clamp(cz, 0, rows - 1);
            if (!blocked[cz * cols + cx]) return cz * cols + cx;
            int maxRad = Mathf.Max(cols, rows);
            for (int rad = 1; rad < maxRad; rad++)
            {
                for (int dx = -rad; dx <= rad; dx++)
                    for (int dz = -rad; dz <= rad; dz++)
                    {
                        if (Mathf.Abs(dx) != rad && Mathf.Abs(dz) != rad) continue; // ring only
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nx >= cols || nz < 0 || nz >= rows) continue;
                        if (!blocked[nz * cols + nx]) return nz * cols + nx;
                    }
            }
            return -1;
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private int[] RunDijkstra(int start, int goal, bool[] blocked, int cols, int rows)
        {
            int n = cols * rows;
            var dist = new float[n];
            var prev = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; prev[i] = -1; }
            dist[start] = 0f;

            var heap = new MinHeap(n);
            heap.Push(start, 0f);
            while (heap.Count > 0)
            {
                int u = heap.Pop(out float du);
                if (du > dist[u]) continue;      // stale
                if (u == goal) break;
                int ux = u % cols, uz = u / cols;
                for (int d = 0; d < 8; d++)
                {
                    int nx = ux + Dx[d], nz = uz + Dz[d];
                    if (nx < 0 || nx >= cols || nz < 0 || nz >= rows) continue;
                    int v = nz * cols + nx;
                    if (blocked[v]) continue;
                    float w = (Dx[d] != 0 && Dz[d] != 0) ? 1.41421356f : 1f;
                    float nd = dist[u] + w;
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; heap.Push(v, nd); }
                }
            }
            return float.IsInfinity(dist[goal]) ? null : prev;
        }

        /// <summary>Drops points that lie on a straight line, keeping only the corners.</summary>
        private static List<Vector3> Simplify(List<Vector3> pts)
        {
            if (pts.Count <= 2) return pts;
            var outPts = new List<Vector3> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = outPts[outPts.Count - 1];
                Vector3 d1 = pts[i] - a; d1.y = 0f;
                Vector3 d2 = pts[i + 1] - pts[i]; d2.y = 0f;
                if (d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f) continue;
                if (Vector3.Angle(d1, d2) > 5f) outPts.Add(pts[i]);
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }

        /// <summary>Minimal binary min-heap keyed by float priority (lazy-deletion friendly).</summary>
        private class MinHeap
        {
            private int[] _items;
            private float[] _prio;
            private int _count;

            public MinHeap(int capacity)
            {
                capacity = Mathf.Max(4, capacity);
                _items = new int[capacity + 1];
                _prio = new float[capacity + 1];
                _count = 0;
            }

            public int Count => _count;

            public void Push(int item, float priority)
            {
                if (_count + 1 >= _items.Length)
                {
                    System.Array.Resize(ref _items, _items.Length * 2);
                    System.Array.Resize(ref _prio, _prio.Length * 2);
                }
                _count++;
                _items[_count] = item;
                _prio[_count] = priority;
                int i = _count;
                while (i > 1 && _prio[i] < _prio[i / 2]) { Swap(i, i / 2); i /= 2; }
            }

            public int Pop(out float priority)
            {
                int top = _items[1];
                priority = _prio[1];
                _items[1] = _items[_count];
                _prio[1] = _prio[_count];
                _count--;
                int i = 1;
                while (true)
                {
                    int l = i * 2, r = i * 2 + 1, sm = i;
                    if (l <= _count && _prio[l] < _prio[sm]) sm = l;
                    if (r <= _count && _prio[r] < _prio[sm]) sm = r;
                    if (sm == i) break;
                    Swap(i, sm);
                    i = sm;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_prio[a], _prio[b]) = (_prio[b], _prio[a]);
            }
        }
    }
}
