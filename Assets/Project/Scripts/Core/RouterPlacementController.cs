using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Project.LinuxOS;

namespace Project.LarpCore
{
    /// <summary>
    /// Механика 3D-плейсмента (размещения) первого роутера в квартире:
    /// предпросмотр под курсором на столах и поверхностях квартиры, поворот на R, установка на ЛКМ.
    /// </summary>
    public class RouterPlacementController : MonoBehaviour
    {
        public static RouterPlacementController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private bool _isPlacing;
        private GameObject _ghostRouter;
        private GameObject _placedRouter;
        private Renderer[] _ghostRenderers;
        private float _yawAngle;
        private bool _validSpot;
        private Vector3 _targetPos;

        private Action _onPlacedCallback;

        public bool IsPlacing => _isPlacing;
        public bool IsRouterPlaced => _placedRouter != null;

        public static RouterPlacementController EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<RouterPlacementController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("RouterPlacementController");
            return go.AddComponent<RouterPlacementController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void ResetPlacement()
        {
            _isPlacing = false;
            _onPlacedCallback = null;
            if (_ghostRouter != null) Destroy(_ghostRouter);
            if (_placedRouter != null) Destroy(_placedRouter);
            _ghostRouter = null;
            _placedRouter = null;
            PlayerState.Instance?.SetRouterPlaced(false);
        }

        public void BeginPlacement(Action onPlaced)
        {
            if (_placedRouter != null)
            {
                Destroy(_placedRouter);
                _placedRouter = null;
            }

            if (_ghostRouter == null)
            {
                _ghostRouter = CreateRouterVisual("RouterPlacementGhost", isGhost: true);
                _ghostRenderers = _ghostRouter.GetComponentsInChildren<Renderer>();
            }

            _ghostRouter.SetActive(true);
            _yawAngle = 0f;
            _validSpot = false;
            _isPlacing = true;
            _onPlacedCallback = onPlaced;

            GameManager.Instance?.PostToast("Режим установки роутера: наведите на стол или пол, [R] — повернуть, [ЛКМ] — поставить.");
        }

        private void Update()
        {
            if (!_isPlacing || _ghostRouter == null)
                return;

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                _yawAngle = (_yawAngle + 45f) % 360f;
                AudioFeedbackManager.Instance?.PlayClick();
            }

            if (Mouse.current == null || Camera.main == null)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 120f))
            {
                // Разрешаем установку на горизонтальные поверхности (столы, тумбы, пол) внутри квартиры
                bool horizontal = hit.normal.y > 0.75f;
                bool insideFlat = hit.point.x >= 0.5f && hit.point.x <= 20.5f && hit.point.z >= 0.5f && hit.point.z <= 20.5f;
                _validSpot = horizontal && insideFlat;
                _targetPos = hit.point + Vector3.up * 0.03f;

                _ghostRouter.transform.position = _targetPos;
                _ghostRouter.transform.rotation = Quaternion.Euler(0f, _yawAngle, 0f);
                ApplyGhostTint(_validSpot ? new Color(0.22f, 0.82f, 0.36f, 0.85f) : new Color(0.86f, 0.24f, 0.24f, 0.85f));
            }
            else
            {
                _validSpot = false;
            }

            if (_validSpot && Mouse.current.leftButton.wasPressedThisFrame)
            {
                ConfirmPlacement();
            }
        }

        private void ConfirmPlacement()
        {
            _isPlacing = false;
            if (_ghostRouter != null)
                _ghostRouter.SetActive(false);

            _placedRouter = CreateRouterVisual("Placed_HomeRouter", isGhost: false);
            _placedRouter.transform.position = _targetPos;
            _placedRouter.transform.rotation = Quaternion.Euler(0f, _yawAngle, 0f);

            PlayerState.Instance?.SetRouterPlaced(true);
            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            GameManager.Instance?.PostToast("Роутер установлен! Домашняя открытая Wi-Fi сеть активирована.");

            Action cb = _onPlacedCallback;
            _onPlacedCallback = null;
            cb?.Invoke();
        }

        private void ApplyGhostTint(Color tint)
        {
            if (_ghostRenderers == null) return;
            for (int i = 0; i < _ghostRenderers.Length; i++)
            {
                if (_ghostRenderers[i] == null) continue;
                var mat = _ghostRenderers[i].material;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            }
        }

        private static GameObject CreateRouterVisual(string name, bool isGhost)
        {
            var root = new GameObject(name);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material MakeMat(Color c)
            {
                var m = new Material(urpLit);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                return m;
            }

            Material bodyMat = MakeMat(new Color(0.11f, 0.12f, 0.14f, 1f));
            Material antMat = MakeMat(new Color(0.18f, 0.19f, 0.22f, 1f));
            Material ledMat = MakeMat(new Color(0.20f, 0.88f, 0.36f, 1f));

            void BuildBox(string partName, Vector3 localPos, Vector3 localScale, Vector3 localEuler, Material mat)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = partName;
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = localPos;
                p.transform.localScale = localScale;
                p.transform.localEulerAngles = localEuler;
                p.GetComponent<Renderer>().sharedMaterial = mat;
                var col = p.GetComponent<Collider>();
                if (isGhost && col != null) Destroy(col);
            }

            // Корпус роутера
            BuildBox("Chassis", new Vector3(0f, 0.03f, 0f), new Vector3(0.32f, 0.06f, 0.20f), Vector3.zero, bodyMat);
            // Две антенны сзади
            BuildBox("Antenna_L", new Vector3(-0.11f, 0.16f, 0.08f), new Vector3(0.02f, 0.24f, 0.02f), new Vector3(-8f, 0f, 6f), antMat);
            BuildBox("Antenna_R", new Vector3(0.11f, 0.16f, 0.08f), new Vector3(0.02f, 0.24f, 0.02f), new Vector3(-8f, 0f, -6f), antMat);
            // Индикаторы питания/Wi-Fi на передней грани
            BuildBox("Led_Power", new Vector3(-0.08f, 0.035f, -0.102f), new Vector3(0.03f, 0.015f, 0.01f), Vector3.zero, ledMat);
            BuildBox("Led_Wifi", new Vector3(-0.03f, 0.035f, -0.102f), new Vector3(0.03f, 0.015f, 0.01f), Vector3.zero, ledMat);

            return root;
        }
    }
}
