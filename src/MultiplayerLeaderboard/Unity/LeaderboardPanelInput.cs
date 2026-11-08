using System;
using System.Collections.Generic;
using MultiplayerLeaderboard.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRUIControls;

namespace MultiplayerLeaderboard.Unity
{
    /// <summary>
    /// Receives the game's existing pointer events for the leaderboard panel. Beat Saber's VRInputModule
    /// selects this object as the drag handler; this class only translates the panel along a plane that
    /// is fixed to its current rotation. It never writes a rotation value.
    /// </summary>
    internal sealed class LeaderboardPanelInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                                     IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        private WorldLeaderboardPanel _panel;
        private Plane _dragPlane;
        private Vector3 _grabOffset;
        private bool _dragging;
        private Plane _rotationPlane;
        private Vector3 _rotationLastVector;
        private bool _rotating;
        private bool _rotationUsesRightButton;

        internal void Initialize(WorldLeaderboardPanel panel)
        {
            _panel = panel;
        }

        private void Update()
        {
            if (_panel == null)
            {
                return;
            }

            if (_panel.EditMode)
            {
                _panel.EnsureEditor();
            }

            // Some Beat Saber builds raycast the world-space panel correctly but never dispatch the
            // pointer-down event to its graphic. Read the same VRPointer selection the game uses and
            // start the drag from the desktop trigger, while OnPointerDown remains the VR path.
            if (!_dragging && !_rotating && Input.GetMouseButtonDown(0) && _panel.EditMode)
            {
                GameObject target = PointerTarget();
                if (_panel.IsPointerTarget(target))
                {
                    if (_panel.RotationEditing)
                    {
                        TryBeginRotation(null, false);
                    }
                    else if (_panel.DragEnabled)
                    {
                        TryBeginDrag(null);
                    }
                }
                else if (target != null)
                {
                    _panel.TryClickEditor(target);
                }
            }

            // Right-click is a direct desktop shortcut for rotation even when pointer rotation
            // mode is not active. It leaves ordinary left-button positioning untouched.
            if (!_dragging && !_rotating && Input.GetMouseButtonDown(1) && _panel.EditMode)
            {
                GameObject target = PointerTarget();
                if (_panel.IsPointerTarget(target))
                {
                    TryBeginRotation(null, true);
                }
            }

            if (_dragging && Input.GetMouseButtonUp(0))
            {
                EndDrag();
            }

            if (_rotating)
            {
                UpdateRotationFromPointer();
                bool released = _rotationUsesRightButton ? Input.GetMouseButtonUp(1) : Input.GetMouseButtonUp(0);
                if (released)
                {
                    EndRotation();
                }

                // The wheel gives the pointer method a third axis (roll) without taking over the
                // ordinary depth control used while translating the panel.
                float rotationWheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(rotationWheel) > 0.01f)
                {
                    _panel.RotateLocal(0f, 0f, rotationWheel * 4f);
                }
            }

