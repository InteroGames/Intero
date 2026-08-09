using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Interrogation.Heist;

namespace Interrogation.EditorTools
{
    /// <summary>
    /// 잠입 씬이 실제로 걸어다닐 수 있는 모양인지 확인한다.
    ///
    /// 씬을 코드로 짓기 때문에 벽 하나가 문을 막아도 Play를 눌러보기 전에는 모른다.
    /// 여기서는 플레이어 캡슐 크기로 격자를 훑어 걸을 수 있는 칸을 찾고,
    /// 스폰 지점에서 물 흐르듯 퍼뜨려(flood fill) 목표물·인멸 지점·탈출구에
    /// 전부 닿는지 본다. 지도까지 찍어주므로 벽이 어디서 잘못됐는지 눈으로 보인다.
    /// </summary>
    public static class HeistNavCheck
    {
        // 플레이어 캡슐. HeistPlayer/CharacterController와 같은 값이어야 한다.
        const float Radius = 0.30f;   // 실제 0.32. 벽에 스치는 것까지 막힘으로 세지 않으려고 조금 줄였다.
        const float Height = 1.75f;
        const float EyeY = 1.62f;
        const float Reach = 2.8f;     // HeistDirector.interactRange

        const float Cell = 0.5f;
        const float MinX = -24f, MaxX = 29f, MinZ = -24f, MaxZ = 18f;

        [MenuItem("Tools/Interrogation/Check Heist Navigation")]
        public static void Check()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Heist.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();

            int nx = Mathf.RoundToInt((MaxX - MinX) / Cell);
            int nz = Mathf.RoundToInt((MaxZ - MinZ) / Cell);

            var walkable = new bool[nx, nz];
            int walkCount = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    if (Walkable(WorldOf(i, j))) { walkable[i, j] = true; walkCount++; }

            var player = Object.FindFirstObjectByType<HeistPlayer>();
            if (player == null) { Debug.LogError("[Nav] 플레이어가 씬에 없다."); return; }

            Vector3 spawn = player.transform.position;
            if (!Cellof(spawn, out int sx, out int sz))
            {
                Debug.LogError($"[Nav] 스폰 {spawn}이 격자 밖이다.");
                return;
            }

            // 스폰이 벽에 끼어 있으면 근처의 걸을 수 있는 칸으로 옮겨 시작한다.
            if (!walkable[sx, sz] && !NearestWalkable(walkable, sx, sz, out sx, out sz))
            {
                Debug.LogError($"[Nav] 스폰 {spawn} 주변에 설 수 있는 자리가 없다. 벽 안에 끼어 있다.");
                return;
            }

            var reached = Flood(walkable, sx, sz, out int reachCount);

            Debug.Log($"[Nav] 걸을 수 있는 칸 {walkCount}, 스폰에서 닿는 칸 {reachCount} " +
                      $"({100f * reachCount / Mathf.Max(1, walkCount):0}%)");

            // 목표물·인멸·탈출구에 전부 닿는가.
            var problems = new List<string>();
            foreach (var it in Object.FindObjectsByType<HeistInteractable>(FindObjectsSortMode.None))
            {
                if (Reachable(it, reached, out float dist))
                    Debug.Log($"[Nav] OK   {it.kind,-8} {it.label,-10} 접근 가능 (가장 가까운 자리 {dist:0.0}m)");
                else
                    problems.Add($"{it.kind} \"{it.label}\" 에 닿을 수 없다. {it.transform.position}");
            }

            DumpMap(walkable, reached, sx, sz);

            if (problems.Count == 0)
                Debug.Log("[Nav] 벽 이상 없음. 스폰에서 모든 지점에 걸어서 닿는다.");
            else
                foreach (var p in problems) Debug.LogError($"[Nav] {p}");
        }

        // ---------------------------------------------------------------- 격자

        static Vector3 WorldOf(int i, int j) => new Vector3(MinX + i * Cell, 0f, MinZ + j * Cell);

        static bool Cellof(Vector3 p, out int i, out int j)
        {
            i = Mathf.RoundToInt((p.x - MinX) / Cell);
            j = Mathf.RoundToInt((p.z - MinZ) / Cell);
            return i >= 0 && j >= 0 && i < Mathf.RoundToInt((MaxX - MinX) / Cell)
                                    && j < Mathf.RoundToInt((MaxZ - MinZ) / Cell);
        }

