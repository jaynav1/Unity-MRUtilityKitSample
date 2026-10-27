using System;
using UnityEngine;
using MRUKSpectator.Network;

namespace MRUKSpectator.Whiteboard
{
    /// <summary>
    /// Enables mixed reality repositioning and network synchronization of the SpectatorWhiteboard.
    /// Supports:
    /// - VR Touch Controllers: Aim at board + hold Grip Trigger (Hand Trigger). Thumbstick pushes/pulls depth.
    /// - VR Bare Hands: Middle Finger Pinch (OVRHand.HandFinger.Middle) to grab and move.
    /// - Desktop / Editor: Right-Click Drag (or Middle-Click Drag) to move on view plane. Mouse Wheel pushes/pulls depth.
    /// </summary>
    public class WhiteboardDraggable : MonoBehaviour
    {
        [Header("Interaction Settings")]
        [SerializeField] private bool _allowMovement = true;
        [SerializeField] private float _grabDistance = 5f;

        public bool AllowMovement
        {
            get => _allowMovement;
            set => _allowMovement = value;
        }

        public float GrabDistance
        {
            get => _grabDistance;
            set => _grabDistance = value;
        }

        private enum GrabSourceType { None, Mouse, Controller, Hand }
        private GrabSourceType _activeGrabSource = GrabSourceType.None;

        private OVRInput.Controller _activeController;
        private OVRHand _activeHand;
        private OVRHand _leftHand;
        private OVRHand _rightHand;

        private Collider _collider;
        private float _grabDistanceAlongRay;
        private Vector3 _grabLocalPoint;
        private Quaternion _grabInitialRotation;
        private Quaternion _sourceInitialRotation;
        private float _lastBroadcastTime;

        public bool IsGrabbed => _activeGrabSource != GrabSourceType.None;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
        }

        private void Start()
        {
            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnWhiteboardPoseReceived += OnRemotePoseReceived;
            }
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnWhiteboardPoseReceived += OnRemotePoseReceived;
            }
        }

        private void OnDestroy()
        {
            if (QuestSender.Instance != null)
            {
                QuestSender.Instance.OnWhiteboardPoseReceived -= OnRemotePoseReceived;
            }
            if (SpectatorServer.Instance != null)
            {
                SpectatorServer.Instance.OnWhiteboardPoseReceived -= OnRemotePoseReceived;
            }
        }

        private void Update()
        {
            if (!_allowMovement) return;

            if (_collider == null)
            {
                _collider = GetComponent<Collider>();
                if (_collider == null) return;
            }

            if (_activeGrabSource == GrabSourceType.None)
            {
                CheckForGrabStart();
            }
            else
            {
                UpdateActiveGrab();
            }
        }

        private void CheckForGrabStart()
        {
            // 1. Desktop / Mouse Check (Right Mouse Button or Middle Mouse Button)
#if !UNITY_ANDROID || UNITY_EDITOR
            CheckMouseGrab();
            if (_activeGrabSource != GrabSourceType.None) return;
#endif

            // 2. VR Touch Controllers (Grip Trigger)
            CheckControllerGrab(OVRInput.Controller.RTouch);
            if (_activeGrabSource != GrabSourceType.None) return;

            CheckControllerGrab(OVRInput.Controller.LTouch);
            if (_activeGrabSource != GrabSourceType.None) return;

            // 3. VR Bare Hands (Middle Finger Pinch)
            AutoDetectHands();
            if (_rightHand != null) CheckHandGrab(_rightHand);
            if (_activeGrabSource != GrabSourceType.None) return;

            if (_leftHand != null) CheckHandGrab(_leftHand);
        }

        private void AutoDetectHands()
        {
            if (_leftHand != null && _rightHand != null) return;

            var hands = FindObjectsByType<OVRHand>();
            foreach (var h in hands)
            {
                if (h == null) continue;
                try
                {
                    if (h.GetHand() == OVRPlugin.Hand.HandLeft ||
                        h.gameObject.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_leftHand == null) _leftHand = h;
                    }
                    else if (h.GetHand() == OVRPlugin.Hand.HandRight ||
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

        private void CheckMouseGrab()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector2 mousePos = Vector2.zero;
            bool isDown = false;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                mousePos = mouse.position.ReadValue();
                isDown = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                mousePos = Input.mousePosition;
                isDown = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            }
            catch { }
#endif

            if (isDown)
            {
                Ray ray = cam.ScreenPointToRay(mousePos);
                if (_collider.Raycast(ray, out RaycastHit hit, 50f))
                {
                    StartGrab(GrabSourceType.Mouse, hit, ray, Quaternion.identity);
                }
            }
        }

        private void CheckControllerGrab(OVRInput.Controller controller)
        {
            try
            {
                bool isGripDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, controller);
                if (!isGripDown) return;

                if (TryGetControllerRay(controller, out Ray ray, out Quaternion rot))
                {
                    if (_collider.Raycast(ray, out RaycastHit hit, _grabDistance))
                    {
                        _activeController = controller;
                        StartGrab(GrabSourceType.Controller, hit, ray, rot);
                    }
                }
            }
            catch { }
        }

        private void CheckHandGrab(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked || hand.PointerPose == null) return;

            try
            {
                bool isMiddlePinch = hand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
                if (!isMiddlePinch) return;

                Ray ray = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
                if (_collider.Raycast(ray, out RaycastHit hit, _grabDistance))
                {
                    _activeHand = hand;
                    StartGrab(GrabSourceType.Hand, hit, ray, hand.PointerPose.rotation);
                }
            }
            catch { }
        }

        private void StartGrab(GrabSourceType source, RaycastHit hit, Ray ray, Quaternion sourceRot)
        {
            _activeGrabSource = source;
            _grabDistanceAlongRay = hit.distance;
            _grabLocalPoint = transform.InverseTransformPoint(hit.point);
            _grabInitialRotation = transform.rotation;
            _sourceInitialRotation = sourceRot;
        }

        private void UpdateActiveGrab()
        {
            switch (_activeGrabSource)
            {
                case GrabSourceType.Mouse:
                    UpdateMouseGrab();
                    break;
                case GrabSourceType.Controller:
                    UpdateControllerGrab();
                    break;
                case GrabSourceType.Hand:
                    UpdateHandGrab();
                    break;
            }
        }

        private void UpdateMouseGrab()
        {
            Camera cam = Camera.main;
            if (cam == null) { EndGrab(); return; }

            Vector2 mousePos = Vector2.zero;
            float scroll = 0f;
            bool isHeld = false;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                mousePos = mouse.position.ReadValue();
                scroll = mouse.scroll.ReadValue().y * 0.002f;
                isHeld = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                mousePos = Input.mousePosition;
                scroll = Input.mouseScrollDelta.y * 0.2f;
                isHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            }
            catch { }
