using System.Collections.Generic;
using UnityEngine;

namespace MRUKSpectator.Visualization
{
    public class PlayerAvatarMarker : MonoBehaviour
    {
        [Header("Interpolation")]
        [SerializeField] private float _lerpSpeed = 16.0f;

        [Header("Visuals")]
        [SerializeField] private Color _markerColor = new Color(0.12f, 0.75f, 1.0f, 1.0f);
        [SerializeField] private bool _enableTrail = true;
        [SerializeField] private float _trailTime = 12.0f;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private TrailRenderer _trailRenderer;
        private Transform _headVisual;
        private Transform _pointerVisual;
        private Transform _floorRing;

        public Vector3 CurrentPosition => transform.position;
        public Quaternion CurrentRotation => transform.rotation;

        private void Awake()
        {
            BuildProceduralMarker();
        }

        private void BuildProceduralMarker()
        {
            // Head visual (represents VR headset)
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "HeadsetVisual";
            head.transform.SetParent(transform, false);
            head.transform.localScale = new Vector3(0.24f, 0.14f, 0.18f);
            Destroy(head.GetComponent<Collider>());
            _headVisual = head.transform;

            // Direction pointer / visor
            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "VisorPointer";
            visor.transform.SetParent(head.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            visor.transform.localScale = new Vector3(0.8f, 0.5f, 0.4f);
            Destroy(visor.GetComponent<Collider>());
            _pointerVisual = visor.transform;

            // Floor projection ring
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "FloorRing";
            ring.transform.SetParent(transform, false);
            ring.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);
            Destroy(ring.GetComponent<Collider>());
            _floorRing = ring.transform;

            // Materials
            Material headMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            headMat.color = _markerColor;
            head.GetComponent<Renderer>().material = headMat;

            Material visorMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            visorMat.color = Color.white;
            visor.GetComponent<Renderer>().material = visorMat;

            Material ringMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            ringMat.color = new Color(_markerColor.r, _markerColor.g, _markerColor.b, 0.4f);
            ring.GetComponent<Renderer>().material = ringMat;

            // Path trail
            if (_enableTrail)
            {
                _trailRenderer = gameObject.AddComponent<TrailRenderer>();
                _trailRenderer.time = _trailTime;
                _trailRenderer.startWidth = 0.08f;
                _trailRenderer.endWidth = 0.0f;
                _trailRenderer.material = ringMat;
                _trailRenderer.minVertexDistance = 0.05f;
            }
        }

        public void UpdateTargetPose(Vector3 roomLocalPosition, Quaternion roomLocalRotation)
        {
            _targetPosition = roomLocalPosition;
            if (roomLocalRotation.x != 0 || roomLocalRotation.y != 0 || roomLocalRotation.z != 0 || roomLocalRotation.w != 0)
            {
                _targetRotation = roomLocalRotation.normalized;
            }
        }

        private void Update()
        {
            // Smoothly interpolate position and rotation
            transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * _lerpSpeed);
            if (Quaternion.Angle(transform.rotation, _targetRotation) > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * _lerpSpeed);
            }

            // Keep the floor ring pinned to y=0 (floor level)
            if (_floorRing != null)
            {
                _floorRing.position = new Vector3(transform.position.x, 0.01f, transform.position.z);
                _floorRing.rotation = Quaternion.identity;
            }
        }

        public void ClearTrail()
        {
            if (_trailRenderer != null)
            {
                _trailRenderer.Clear();
            }
        }
    }
}
