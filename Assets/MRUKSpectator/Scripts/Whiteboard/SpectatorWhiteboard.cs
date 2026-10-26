using System;
using UnityEngine;
using MRUKSpectator.Network;

namespace MRUKSpectator.Whiteboard
{
    /// <summary>
    /// Collaborative Mixed Reality Whiteboard.
    /// Manages an authoritative 2D drawable texture, local stroke rasterization,
    /// and network synchronization between Meta Quest headsets and PC Spectators.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class SpectatorWhiteboard : MonoBehaviour
    {
        public static SpectatorWhiteboard Instance { get; private set; }

        [Header("Texture Dimensions")]
        [SerializeField] private int _textureWidth = 2048;
        [SerializeField] private int _textureHeight = 1152; // 16:9 aspect ratio

        [Header("Appearance")]
        [SerializeField] private Color32 _backgroundColor = new Color32(248, 249, 250, 255); // Crisp off-white
        [SerializeField] private Color32 _borderColor = new Color32(200, 204, 209, 255);
        [SerializeField] private int _borderThickness = 8;

        [Header("Default Brush Settings")]
        [SerializeField] private Color32 _defaultPenColor = new Color32(18, 92, 232, 255); // Vivid Blue
        [SerializeField] private byte _defaultBrushRadius = 6;

        public Texture2D WhiteboardTexture { get; private set; }
        public int TextureWidth => _textureWidth;
        public int TextureHeight => _textureHeight;

        private MeshRenderer _meshRenderer;
        private Material _whiteboardMaterial;
        private Color32[] _pixelBuffer;
        private bool _isDirty;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _meshRenderer = GetComponent<MeshRenderer>();
            InitializeWhiteboardTexture();
            EnsureCollider();
        }

        private void Start()
        {
            SubscribeToNetworkEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkEvents();
            if (_whiteboardMaterial != null)
            {
                Destroy(_whiteboardMaterial);
            }
            if (WhiteboardTexture != null)
            {
                Destroy(WhiteboardTexture);
            }
        }

        private void InitializeWhiteboardTexture()
        {
            WhiteboardTexture = new Texture2D(_textureWidth, _textureHeight, TextureFormat.RGBA32, false)
            {
                name = "Whiteboard_DynamicTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            _pixelBuffer = new Color32[_textureWidth * _textureHeight];
            ClearBuffer(false);

            // Assign to material
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ??
                                 Shader.Find("Universal Render Pipeline/Lit") ??
                                 Shader.Find("Unlit/Texture") ??
                                 Shader.Find("Standard");

            _whiteboardMaterial = new Material(unlitShader)
            {
                name = "Whiteboard_Material"
            };

            if (_whiteboardMaterial.HasProperty("_BaseMap"))
            {
                _whiteboardMaterial.SetTexture("_BaseMap", WhiteboardTexture);
            }
            else
            {
                _whiteboardMaterial.mainTexture = WhiteboardTexture;
            }

            _meshRenderer.sharedMaterial = _whiteboardMaterial;
        }

        private void EnsureCollider()
        {
            if (!TryGetComponent<BoxCollider>(out var boxCollider))
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(1f, 1f, 0.02f);
            }
        }

