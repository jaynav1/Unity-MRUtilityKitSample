using System;
using UnityEngine;

namespace MRUKSpectator.Whiteboard
{
    /// <summary>
    /// Dual Hand Tracking & Controller Pointer Input for the Mixed Reality Whiteboard.
    /// Supports:
    /// - Bare hands via Index Finger Pinching (hand.GetFingerIsPinching(OVRHand.HandFinger.Index))
    /// - Touch Controllers via Primary Index Trigger
    /// - PC Spectator mouse drag in Editor and Standalone via New Input System
    /// </summary>
    public class WhiteboardPointerInput : MonoBehaviour
    {
        [Header("Hand Tracking")]
        [SerializeField] private OVRHand _leftHand;
        [SerializeField] private OVRHand _rightHand;

        [Header("Controllers")]
        [SerializeField] private OVRInput.Controller _leftController = OVRInput.Controller.LTouch;
        [SerializeField] private OVRInput.Controller _rightController = OVRInput.Controller.RTouch;

        [Header("Pen Settings")]
        [SerializeField] private Color32 _penColor = new Color32(18, 92, 232, 255); // Vivid Blue
        [SerializeField] private byte _brushRadius = 6;
        [SerializeField] private float _maxPointerDistance = 8f;

        [Header("Cursor Indicator")]
        [SerializeField] private float _indicatorScale = 0.02f;

        public Color32 PenColor
        {
            get => _penColor;
            set => _penColor = value;
        }

        public byte BrushRadius
        {
            get => _brushRadius;
            set => _brushRadius = value;
        }

        private class PointerState
        {
            public bool IsDrawing;
            public Vector2 LastUV;
            public GameObject Indicator;
        }

        private PointerState _leftState = new();
        private PointerState _rightState = new();
        private PointerState _mouseState = new();

        private void Start()
        {
            AutoDetectHands();
            SetupIndicator(ref _leftState, "LeftPointerIndicator", new Color(0f, 0.8f, 1f, 0.85f));
            SetupIndicator(ref _rightState, "RightPointerIndicator", new Color(1f, 0.5f, 0f, 0.85f));
            SetupIndicator(ref _mouseState, "MousePointerIndicator", new Color(1f, 1f, 1f, 0.85f));
        }

        private void AutoDetectHands()
        {
            if (_leftHand != null && _rightHand != null) return;

            var hands = FindObjectsByType<OVRHand>();
            foreach (var h in hands)
            {
                try
                {
                    if (h.GetHandType() == OVRPlugin.Hand.HandLeft ||
                        h.gameObject.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_leftHand == null) _leftHand = h;
                    }
                    else if (h.GetHandType() == OVRPlugin.Hand.HandRight ||
                             h.gameObject.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_rightHand == null) _rightHand = h;
                    }
                }
                catch
                {
                    if (h.gameObject.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_leftHand == null) _leftHand = h;
                    }
                    else if (h.gameObject.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_rightHand == null) _rightHand = h;
                    }
                }
            }
        }

        private void SetupIndicator(ref PointerState state, string name, Color color)
        {
            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = name;
            indicator.transform.SetParent(transform, false);
            indicator.transform.localScale = Vector3.one * _indicatorScale;

            // Remove collider so it never intercepts raycasts
            if (indicator.TryGetComponent<Collider>(out var col))
            {
                Destroy(col);
            }

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (unlit != null)
            {
                var mat = new Material(unlit)
                {
                    color = color
                };
                if (indicator.TryGetComponent<Renderer>(out var rend))
                {
                    rend.sharedMaterial = mat;
                }
            }

            indicator.SetActive(false);
            state.Indicator = indicator;
        }

        private void Update()
        {
            if (_leftHand == null || _rightHand == null)
            {
                AutoDetectHands();
            }

            try
            {
                UpdateHandOrControllerPointer(_leftHand, _leftController, ref _leftState);
                UpdateHandOrControllerPointer(_rightHand, _rightController, ref _rightState);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WhiteboardPointerInput] Hand/Controller update warning: {ex.Message}");
            }

#if !UNITY_ANDROID || UNITY_EDITOR
            try
            {
                UpdateMousePointer(ref _mouseState);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WhiteboardPointerInput] Mouse update warning: {ex.Message}");
            }
