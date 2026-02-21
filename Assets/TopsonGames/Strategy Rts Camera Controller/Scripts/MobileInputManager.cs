namespace TopsonGames
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem.EnhancedTouch;
    using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

    public class MobileInputManager : MonoBehaviour
    {
        public static MobileInputManager Instance;

        [Header("References")]
        public CameraController cameraController;

        [Header("Settings")]
        public float panSensitivity = 0.05f;
        public float zoomSensitivity = 0.01f;
        public float rotationSensitivity = 0.4f;
        public float dragThreshold = 20f;

        [Header("Input Actions")]
        public InputActionReference touchPositionAction;
        public InputActionReference touchContactAction;

        private bool isDragging = false;
        private Vector2 startTouchPos;
        private Vector2 lastTouchPos;
        private bool touchStartedOnUI = false;

        private float initialPinchDistance;
        private float lastPinchAngle;
        private bool isMultiTouch = false;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            if (!cameraController) cameraController = FindFirstObjectByType<CameraController>();
        }

        private void OnEnable()
        {
            touchPositionAction?.action.Enable();
            touchContactAction?.action.Enable();
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            touchPositionAction?.action.Disable();
            touchContactAction?.action.Disable();
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            if (ETouch.activeTouches.Count == 0 && !Mouse.current.leftButton.isPressed)
            {
                ResetState();
                return;
            }

            int touchCount = ETouch.activeTouches.Count;
            if (touchCount == 0 && Mouse.current.leftButton.isPressed) touchCount = 1;

            Vector2 currentPos = touchPositionAction.action.ReadValue<Vector2>();

            if (touchCount == 1)
            {
                isMultiTouch = false;

                if (touchContactAction.action.WasPressedThisFrame())
                {
                    touchStartedOnUI = IsPointerOverUI();
                    startTouchPos = currentPos;
                    lastTouchPos = currentPos;
                    isDragging = false;
                }

                if (touchStartedOnUI) return;

                float dragDist = Vector2.Distance(currentPos, startTouchPos);

                if (!isDragging && dragDist > dragThreshold)
                {
                    isDragging = true;
                }

                if (isDragging)
                {
                    Vector2 delta = currentPos - lastTouchPos;
                    cameraController.Mobile_PanCamera(delta * panSensitivity);
                }

                if (touchContactAction.action.WasReleasedThisFrame())
                {
                    touchStartedOnUI = false;
                    isDragging = false;
                }

                lastTouchPos = currentPos;
            }
            else if (touchCount >= 2)
            {
                if (ETouch.activeTouches.Count < 2) return;

                var t1 = ETouch.activeTouches[0];
                var t2 = ETouch.activeTouches[1];

                float currentPinchDist = Vector2.Distance(t1.screenPosition, t2.screenPosition);
                Vector2 diff = t2.screenPosition - t1.screenPosition;
                float currentAngle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

                if (!isMultiTouch)
                {
                    isMultiTouch = true;
                    initialPinchDistance = currentPinchDist;
                    lastPinchAngle = currentAngle;
                }
                else
                {
                    float deltaDist = currentPinchDist - initialPinchDistance;
                    if (Mathf.Abs(deltaDist) > dragThreshold * 0.5f)
                    {
                        cameraController.Mobile_ZoomCamera(deltaDist * zoomSensitivity);
                        initialPinchDistance = currentPinchDist;
                    }

                    float deltaAngle = Mathf.DeltaAngle(lastPinchAngle, currentAngle);
                    if (Mathf.Abs(deltaAngle) > 0.5f) 
                    {
                        cameraController.Mobile_RotateCamera(-deltaAngle * rotationSensitivity);
                        lastPinchAngle = currentAngle;
                    }
                }

                isDragging = true;
            }
        }

        private void ResetState()
        {
            isDragging = false;
            isMultiTouch = false;
            touchStartedOnUI = false;
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;

            if (ETouch.activeTouches.Count > 0)
            {
                return EventSystem.current.IsPointerOverGameObject(ETouch.activeTouches[0].touchId);
            }

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}