            if (_dragging)
            {
                UpdateDragFromPointer();

                // The desktop mouse wheel is the depth control while dragging. The VR thumbstick is
                // delivered through OnScroll by the game's VRInputModule.
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    _panel.MoveDepth(wheel * 0.05f);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_dragging && !_rotating)
            {
                bool rotate = _panel != null && (_panel.RotationEditing ||
                                                 eventData.button == PointerEventData.InputButton.Right);
                if (rotate)
                {
                    TryBeginRotation(eventData, eventData.button == PointerEventData.InputButton.Right);
                }
                else
                {
                    TryBeginDrag(eventData);
                }
            }
        }

        private GameObject PointerTarget()
        {
            try
            {
                EventSystem eventSystem = EventSystem.current;
                VRInputModule module = eventSystem == null ? null : eventSystem.currentInputModule as VRInputModule;
                if (module == null || module.vrPointer == null || _panel == null)
                {
                    return null;
                }

                return module.vrPointer.pointingOver;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the pointer target failed: " + ex.GetType().Name);
                return null;
            }
        }

        private void TryBeginDrag(PointerEventData eventData)
        {
            _dragging = false;
            if (_panel == null || !_panel.EditMode || !_panel.DragEnabled)
            {
                return;
            }

            _dragPlane = new Plane(_panel.WorldRotation * Vector3.forward, _panel.WorldPosition);
            if (!TryGetPointerRay(eventData, out Ray ray))
            {
                LeaderboardLog.Warn("Could not read the laser pointer ray; the leaderboard was not dragged.");
                return;
            }

            if (!_dragPlane.Raycast(ray, out float distance))
            {
                LeaderboardLog.Warn("The laser pointer ray is parallel to the leaderboard; the drag was not started.");
                return;
            }

            Vector3 hit = ray.GetPoint(distance);
            _grabOffset = _panel.WorldPosition - hit;
            _dragging = true;
            LeaderboardLog.Info("Leaderboard drag started at " + hit + "; rotation remains " + _panel.WorldRotation +
                                ".");
        }

        private void UpdateDragFromPointer()
        {
            if (!_dragging || _panel == null)
            {
                return;
            }

            if (!TryGetPointerRay(null, out Ray ray))
            {
                return;
            }

            if (_dragPlane.Raycast(ray, out float distance))
            {
                _panel.SetWorldPosition(ray.GetPoint(distance) + _grabOffset);
            }
        }

        private void TryBeginRotation(PointerEventData eventData, bool rightButton)
        {
            _rotating = false;
            if (_panel == null || !_panel.EditMode)
            {
                return;
            }

            if (!TryGetPointerRay(eventData, out Ray ray))
            {
                LeaderboardLog.Warn("Could not read the laser pointer ray; pointer rotation was not started.");
                return;
            }

            Vector3 normal = -(_panel.WorldRotation * Vector3.forward);
            Camera camera = Camera.main;
            if (camera == null && _panel.Canvas != null)
            {
                camera = _panel.Canvas.worldCamera;
            }

            if (camera != null)
            {
                normal = -camera.transform.forward;
            }

            if (normal.sqrMagnitude < 0.001f)
            {
                normal = -(_panel.WorldRotation * Vector3.forward);
            }

            _rotationPlane = new Plane(normal.normalized, _panel.WorldPosition);
            if (!_rotationPlane.Raycast(ray, out float distance))
            {
                LeaderboardLog.Warn("The laser pointer ray is parallel to the rotation plane; rotation was not started.");
                return;
            }

            Vector3 vector = ray.GetPoint(distance) - _panel.WorldPosition;
            if (vector.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _rotationLastVector = vector.normalized;
            _rotationUsesRightButton = rightButton;
            _rotating = true;
            LeaderboardLog.Info("Leaderboard pointer rotation started at " + ray.GetPoint(distance) +
                                "; position remains " + _panel.WorldPosition + ".");
        }

        private void UpdateRotationFromPointer()
        {
            if (!_rotating || _panel == null || !TryGetPointerRay(null, out Ray ray))
            {
                return;
            }

            if (!_rotationPlane.Raycast(ray, out float distance))
            {
                return;
            }

            Vector3 vector = ray.GetPoint(distance) - _panel.WorldPosition;
            if (vector.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 current = vector.normalized;
            if (Vector3.Dot(_rotationLastVector, current) > 0.99999f)
            {
                return;
            }

            Quaternion delta = Quaternion.FromToRotation(_rotationLastVector, current);
            _panel.ApplyWorldRotation(delta);
            _rotationLastVector = current;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndDrag();
            EndRotation();
        }

        private void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (_panel != null)
            {
                _panel.LogDragFinished();
            }
        }

        private void EndRotation()
        {
            if (!_rotating)
            {
                return;
            }

            _rotating = false;
            if (_panel != null)
            {
                _panel.LogRotationFinished();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_panel != null)
            {
                _panel.SetHover(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_panel != null)
            {
                _panel.SetHover(false);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_panel != null && _rotating)
            {
                _panel.RotateLocal(0f, 0f, eventData.scrollDelta.y * 4f);
            }
            else if (_panel != null && _dragging)
            {
                _panel.MoveDepth(eventData.scrollDelta.y * 0.05f);
            }
        }

        private bool TryGetPointerRay(PointerEventData eventData, out Ray ray)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                VRInputModule module = eventSystem.currentInputModule as VRInputModule;
                if (module != null && module.vrPointer != null)
                {
                    VRController controller = module.vrPointer.lastSelectedVrController;
                    if (controller != null && controller.viewAnchorTransform != null)
                    {
                        ray = new Ray(controller.viewAnchorTransform.position,
                                      controller.viewAnchorTransform.forward);
                        return true;
                    }
                }
            }

            Camera camera = Camera.main;
            if (camera == null && _panel != null && _panel.Canvas != null)
            {
                camera = _panel.Canvas.worldCamera;
            }

            if (camera != null && eventData != null && !float.IsNaN(eventData.position.x) &&
                !float.IsInfinity(eventData.position.x) && eventData.position.x < 10000f)
            {
                ray = camera.ScreenPointToRay(eventData.position);
                return true;
            }

            ray = default(Ray);
            return false;
        }

    }
}
