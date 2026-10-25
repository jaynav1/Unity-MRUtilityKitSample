using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Network;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRUKSpectator.Visualization
{
    public class SpectatorRoomManager : MonoBehaviour
    {
        public static SpectatorRoomManager Instance { get; private set; }

        [Header("Room Appearance")]
        [SerializeField] private bool _hideCeilingByDefault = true;
        [SerializeField] private Color _mrukBlueColor = new Color(0.143f, 0.690f, 0.868f, 1f);

        [Header("Player Tracking")]
        [SerializeField] private GameObject _playerAvatarPrefab;

        private readonly Dictionary<string, PlayerAvatarMarker> _players = new();
        private bool _isCeilingHidden = true;
        private Bounds _roomBounds;
        private readonly List<GameObject> _ceilingObjects = new();

        public bool HasRoomLoaded { get; private set; }
        public int AnchorCount { get; private set; }
        public Vector3 RoomSize => _roomBounds.size;
        public bool IsCeilingHidden => _isCeilingHidden;

        private void Awake()
        {
            Instance = this;
            _isCeilingHidden = _hideCeilingByDefault;
        }

        private void Start()
        {
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnRoomJsonReceived += HandleRoomJsonReceived;
                SpectatorServer.Instance.OnPoseReceived += HandlePoseReceived;
            }
        }

        private void OnDestroy()
        {
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnRoomJsonReceived -= HandleRoomJsonReceived;
                SpectatorServer.Instance.OnPoseReceived -= HandlePoseReceived;
            }
        }

        public async void HandleRoomJsonReceived(string json)
        {
            Debug.Log($"[SpectatorRoomManager] Loading received room JSON ({json.Length} chars)...");
            if (MRUK.Instance == null)
            {
                Debug.LogError("[SpectatorRoomManager] MRUK instance not found in scene!");
                return;
            }

            try
            {
                await MRUK.Instance.LoadSceneFromJsonString(json);
                HasRoomLoaded = true;

                PostProcessRoom();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorRoomManager] Failed to load room JSON: {ex.Message}");
            }
        }

        public void PostProcessRoom()
        {
            MRUKRoom room = MRUK.Instance.GetCurrentRoom();
            if (room == null && MRUK.Instance.Rooms.Count > 0)
            {
                room = MRUK.Instance.Rooms[0];
            }

            if (room == null)
            {
                Debug.LogWarning("[SpectatorRoomManager] No room found after loading JSON.");
                return;
            }

            AnchorCount = room.Anchors.Count;
            GenerateRoomMeshes(room);
            CalculateRoomBounds(room);

            // Auto-frame camera to fit the room in 3D orbit view
            if (OrbitCameraController.Instance != null)
            {
                OrbitCameraController.Instance.FocusOnBounds(_roomBounds);
            }

            Debug.Log($"[SpectatorRoomManager] Room post-processed: {AnchorCount} anchors rendered, size: {_roomBounds.size}");
        }

        public void ApplyRoomStyling(MRUKRoom room)
        {
            GenerateRoomMeshes(room);
        }

        private void GenerateRoomMeshes(MRUKRoom room)
        {
            _ceilingObjects.Clear();
            Shader mrukShader = Shader.Find("MixedReality/SceneMeshDefault");
            Material blueMaterial;
            if (mrukShader != null)
            {
                blueMaterial = new Material(mrukShader);
                blueMaterial.SetColor("_Color", _mrukBlueColor);
                blueMaterial.SetFloat("_Angle", 60f);
            }
            else
            {
                Shader fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                blueMaterial = new Material(fallbackShader);
                blueMaterial.color = _mrukBlueColor;
                if (blueMaterial.HasProperty("_BaseColor")) blueMaterial.SetColor("_BaseColor", _mrukBlueColor);
                if (blueMaterial.HasProperty("_Cull")) blueMaterial.SetFloat("_Cull", (float)CullMode.Off);
            }

            foreach (var anchor in room.Anchors)
            {
                Transform visualTransform = anchor.transform.Find("SpectatorVisual");
                GameObject visualGo;
                if (visualTransform != null)
                {
                    visualGo = visualTransform.gameObject;
                }
                else
                {
                    visualGo = new GameObject("SpectatorVisual");
                    visualGo.transform.SetParent(anchor.transform, false);
                }

                // Check or create MeshFilter
                if (!visualGo.TryGetComponent(out MeshFilter meshFilter))
                {
                    meshFilter = visualGo.AddComponent<MeshFilter>();
                }

                // Check or create MeshRenderer
                if (!visualGo.TryGetComponent(out MeshRenderer meshRenderer))
                {
                    meshRenderer = visualGo.AddComponent<MeshRenderer>();
                }

                Mesh mesh = null;

                // Procedural 3D Mesh Generation
                if (anchor.VolumeBounds.HasValue)
                {
                    mesh = CreateBoxMesh(anchor.VolumeBounds.Value);
                }
                else if (anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3)
                {
                    mesh = CreatePolygonMesh(anchor.PlaneBoundary2D);
                }
                else if (anchor.PlaneRect.HasValue)
                {
                    mesh = CreatePlaneMesh(anchor.PlaneRect.Value);
                }
                else if (anchor.GlobalMesh != null)
                {
                    mesh = anchor.GlobalMesh;
                }

                if (mesh != null)
                {
                    meshFilter.sharedMesh = mesh;
                }

                bool isCeiling = anchor.HasAnyLabel(MRUKAnchor.SceneLabels.CEILING);
                if (isCeiling)
                {
                    _ceilingObjects.Add(visualGo);
                    visualGo.SetActive(!_isCeilingHidden);
                }

                meshRenderer.sharedMaterial = blueMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        private static Mesh CreateBoxMesh(Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 ext = bounds.extents;

            Vector3[] vertices = new Vector3[]
            {
                // Bottom
                c + new Vector3(-ext.x, -ext.y, -ext.z),
                c + new Vector3( ext.x, -ext.y, -ext.z),
                c + new Vector3( ext.x, -ext.y,  ext.z),
                c + new Vector3(-ext.x, -ext.y,  ext.z),
                // Top
                c + new Vector3(-ext.x,  ext.y, -ext.z),
                c + new Vector3( ext.x,  ext.y, -ext.z),
                c + new Vector3( ext.x,  ext.y,  ext.z),
                c + new Vector3(-ext.x,  ext.y,  ext.z),
            };

            int[] triangles = new int[]
            {
                // Bottom
                0, 2, 1, 0, 3, 2,
                // Top
                4, 5, 6, 4, 6, 7,
                // Front
                0, 1, 5, 0, 5, 4,
                // Back
                2, 3, 7, 2, 7, 6,
                // Left
                3, 0, 4, 3, 4, 7,
                // Right
                1, 2, 6, 1, 6, 5
            };

            Mesh mesh = new Mesh
            {
                name = "BoxVisualMesh",
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePlaneMesh(Rect rect)
        {
            float x0 = rect.xMin, x1 = rect.xMax;
            float y0 = rect.yMin, y1 = rect.yMax;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(x0, y0, 0f),
                new Vector3(x1, y0, 0f),
                new Vector3(x1, y1, 0f),
                new Vector3(x0, y1, 0f),
                // Double sided
                new Vector3(x0, y0, 0f),
                new Vector3(x1, y0, 0f),
                new Vector3(x1, y1, 0f),
                new Vector3(x0, y1, 0f),
            };

            int[] triangles = new int[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6
            };

            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };

            Mesh mesh = new Mesh
            {
                name = "PlaneVisualMesh",
                vertices = vertices,
                triangles = triangles,
                uv = uvs
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePolygonMesh(List<Vector2> points)
        {
            if (points == null || points.Count < 3) return null;

            // 1. Try Meta MRUK native ear-clipping triangulator
            try
            {
                Triangulator.TriangulatePoints(points, null, out Vector2[] outVerts, out int[] outIndices);
                if (outVerts != null && outVerts.Length >= 3 && outIndices != null && outIndices.Length >= 3)
                {
                    return BuildDoubleSidedMesh(outVerts, outIndices);
                }
            }
            catch (Exception)
            {
                // Fall back to pure C# Ear-Clipping
            }

            // 2. Pure C# Ear-Clipping (handles concave / reflex corners)
            List<int> triIndices = TriangulateEarClipping(points);
            Vector2[] verts = points.ToArray();
            return BuildDoubleSidedMesh(verts, triIndices.ToArray());
        }

        private static Mesh BuildDoubleSidedMesh(Vector2[] polyVerts, int[] polyIndices)
        {
            int n = polyVerts.Length;
            Vector3[] vertices = new Vector3[n * 2];
            Vector2[] uvs = new Vector2[n * 2];

            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(polyVerts[i].x, polyVerts[i].y, 0f);
                vertices[i + n] = vertices[i];
                uvs[i] = new Vector2(polyVerts[i].x, polyVerts[i].y);
                uvs[i + n] = uvs[i];
            }

            int triCount = polyIndices.Length;
            int[] triangles = new int[triCount * 2];
            // Front faces
            for (int i = 0; i < triCount; i++)
            {
                triangles[i] = polyIndices[i];
            }
            // Back faces (reversed winding for double-sided visibility)
            for (int i = 0; i < triCount; i += 3)
            {
                triangles[triCount + i] = polyIndices[i] + n;
                triangles[triCount + i + 1] = polyIndices[i + 2] + n;
                triangles[triCount + i + 2] = polyIndices[i + 1] + n;
            }

            Mesh mesh = new Mesh
            {
                name = "PolygonVisualMesh",
                vertices = vertices,
                triangles = triangles,
                uv = uvs
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<int> TriangulateEarClipping(List<Vector2> points)
        {
            List<int> triangles = new List<int>();
            int n = points.Count;
            if (n < 3) return triangles;

            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            // Calculate signed area to determine winding order
            float signedArea = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = points[i];
                Vector2 p2 = points[(i + 1) % n];
                signedArea += (p1.x * p2.y - p2.x * p1.y);
            }
            bool isCCW = signedArea > 0f;

            int attempts = 0;
            int maxAttempts = indices.Count * indices.Count;

            while (indices.Count > 3 && attempts < maxAttempts)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int prevIdx = (i - 1 + indices.Count) % indices.Count;
                    int nextIdx = (i + 1) % indices.Count;

                    int a = indices[prevIdx];
                    int b = indices[i];
                    int c = indices[nextIdx];

                    Vector2 va = points[a];
                    Vector2 vb = points[b];
                    Vector2 vc = points[c];

                    // Check if convex relative to winding order
                    float cross = (vb.x - va.x) * (vc.y - vb.y) - (vb.y - va.y) * (vc.x - vb.x);
                    bool convex = isCCW ? cross > 0.00001f : cross < -0.00001f;
                    if (!convex) continue;

                    // Check if any other vertex lies inside triangle va-vb-vc
                    bool hasPointInside = false;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        if (j == prevIdx || j == i || j == nextIdx) continue;
                        Vector2 p = points[indices[j]];
                        if (IsPointInsideTriangle(p, va, vb, vc))
                        {
                            hasPointInside = true;
                            break;
                        }
                    }

                    if (!hasPointInside)
                    {
                        // Valid ear - clip it off
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        indices.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }

                if (!earFound)
                {
                    attempts++;
                    // Fallback to prevent hanging on degenerate collinear points
                    if (indices.Count >= 3)
                    {
                        triangles.Add(indices[0]);
                        triangles.Add(indices[1]);
                        triangles.Add(indices[2]);
                        indices.RemoveAt(1);
                    }
                }
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }

            return triangles;
        }

        private static bool IsPointInsideTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float d1 = Cross2D(pt - v1, v2 - v1);
            float d2 = Cross2D(pt - v2, v3 - v2);
            float d3 = Cross2D(pt - v3, v1 - v3);

            bool hasNeg = (d1 < -0.0001f) || (d2 < -0.0001f) || (d3 < -0.0001f);
            bool hasPos = (d1 > 0.0001f) || (d2 > 0.0001f) || (d3 > 0.0001f);

            return !(hasNeg && hasPos);
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void CalculateRoomBounds(MRUKRoom room)
        {
            _roomBounds = new Bounds(room.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var anchor in room.Anchors)
            {
                if (anchor.VolumeBounds.HasValue)
                {
                    Bounds b = anchor.VolumeBounds.Value;
                    b.center = anchor.transform.TransformPoint(b.center);
                    if (!hasBounds) { _roomBounds = b; hasBounds = true; }
                    else { _roomBounds.Encapsulate(b); }
                }
                else if (anchor.PlaneRect.HasValue)
                {
                    Rect r = anchor.PlaneRect.Value;
                    Vector3 center = anchor.transform.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
                    Vector3 size = anchor.transform.TransformVector(new Vector3(r.width, r.height, 0.2f));
                    Bounds b = new Bounds(center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                    if (!hasBounds) { _roomBounds = b; hasBounds = true; }
                    else { _roomBounds.Encapsulate(b); }
                }
            }

            if (!hasBounds)
            {
                _roomBounds = new Bounds(Vector3.zero, new Vector3(5f, 2.5f, 5f));
            }
        }

        public void ToggleCeiling()
        {
            _isCeilingHidden = !_isCeilingHidden;
            foreach (var go in _ceilingObjects)
            {
                if (go != null) go.SetActive(!_isCeilingHidden);
            }
        }

        public void HandlePoseReceived(PosePacket pose)
        {
            string id = string.IsNullOrEmpty(pose.playerId) ? "Player_1" : pose.playerId;

            if (!_players.TryGetValue(id, out PlayerAvatarMarker marker))
            {
                marker = CreatePlayerAvatar(id);
                _players[id] = marker;
            }

            marker.UpdateTargetPose(pose.position, pose.rotation);
        }

        private PlayerAvatarMarker CreatePlayerAvatar(string id)
        {
            GameObject go;
            if (_playerAvatarPrefab != null)
            {
                go = Instantiate(_playerAvatarPrefab);
            }
            else
            {
                go = new GameObject($"PlayerMarker_{id}");
                go.AddComponent<PlayerAvatarMarker>();
            }

            PlayerAvatarMarker marker = go.GetComponent<PlayerAvatarMarker>();
            return marker;
        }

        public PlayerAvatarMarker GetPrimaryPlayer()
        {
            foreach (var p in _players.Values) return p;
            return null;
        }
    }
}
