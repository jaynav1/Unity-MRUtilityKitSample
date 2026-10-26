using System;
using UnityEngine;
using MRUKSpectator.Network;

namespace MRUKSpectator.Whiteboard
{
    /// <summary>
    /// Enables mixed reality repositioning and synchronization of the SpectatorWhiteboard.
    /// Can be grabbed and repositioned in room-space or aligned to walls.
    /// </summary>
    public class WhiteboardDraggable : MonoBehaviour
    {
        [Header("Interaction")]
        [SerializeField] private bool _allowMovement = true;
        [SerializeField] private float _grabDistance = 0.5f;

        private bool _isGrabbed;
        private Transform _grabbingHand;
        private Vector3 _grabOffset;
        private Quaternion _grabRotOffset;
        private float _lastSyncTime;

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
            if (_isGrabbed) return; // Do not interrupt local manipulation

            transform.localPosition = pose.localPosition;
            transform.localRotation = pose.localRotation;
            if (pose.localScale.sqrMagnitude > 0.001f)
            {
                transform.localScale = pose.localScale;
            }
        }
    }
}
