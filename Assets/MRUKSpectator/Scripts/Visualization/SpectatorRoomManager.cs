using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.CameraController;
using MRUKSpectator.Network;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MRUKSpectator.Visualization
{
    public class SpectatorRoomManager : MonoBehaviour
    {
        public static SpectatorRoomManager Instance { get; private set; }

        [Header("Room Appearance")]
        [SerializeField] private bool _hideCeilingByDefault = true;
        [Tooltip("Color applied to the room meshes (walls, floor, furniture).")]
        [FormerlySerializedAs("_mrukBlueColor")]
        [SerializeField] private Color _roomMeshColor = new Color(0.143f, 0.690f, 0.868f, 1f);
        [Tooltip("Optional: Custom material for all room meshes. If set, this overrides procedural shaders.")]
        [SerializeField] private Material _customRoomMaterial;

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

        public Color RoomMeshColor
        {
            get => _roomMeshColor;
            set
            {
                _roomMeshColor = value;
                MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
                if (room != null) ApplyRoomStyling(room);
            }
        }

        public Material CustomRoomMaterial
        {
            get => _customRoomMaterial;
            set
            {
                _customRoomMaterial = value;
                MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
                if (room != null) ApplyRoomStyling(room);
            }
        }

        private void Awake()
        {
            Instance = this;
            _isCeilingHidden = _hideCeilingByDefault;

            if (!TryGetComponent<MRUKSpectator.Markers.SpectatorMarkerManager>(out _))
            {
                gameObject.AddComponent<MRUKSpectator.Markers.SpectatorMarkerManager>();
            }
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
            if (string.IsNullOrEmpty(json) || json.Length < 50)
            {
                Debug.LogWarning("[SpectatorRoomManager] Ignoring empty or invalid room JSON.");
                return;
            }

            if (HasRoomLoaded)
            {
                Debug.Log("[SpectatorRoomManager] Active room already loaded. Preserving host room.");
                return;
            }

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
            Material roomMaterial;
            if (_customRoomMaterial != null)
            {
                roomMaterial = _customRoomMaterial;
            }
            else
            {
                Shader mrukShader = Shader.Find("MixedReality/SceneMeshDefault");
                if (mrukShader != null)
                {
                    roomMaterial = new Material(mrukShader);
                    roomMaterial.SetColor("_Color", _roomMeshColor);
                    roomMaterial.SetFloat("_Angle", 60f);
                }
                else
                {
                    Shader fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    roomMaterial = new Material(fallbackShader);
                    roomMaterial.color = _roomMeshColor;
                    if (roomMaterial.HasProperty("_BaseColor")) roomMaterial.SetColor("_BaseColor", _roomMeshColor);
                    if (roomMaterial.HasProperty("_Cull")) roomMaterial.SetFloat("_Cull", (float)CullMode.Off);
                }
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

                // Procedural 3D Mesh & Collider Generation
                if (anchor.VolumeBounds.HasValue)
                {
                    mesh = CreateBoxMesh(anchor.VolumeBounds.Value);
                    if (visualGo.TryGetComponent(out MeshCollider mc))
                    {
                        Destroy(mc);
                    }
                    if (!visualGo.TryGetComponent(out BoxCollider boxCollider))
                    {
                        boxCollider = visualGo.AddComponent<BoxCollider>();
                    }
                    boxCollider.center = anchor.VolumeBounds.Value.center;
                    boxCollider.size = anchor.VolumeBounds.Value.size;
                }
                else if (anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3)
                {
                    mesh = CreatePolygonMesh(anchor.PlaneBoundary2D);
                    if (visualGo.TryGetComponent(out BoxCollider bc))
                    {
                        Destroy(bc);
                    }
                    if (!visualGo.TryGetComponent(out MeshCollider meshCollider))
                    {
                        meshCollider = visualGo.AddComponent<MeshCollider>();
                    }
                    meshCollider.sharedMesh = mesh;
                }
                else if (anchor.PlaneRect.HasValue)
                {
                    mesh = CreatePlaneMesh(anchor.PlaneRect.Value);
                    if (visualGo.TryGetComponent(out MeshCollider mc))
                    {
                        Destroy(mc);
                    }
                    if (!visualGo.TryGetComponent(out BoxCollider boxCollider))
                    {
                        boxCollider = visualGo.AddComponent<BoxCollider>();
                    }
                    boxCollider.center = new Vector3(anchor.PlaneRect.Value.center.x, anchor.PlaneRect.Value.center.y, 0f);
                    boxCollider.size = new Vector3(anchor.PlaneRect.Value.width, anchor.PlaneRect.Value.height, 0.02f);
                }
                else if (anchor.GlobalMesh != null)
                {
                    mesh = anchor.GlobalMesh;
                    if (visualGo.TryGetComponent(out BoxCollider bc))
                    {
                        Destroy(bc);
                    }
                    if (!visualGo.TryGetComponent(out MeshCollider meshCollider))
                    {
                        meshCollider = visualGo.AddComponent<MeshCollider>();
                    }
                    meshCollider.sharedMesh = mesh;
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

                meshRenderer.sharedMaterial = roomMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        private static Mesh CreateBoxMesh(Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 ext = bounds.extents;

            Vector3 min = c - ext;
            Vector3 max = c + ext;

            // 24 vertices (4 per face x 6 faces) with exact outward normals
            Vector3[] vertices = new Vector3[24];
            Vector3[] normals = new Vector3[24];
            Vector2[] uvs = new Vector2[24];
            int[] triangles = new int[36];

            // 0: Top face (+Y)
            vertices[0] = new Vector3(min.x, max.y, min.z);
            vertices[1] = new Vector3(max.x, max.y, min.z);
            vertices[2] = new Vector3(max.x, max.y, max.z);
            vertices[3] = new Vector3(min.x, max.y, max.z);
            for (int i = 0; i < 4; i++) normals[i] = Vector3.up;
            uvs[0] = new Vector2(0, 0); uvs[1] = new Vector2(1, 0); uvs[2] = new Vector2(1, 1); uvs[3] = new Vector2(0, 1);
            triangles[0] = 0; triangles[1] = 3; triangles[2] = 2;
            triangles[3] = 0; triangles[4] = 2; triangles[5] = 1;

            // 1: Bottom face (-Y)
            vertices[4] = new Vector3(min.x, min.y, max.z);
            vertices[5] = new Vector3(max.x, min.y, max.z);
            vertices[6] = new Vector3(max.x, min.y, min.z);
            vertices[7] = new Vector3(min.x, min.y, min.z);
            for (int i = 4; i < 8; i++) normals[i] = Vector3.down;
            uvs[4] = new Vector2(0, 0); uvs[5] = new Vector2(1, 0); uvs[6] = new Vector2(1, 1); uvs[7] = new Vector2(0, 1);
            triangles[6] = 4; triangles[7] = 7; triangles[8] = 6;
            triangles[9] = 4; triangles[10] = 6; triangles[11] = 5;

            // 2: Front face (+Z)
            vertices[8]  = new Vector3(min.x, min.y, max.z);
            vertices[9]  = new Vector3(max.x, min.y, max.z);
            vertices[10] = new Vector3(max.x, max.y, max.z);
            vertices[11] = new Vector3(min.x, max.y, max.z);
            for (int i = 8; i < 12; i++) normals[i] = Vector3.forward;
            uvs[8] = new Vector2(0, 0); uvs[9] = new Vector2(1, 0); uvs[10] = new Vector2(1, 1); uvs[11] = new Vector2(0, 1);
            triangles[12] = 8; triangles[13] = 11; triangles[14] = 10;
            triangles[15] = 8; triangles[16] = 10; triangles[17] = 9;

            // 3: Back face (-Z)
            vertices[12] = new Vector3(max.x, min.y, min.z);
            vertices[13] = new Vector3(min.x, min.y, min.z);
            vertices[14] = new Vector3(min.x, max.y, min.z);
            vertices[15] = new Vector3(max.x, max.y, min.z);
            for (int i = 12; i < 16; i++) normals[i] = Vector3.back;
            uvs[12] = new Vector2(0, 0); uvs[13] = new Vector2(1, 0); uvs[14] = new Vector2(1, 1); uvs[15] = new Vector2(0, 1);
            triangles[18] = 12; triangles[19] = 15; triangles[20] = 14;
            triangles[21] = 12; triangles[22] = 14; triangles[23] = 13;

            // 4: Right face (+X)
            vertices[16] = new Vector3(max.x, min.y, min.z);
            vertices[17] = new Vector3(max.x, max.y, min.z);
            vertices[18] = new Vector3(max.x, max.y, max.z);
            vertices[19] = new Vector3(max.x, min.y, max.z);
            for (int i = 16; i < 20; i++) normals[i] = Vector3.right;
            uvs[16] = new Vector2(0, 0); uvs[17] = new Vector2(1, 0); uvs[18] = new Vector2(1, 1); uvs[19] = new Vector2(0, 1);
            triangles[24] = 16; triangles[25] = 17; triangles[26] = 18;
            triangles[27] = 16; triangles[28] = 18; triangles[29] = 19;

            // 5: Left face (-X)
            vertices[20] = new Vector3(min.x, min.y, max.z);
            vertices[21] = new Vector3(min.x, max.y, max.z);
            vertices[22] = new Vector3(min.x, max.y, min.z);
            vertices[23] = new Vector3(min.x, min.y, min.z);
            for (int i = 20; i < 24; i++) normals[i] = Vector3.left;
            uvs[20] = new Vector2(0, 0); uvs[21] = new Vector2(1, 0); uvs[22] = new Vector2(1, 1); uvs[23] = new Vector2(0, 1);
            triangles[30] = 20; triangles[31] = 21; triangles[32] = 22;
            triangles[33] = 20; triangles[34] = 22; triangles[35] = 23;

            Mesh mesh = new Mesh
            {
                name = "BoxVisualMesh",
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };
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

        private static readonly Color[] PlayerColorPalette = new Color[]
        {
            new Color(0.0f, 0.898f, 1.0f, 1.0f),  // Player 1: Vivid Cyan (#00E5FF)
            new Color(1.0f, 0.569f, 0.0f, 1.0f),  // Player 2: Vivid Orange (#FF9100)
            new Color(0.835f, 0.0f, 0.976f, 1.0f), // Player 3: Vivid Magenta (#D500F9)
            new Color(0.0f, 0.902f, 0.463f, 1.0f), // Player 4: Vivid Spring Green (#00E676)
            new Color(1.0f, 0.839f, 0.0f, 1.0f),  // Player 5: Gold (#FFD600)
            new Color(1.0f, 0.251f, 0.506f, 1.0f)  // Player 6: Coral Pink (#FF4081)
        };

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
            int playerIndex = _players.Count;
            Color playerColor = PlayerColorPalette[playerIndex % PlayerColorPalette.Length];
            string displayName = id.Replace("_", " ");
            marker.SetPlayerIdentity(id, playerColor, displayName);
            return marker;
        }

        public PlayerAvatarMarker GetPrimaryPlayer()
        {
            foreach (var p in _players.Values) return p;
            return null;
        }

        public IReadOnlyCollection<PlayerAvatarMarker> GetAllPlayers() => _players.Values;
        public int PlayerCount => _players.Count;
    }
}
