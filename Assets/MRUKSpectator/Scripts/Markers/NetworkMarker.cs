using System;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using MRUKSpectator.Network;

namespace MRUKSpectator.Markers
{
    /// <summary>
    /// Simple mixed-reality spatial marker: a clean colored sphere
    /// synchronized in physical MRUK room-local coordinates.
    /// </summary>
    public class NetworkMarker : MonoBehaviour
    {
        [Header("Marker Metadata")]
        [SerializeField] private string _markerId;
        [SerializeField] private string _markerLabel = "Point";
        [SerializeField] private Color _markerColor = new Color(0.143f, 0.690f, 0.868f, 1f); // MRUK Cyan
        [SerializeField] private float _markerRadius = 0.05f; // 10cm diameter

        public string MarkerId => _markerId;
        public string MarkerLabel => _markerLabel;
        public Color MarkerColor => _markerColor;
        public Vector3 RoomLocalPosition { get; private set; }
        public Quaternion RoomLocalRotation { get; private set; } = Quaternion.identity;

        private MeshRenderer _renderer;
        private Material _material;
        private Transform _labelTransform;
        private TextMesh _textMesh;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_markerId))
            {
                _markerId = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            BuildSimpleVisual();
        }

        public void Initialize(MarkerPacket packet)
        {
            _markerId = packet.markerId;
            RoomLocalPosition = packet.localPosition;
            RoomLocalRotation = packet.localRotation;
            _markerColor = packet.color;
            _markerLabel = packet.label;

            if (_textMesh != null) _textMesh.text = _markerLabel;
            ApplyColor();
            UpdateTransformToRoom();
        }

        public MarkerPacket ToPacket()
        {
            return new MarkerPacket
            {
                markerId = _markerId,
                localPosition = RoomLocalPosition,
                localRotation = RoomLocalRotation,
                color = _markerColor,
                label = _markerLabel,
                scale = _markerRadius * 2f
            };
        }

        public void SetColor(Color color)
        {
            _markerColor = color;
            ApplyColor();
        }

        public void SetLabel(string label)
        {
            _markerLabel = label;
            if (_textMesh != null) _textMesh.text = label;
        }

        private void Update()
        {
            UpdateTransformToRoom();
        }

        private void LateUpdate()
        {
            // Billboard the text label towards the spectator camera
            if (_labelTransform != null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    _labelTransform.rotation = cam.transform.rotation.normalized;
                }
            }
        }

        private void UpdateTransformToRoom()
        {
            MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null && MRUK.Instance != null && MRUK.Instance.Rooms.Count > 0)
            {
                room = MRUK.Instance.Rooms[0];
            }

            if (room != null)
            {
                transform.position = room.transform.TransformPoint(RoomLocalPosition);
                transform.rotation = (room.transform.rotation * RoomLocalRotation).normalized;
            }
            else
            {
                transform.localPosition = RoomLocalPosition;
                transform.localRotation = RoomLocalRotation.normalized;
            }
        }

        private void BuildSimpleVisual()
        {
            // 1. Base Puck / Disc resting on the surface face
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "PuckBase";
            disc.transform.SetParent(transform, false);
            // Cylinder in Unity is 2 units tall, 1 unit diameter.
            // Scale: diameter 0.12m, height 0.008m (8mm)
            disc.transform.localScale = new Vector3(0.12f, 0.004f, 0.12f);
            disc.transform.localPosition = new Vector3(0f, 0.004f, 0f);

            // 2. Central Beacon Dome
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "BeaconDome";
            dome.transform.SetParent(transform, false);
            dome.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            dome.transform.localPosition = new Vector3(0f, 0.025f, 0f);

            // Remove default primitive colliders from children and add single root collider
            if (disc.TryGetComponent(out Collider dc)) Destroy(dc);
            if (dome.TryGetComponent(out Collider sc)) Destroy(sc);

            SphereCollider rootCol = GetComponent<SphereCollider>();
            if (rootCol == null) rootCol = gameObject.AddComponent<SphereCollider>();
            rootCol.center = new Vector3(0f, 0.03f, 0f);
            rootCol.radius = 0.07f;

            // Shared illuminated material
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _material = new Material(litShader);
            if (disc.TryGetComponent(out MeshRenderer dr)) dr.sharedMaterial = _material;
            if (dome.TryGetComponent(out MeshRenderer mr)) mr.sharedMaterial = _material;

            // 3. Floating 3D Text Label
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            _labelTransform = labelGo.transform;
            _textMesh = labelGo.AddComponent<TextMesh>();
            _textMesh.text = _markerLabel;
            _textMesh.fontSize = 24;
            _textMesh.characterSize = 0.012f;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.color = Color.white;

            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_material != null)
            {
                _material.color = _markerColor;
                if (_material.HasProperty("_BaseColor"))
                    _material.SetColor("_BaseColor", _markerColor);
                if (_material.HasProperty("_EmissionColor"))
                {
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", _markerColor * 0.4f);
                }
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