#endif

            if (!isHeld)
            {
                EndGrab();
                return;
            }

            _grabDistanceAlongRay = Mathf.Clamp(_grabDistanceAlongRay + scroll, 0.5f, 20f);
            Ray ray = cam.ScreenPointToRay(mousePos);
            Vector3 targetPivot = ray.GetPoint(_grabDistanceAlongRay);

            transform.position = targetPivot - (transform.rotation * _grabLocalPoint);
            MaybeBroadcastPose(false);
        }

        private void UpdateControllerGrab()
        {
            try
            {
                bool isHeld = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, _activeController);
                if (!isHeld)
                {
                    EndGrab();
                    return;
                }

                if (TryGetControllerRay(_activeController, out Ray ray, out Quaternion rot))
                {
                    Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, _activeController);
                    _grabDistanceAlongRay = Mathf.Clamp(_grabDistanceAlongRay + stick.y * Time.deltaTime * 1.5f, 0.4f, 15f);

                    // Reorient board with controller rotation
                    Quaternion rotDelta = rot * Quaternion.Inverse(_sourceInitialRotation);
                    transform.rotation = rotDelta * _grabInitialRotation;

                    Vector3 targetPivot = ray.GetPoint(_grabDistanceAlongRay);
                    transform.position = targetPivot - (transform.rotation * _grabLocalPoint);

                    MaybeBroadcastPose(false);
                }
            }
            catch
            {
                EndGrab();
            }
        }

        private void UpdateHandGrab()
        {
            try
            {
                if (_activeHand == null || !_activeHand.IsTracked || _activeHand.PointerPose == null)
                {
                    EndGrab();
                    return;
                }

                bool isPinching = _activeHand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
                if (!isPinching)
                {
                    EndGrab();
                    return;
                }

                Ray ray = new Ray(_activeHand.PointerPose.position, _activeHand.PointerPose.forward);
                Vector3 targetPivot = ray.GetPoint(_grabDistanceAlongRay);

                transform.position = targetPivot - (transform.rotation * _grabLocalPoint);
                MaybeBroadcastPose(false);
            }
            catch
            {
                EndGrab();
            }
        }

        private void EndGrab()
        {
            _activeGrabSource = GrabSourceType.None;
            _activeHand = null;
            MaybeBroadcastPose(true);
        }

        private bool TryGetControllerRay(OVRInput.Controller controller, out Ray ray, out Quaternion rot)
        {
            try
            {
                Vector3 pos = OVRInput.GetLocalControllerPosition(controller);
                rot = OVRInput.GetLocalControllerRotation(controller);

                Transform trackingSpace = transform;
                if (Camera.main != null && Camera.main.transform.parent != null)
                {
                    trackingSpace = Camera.main.transform.parent;
                }

                Vector3 worldPos = trackingSpace.TransformPoint(pos);
                Vector3 worldDir = trackingSpace.TransformDirection(rot * Vector3.forward);
                Quaternion worldRot = trackingSpace.rotation * rot;
                rot = worldRot;

                ray = new Ray(worldPos, worldDir);
                return true;
            }
            catch
            {
                ray = default;
                rot = Quaternion.identity;
                return false;
            }
        }

        private void MaybeBroadcastPose(bool force)
        {
            if (force || Time.time - _lastBroadcastTime > 0.05f) // 20Hz sync during movement
            {
                _lastBroadcastTime = Time.time;
                SetPose(transform.position, transform.rotation, transform.localScale, true);
            }
        }

        public void SetPose(Vector3 pos, Quaternion rot, Vector3 scale, bool broadcast = true)
        {
            transform.position = pos;
            transform.rotation = rot;
            transform.localScale = scale;

            if (broadcast)
            {
                var pose = new WhiteboardPosePacket
                {
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                };

                if (QuestSender.Instance != null && QuestSender.Instance.IsConnected)
                {
                    QuestSender.Instance.SendWhiteboardPose(pose);
                }
                else if (SpectatorServer.Instance != null && SpectatorServer.Instance.IsRunning)
                {
                    SpectatorServer.Instance.SendWhiteboardPose(pose);
                }
            }
        }

        private void OnRemotePoseReceived(WhiteboardPosePacket pose)
        {
            if (IsGrabbed) return; // Do not interrupt local manipulation

            transform.localPosition = pose.localPosition;
            transform.localRotation = pose.localRotation;
            if (pose.localScale.sqrMagnitude > 0.001f)
            {
                transform.localScale = pose.localScale;
            }
        }
    }
}
