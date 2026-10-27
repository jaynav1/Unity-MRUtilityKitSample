using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRUKSpectator.CameraController
{
    public class OrbitCameraController : MonoBehaviour
    {
        public static OrbitCameraController Instance { get; private set; }

        [Header("Target & Distances")]
        [SerializeField] private Vector3 _targetPoint = Vector3.zero;
        [SerializeField] private float _distance = 7.0f;
        [SerializeField] private float _minDistance = 1.5f;
        [SerializeField] private float _maxDistance = 25.0f;

        [Header("Orbit Angles (Degrees)")]
        [SerializeField] private float _yaw = 45.0f;
        [SerializeField] private float _pitch = 35.0f;
        [SerializeField] private float _minPitch = 5.0f;
        [SerializeField] private float _maxPitch = 88.0f;

        [Header("Speed & Sensitivity")]
        [SerializeField] private float _orbitSensitivity = 3.5f;
        [SerializeField] private float _zoomSensitivity = 4.0f;
        [SerializeField] private float _panSensitivity = 0.015f;
        [SerializeField] private float _smoothSpeed = 12.0f;

        [Header("Follow Player")]
        [SerializeField] private bool _followPlayer = false;
        [SerializeField] private Transform _playerToFollow;

        private float _currentYaw;
        private float _currentPitch;
        private float _currentDistance;
        private Vector3 _currentTargetPoint;
        private Camera _cam;
        private bool _isOrbitingWithRightClick;

        public Vector3 TargetPoint => _targetPoint;
        public bool IsFollowingPlayer => _followPlayer;

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            _currentYaw = _yaw;
            _currentPitch = _pitch;
            _currentDistance = _distance;
            _currentTargetPoint = _targetPoint;
            UpdateCameraTransform();
        }

        private void LateUpdate()
        {
            HandleInput();
            UpdateCameraTransform();
        }

        private void HandleInput()
        {
            // Do not process orbit if pointer is interacting with UI
            bool isOverUI = false;
            try
            {
                if (UnityEngine.EventSystems.EventSystem.current != null)
                {
                    isOverUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
                }
            }
            catch (System.Exception)
            {
                // StandaloneInputModule throws InvalidOperationException when legacy input is disabled
            }
            if (isOverUI) return;

            bool isRightPressed = false;
            bool isMiddlePressed = false;
            bool isShiftPressed = false;
            Vector2 mouseScreenPos = Vector2.zero;
            float dx = 0f;
            float dy = 0f;
            float scroll = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null)
            {
                mouseScreenPos = mouse.position.ReadValue();
                isRightPressed = mouse.rightButton.isPressed;
                isMiddlePressed = mouse.middleButton.isPressed;
                Vector2 delta = mouse.delta.ReadValue();
                dx = delta.x * 0.15f * _orbitSensitivity;
                dy = delta.y * 0.15f * _orbitSensitivity;
                scroll = mouse.scroll.ReadValue().y * 0.005f;
            }
            if (kb != null)
            {
                isShiftPressed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                mouseScreenPos = Input.mousePosition;
                isRightPressed = Input.GetMouseButton(1);
                isMiddlePressed = Input.GetMouseButton(2);
                isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                dx = Input.GetAxis("Mouse X") * _orbitSensitivity;
                dy = Input.GetAxis("Mouse Y") * _orbitSensitivity;
                scroll = Input.GetAxis("Mouse ScrollWheel");
            }
            catch (System.InvalidOperationException)
            {
                // In case legacy input is disabled
            }
#endif

            // Check if Right-click drag should orbit the camera
            if (isRightPressed)
            {
                if (!_isOrbitingWithRightClick)
                {
                    // Do not start orbit if interacting with whiteboard
                    if (!MRUKSpectator.Whiteboard.WhiteboardDraggable.AnyWhiteboardGrabbed && !IsPointerOverWhiteboard(mouseScreenPos))
                    {
                        _isOrbitingWithRightClick = true;
                    }
                }
            }
            else
            {
                _isOrbitingWithRightClick = false;
            }

            // Orbit: Right click drag (without shift, and not on whiteboard)
            if (_isOrbitingWithRightClick && !isShiftPressed)
            {
                _yaw += dx;
                _pitch -= dy;
                _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

                if (Mathf.Abs(dx) > 0.05f || Mathf.Abs(dy) > 0.05f)
                {
                    _followPlayer = false;
                }
            }

            // Pan: Middle click drag or Shift + Right click drag
            if (isMiddlePressed || (_isOrbitingWithRightClick && isShiftPressed))
            {
                float px = -dx * _panSensitivity * _distance;
                float py = -dy * _panSensitivity * _distance;

                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                _targetPoint += right * px + forward * py;
                _followPlayer = false;
            }

            // Zoom: Scroll Wheel
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _distance -= scroll * _zoomSensitivity;
                _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
            }

            // Follow player if active
            if (_followPlayer && _playerToFollow != null)
            {
                _targetPoint = Vector3.Lerp(_targetPoint, _playerToFollow.position, Time.deltaTime * 5f);
            }
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            float sqr = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sqr < 0.00001f || float.IsNaN(sqr) || float.IsInfinity(sqr)) return Quaternion.identity;
            float inv = 1.0f / Mathf.Sqrt(sqr);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        private bool IsPointerOverWhiteboard(Vector2 mouseScreenPos)
        {
            if (_cam == null) return false;
            Ray ray = _cam.ScreenPointToRay(mouseScreenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                if (hit.collider != null)
                {
                    if (hit.collider.name.IndexOf("Whiteboard", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (hit.collider.transform.parent != null && hit.collider.transform.parent.name.IndexOf("Whiteboard", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                        hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.SpectatorWhiteboard>() != null ||
                        hit.collider.GetComponentInParent<MRUKSpectator.Whiteboard.WhiteboardDraggable>() != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void UpdateCameraTransform()
        {
            // Smooth damp toward target values
            float t = Time.deltaTime * _smoothSpeed;
            _currentYaw = Mathf.Lerp(_currentYaw, _yaw, t);
            _currentPitch = Mathf.Lerp(_currentPitch, _pitch, t);
            _currentDistance = Mathf.Lerp(_currentDistance, _distance, t);
            _currentTargetPoint = Vector3.Lerp(_currentTargetPoint, _targetPoint, t);

            // Compute spherical position around target with strictly normalized orientation
            Quaternion rotation = NormalizeSafe(Quaternion.Euler(_currentPitch, _currentYaw, 0f));
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);

            transform.position = _currentTargetPoint + offset;
            transform.rotation = rotation;
        }

        public void FocusOnBounds(Bounds bounds)
        {
            _targetPoint = bounds.center;
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.z, bounds.size.y);
            _distance = Mathf.Clamp(maxDim * 1.6f, _minDistance, _maxDistance);
            _pitch = 40.0f;
            _yaw = 45.0f;
            _followPlayer = false;
        }

        public void SetTopDownView()
        {
            _pitch = 88.0f;
            _yaw = 0.0f;
        }

        public void SetIsometricView()
        {
            _pitch = 35.0f;
            _yaw = 45.0f;
        }

        public void ToggleFollowPlayer(Transform player)
        {
            _playerToFollow = player;
            _followPlayer = !_followPlayer;
        }

        public void SetFollowPlayer(Transform player, bool follow)
        {
            _playerToFollow = player;
            _followPlayer = follow;
        }
    }
}