#endif
        }

        private void UpdateHandOrControllerPointer(OVRHand hand, OVRInput.Controller controller, ref PointerState state)
        {
            bool hasRay = false;
            Ray ray = default;
            bool isDown = false;
            bool isHeld = false;
            bool isUp = false;

            // 1. Hand Tracking Mode (Bare Hands)
            if (hand != null && hand.IsTracked && hand.PointerPose != null)
            {
                ray = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
                hasRay = true;

                bool isPinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                isDown = isPinching && !state.IsDrawing;
                isHeld = isPinching && state.IsDrawing;
                isUp = !isPinching && state.IsDrawing;
            }
            // 2. Touch Controller Mode
            else
            {
                try
                {
                    Vector3 pos = OVRInput.GetLocalControllerPosition(controller);
                    Quaternion rot = OVRInput.GetLocalControllerRotation(controller);

                    // Transform to camera rig space if tracking space exists
                    Transform trackingSpace = transform;
                    if (Camera.main != null && Camera.main.transform.parent != null)
                    {
                        trackingSpace = Camera.main.transform.parent;
                    }

                    Vector3 worldPos = trackingSpace.TransformPoint(pos);
                    Vector3 worldDir = trackingSpace.TransformDirection(rot * Vector3.forward);
                    ray = new Ray(worldPos, worldDir);
                    hasRay = true;

                    isDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
                    isHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller);
                    isUp = OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, controller);
                }
                catch
                {
                    hasRay = false;
                }
            }

            if (!hasRay)
            {
                if (state.Indicator != null && state.Indicator.activeSelf)
                {
                    state.Indicator.SetActive(false);
                }
                state.IsDrawing = false;
                return;
            }

            ProcessRayDrawing(ray, isDown, isHeld, isUp, ref state);
        }

        private void UpdateMousePointer(ref PointerState state)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector2 mousePos = Vector2.zero;
            bool isDown = false;
            bool isHeld = false;
            bool isUp = false;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                mousePos = mouse.position.ReadValue();
                isDown = mouse.leftButton.wasPressedThisFrame;
                isHeld = mouse.leftButton.isPressed;
                isUp = mouse.leftButton.wasReleasedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                mousePos = Input.mousePosition;
                isDown = Input.GetMouseButtonDown(0);
                isHeld = Input.GetMouseButton(0);
                isUp = Input.GetMouseButtonUp(0);
            }
            catch { }
#endif

            Ray ray = cam.ScreenPointToRay(mousePos);
            ProcessRayDrawing(ray, isDown, isHeld, isUp, ref state);
        }

        private void ProcessRayDrawing(Ray ray, bool isDown, bool isHeld, bool isUp, ref PointerState state)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _maxPointerDistance))
            {
                SpectatorWhiteboard wb = hit.collider.GetComponentInParent<SpectatorWhiteboard>();
                if (wb != null)
                {
                    // Update indicator position
                    if (state.Indicator != null)
                    {
                        state.Indicator.transform.position = hit.point + hit.normal * 0.005f;
                        state.Indicator.transform.rotation = Quaternion.LookRotation(hit.normal);
                        if (!state.Indicator.activeSelf) state.Indicator.SetActive(true);
                    }

                    Vector2 currentUV = wb.WorldToUV(hit.point);

                    if (isDown)
                    {
                        state.IsDrawing = true;
                        state.LastUV = currentUV;
                        wb.DrawLine(currentUV, currentUV, _penColor, _brushRadius, true);
                    }
                    else if (isHeld && state.IsDrawing)
                    {
                        if (Vector2.Distance(state.LastUV, currentUV) > 0.001f)
                        {
                            wb.DrawLine(state.LastUV, currentUV, _penColor, _brushRadius, true);
                            state.LastUV = currentUV;
                        }
                    }
                    else if (isUp)
                    {
                        state.IsDrawing = false;
                    }

                    return;
                }
            }

            // If we missed the whiteboard
            if (state.Indicator != null && state.Indicator.activeSelf)
            {
                state.Indicator.SetActive(false);
            }
            if (isUp)
            {
                state.IsDrawing = false;
            }
        }
    }
}