        /// <summary>이 자리에 사람이 설 수 있는가. 발밑에 바닥이 있고, 몸이 아무데도 안 낀다.</summary>
        static bool Walkable(Vector3 foot)
        {
            // 발밑에 바닥이 있어야 한다.
            if (!Physics.Raycast(foot + Vector3.up * 0.6f, Vector3.down, out RaycastHit ground, 1.2f,
                                 ~0, QueryTriggerInteraction.Ignore))
                return false;

            Vector3 stand = new Vector3(foot.x, ground.point.y, foot.z);

            var p0 = stand + Vector3.up * (Radius + 0.05f);
            var p1 = stand + Vector3.up * (Height - Radius);
            return !Physics.CheckCapsule(p0, p1, Radius, ~0, QueryTriggerInteraction.Ignore);
        }

        static bool NearestWalkable(bool[,] walkable, int sx, int sz, out int ox, out int oz)
        {
            for (int r = 1; r <= 8; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        int x = sx + dx, z = sz + dz;
                        if (x < 0 || z < 0 || x >= walkable.GetLength(0) || z >= walkable.GetLength(1)) continue;
                        if (walkable[x, z]) { ox = x; oz = z; return true; }
                    }
            ox = sx; oz = sz;
            return false;
        }

        static bool[,] Flood(bool[,] walkable, int sx, int sz, out int count)
        {
            int nx = walkable.GetLength(0), nz = walkable.GetLength(1);
            var seen = new bool[nx, nz];
            var q = new Queue<Vector2Int>();
            q.Enqueue(new Vector2Int(sx, sz));
            seen[sx, sz] = true;
            count = 1;

            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in dirs)
                {
                    int x = c.x + d.x, z = c.y + d.y;
                    if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                    if (seen[x, z] || !walkable[x, z]) continue;
                    seen[x, z] = true;
                    count++;
                    q.Enqueue(new Vector2Int(x, z));
                }
            }
            return seen;
        }

        /// <summary>
        /// 닿는 자리에 서서 이걸 실제로 조준할 수 있는가.
        /// HeistDirector가 하는 것과 같은 방식으로 눈높이에서 광선을 쏜다.
        /// </summary>
        static bool Reachable(HeistInteractable it, bool[,] reached, out float best)
        {
            best = float.MaxValue;
            var col = it.GetComponentInChildren<Collider>();
            if (col == null) return false;

            Vector3 target = col.bounds.center;
            int nx = reached.GetLength(0), nz = reached.GetLength(1);

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!reached[i, j]) continue;

                    var foot = WorldOf(i, j);
                    var eye = new Vector3(foot.x, EyeY, foot.z);
                    float d = Vector3.Distance(eye, target);
                    if (d > Reach || d >= best) continue;

                    if (Physics.Raycast(eye, (target - eye).normalized, out RaycastHit hit, Reach,
                                        ~0, QueryTriggerInteraction.Ignore)
                        && hit.collider.GetComponentInParent<HeistInteractable>() == it)
                        best = d;
                }

            return best < float.MaxValue;
        }

        // ---------------------------------------------------------------- 지도

        /// <summary>
        /// 격자 그대로(한 칸 0.5m) 찍는다. 줄여 찍으면 두께 0.25m짜리 벽이
        /// 이웃 칸에 묻혀 사라지고, 벽이 있는지 없는지를 확인할 수가 없다.
        ///   ·  걸을 수 있고 스폰에서 닿는다
        ///   x  걸을 수는 있는데 닿지 않는다 (막힌 구역)
        ///   #  벽이나 물건, 또는 바닥 없음
        ///   S  스폰   L 목표물   B 인멸   E 탈출구
        /// </summary>
        static void DumpMap(bool[,] walkable, bool[,] reached, int sx, int sz)
        {
            int nx = walkable.GetLength(0), nz = walkable.GetLength(1);
            var marks = new Dictionary<(int, int), char>();

            foreach (var it in Object.FindObjectsByType<HeistInteractable>(FindObjectsSortMode.None))
            {
                if (!Cellof(it.transform.position, out int i, out int j)) continue;
                marks[(i, j)] = it.kind == InteractKind.Loot ? 'L'
                              : it.kind == InteractKind.Conceal ? 'B' : 'E';
            }
            marks[(sx, sz)] = 'S';

            var sb = new StringBuilder("[Nav] 지도 (위가 +Z / 오른쪽이 +X, 한 칸 0.5m)\n");
            for (int j = nz - 1; j >= 0; j--)
            {
                sb.Append($"{MinZ + j * Cell,6:0.0} ");
                for (int i = 0; i < nx; i++)
                {
                    if (marks.TryGetValue((i, j), out char m)) { sb.Append(m); continue; }
                    sb.Append(reached[i, j] ? '·' : walkable[i, j] ? 'x' : '#');
                }
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
        }
    }
}