        private void SubscribeToNetworkEvents()
        {
            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnWhiteboardLineReceived += OnRemoteLineReceived;
                QuestSender.Instance.OnWhiteboardClearReceived += OnRemoteClearReceived;
            }

            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnWhiteboardLineReceived += OnRemoteLineReceived;
                SpectatorServer.Instance.OnWhiteboardClearReceived += OnRemoteClearReceived;
            }
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnWhiteboardLineReceived -= OnRemoteLineReceived;
                QuestSender.Instance.OnWhiteboardClearReceived -= OnRemoteClearReceived;
            }

            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnWhiteboardLineReceived -= OnRemoteLineReceived;
                SpectatorServer.Instance.OnWhiteboardClearReceived -= OnRemoteClearReceived;
            }
        }

        private void LateUpdate()
        {
            if (_isDirty && WhiteboardTexture != null)
            {
                WhiteboardTexture.SetPixels32(_pixelBuffer);
                WhiteboardTexture.Apply(false);
                _isDirty = false;
            }
        }

        /// <summary>
        /// Converts a world-space hit point to UV coordinates (0.0 to 1.0) on the whiteboard Quad.
        /// </summary>
        public Vector2 WorldToUV(Vector3 worldHitPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldHitPos);
            // Quad standard mesh local space spans from (-0.5, -0.5) to (+0.5, +0.5)
            float u = Mathf.Clamp01(local.x + 0.5f);
            float v = Mathf.Clamp01(local.y + 0.5f);
            return new Vector2(u, v);
        }

        /// <summary>
        /// Draws a line on the whiteboard. Broadcasts to other participants if broadcast is true.
        /// </summary>
        public void DrawLine(Vector2 startUV, Vector2 endUV, Color32 color, byte brushRadius, bool broadcast = true)
        {
            if (brushRadius == 0) brushRadius = _defaultBrushRadius;

            RasterizeLine(startUV, endUV, color, brushRadius);
            _isDirty = true;

            if (broadcast)
            {
                var packet = new WhiteboardLinePacket
                {
                    startUV = startUV,
                    endUV = endUV,
                    color = color,
                    brushRadius = brushRadius
                };

                if (QuestSender.Instance != null && QuestSender.Instance.IsConnected)
                {
                    QuestSender.Instance.SendWhiteboardLine(packet);
                }
                else if (SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
                {
                    SpectatorServer.Instance.SendWhiteboardLine(packet);
                }
            }
        }

        /// <summary>
        /// Clears the whiteboard. Broadcasts to other participants if broadcast is true.
        /// </summary>
        public void Clear(bool broadcast = true)
        {
            ClearBuffer(true);

            if (broadcast)
            {
                if (QuestSender.Instance != null && QuestSender.Instance.IsConnected)
                {
                    QuestSender.Instance.SendWhiteboardClear();
                }
                else if (SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
                {
                    SpectatorServer.Instance.SendWhiteboardClear();
                }
            }
        }

        private void ClearBuffer(bool markDirty = true)
        {
            for (int y = 0; y < _textureHeight; y++)
            {
                for (int x = 0; x < _textureWidth; x++)
                {
                    bool isBorder = x < _borderThickness || x >= _textureWidth - _borderThickness ||
                                    y < _borderThickness || y >= _textureHeight - _borderThickness;
                    _pixelBuffer[y * _textureWidth + x] = isBorder ? _borderColor : _backgroundColor;
                }
            }

            if (markDirty)
            {
                _isDirty = true;
            }
            else if (WhiteboardTexture != null)
            {
                WhiteboardTexture.SetPixels32(_pixelBuffer);
                WhiteboardTexture.Apply(false);
            }
        }

        private void RasterizeLine(Vector2 startUV, Vector2 endUV, Color32 color, int radius)
        {
            float dist = Vector2.Distance(startUV, endUV);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist * _textureWidth));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 uv = Vector2.Lerp(startUV, endUV, t);
                StampBrush(uv, color, radius);
            }
        }

        private void StampBrush(Vector2 uv, Color32 color, int radius)
        {
            int cx = Mathf.RoundToInt(uv.x * (_textureWidth - 1));
            int cy = Mathf.RoundToInt(uv.y * (_textureHeight - 1));
            int r2 = radius * radius;

            int minX = Mathf.Max(0, cx - radius);
            int maxX = Mathf.Min(_textureWidth - 1, cx + radius);
            int minY = Mathf.Max(0, cy - radius);
            int maxY = Mathf.Min(_textureHeight - 1, cy + radius);

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - cy;
                int dy2 = dy * dy;
                int rowOffset = y * _textureWidth;

                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy2 <= r2)
                    {
                        _pixelBuffer[rowOffset + x] = color;
                    }
                }
            }
        }

        private void OnRemoteLineReceived(WhiteboardLinePacket line)
        {
            DrawLine(line.startUV, line.endUV, line.color, line.brushRadius, false);
        }

        private void OnRemoteClearReceived()
        {
            Clear(false);
        }
    }
}
