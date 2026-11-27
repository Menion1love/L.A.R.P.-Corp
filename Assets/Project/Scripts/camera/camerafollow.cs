using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Цель")]
    [SerializeField] private GameObject target;

    [Header("Настройки дистанции")]
    [SerializeField] private float distance = 18f; // Расстояние от камеры до игрока
    [SerializeField] private float heightOffset = 1f; // Смещение точки фокуса вверх (например, на уровень груди, а не ног)

    [Header("Плавность")]
    [SerializeField] private float smoothSpeed = 10f;

    [Header("Выравнивание горизонта")]
    [SerializeField] private bool autoLevelHorizon = true;

    private bool _followEnabled = true;
    private Quaternion _roomRotation;

    public bool FollowEnabled => _followEnabled;
    public Quaternion RoomRotation => _roomRotation;

    private void Awake()
    {
        if (autoLevelHorizon)
        {
            Vector3 euler = transform.eulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)) > 0.5f)
            {
                euler.z = 0f;
                if (euler.x < 25f)
                    euler.x = 32f;
                transform.rotation = Quaternion.Euler(euler);
            }
        }

        _roomRotation = transform.rotation;
    }

    private void Start()
    {
        if (target != null && _followEnabled)
        {
            SnapToTarget();
        }
    }

    public void SetFollowEnabled(bool enabled)
    {
        _followEnabled = enabled;
    }

    public Vector3 GetDesiredPosition()
    {
        if (target == null)
            return transform.position;

        Vector3 focusPoint = target.transform.position + Vector3.up * heightOffset;
        Vector3 forward = _roomRotation * Vector3.forward;
        return focusPoint - forward * distance;
    }

    public void SnapToTarget()
    {
        if (target == null) return;
        transform.rotation = _roomRotation;
        transform.position = GetDesiredPosition();
    }

    private void LateUpdate()
    {
        if (!_followEnabled || target == null) return;

        // Точка на персонаже, в которую смотрит центр камеры
        Vector3 focusPoint = target.transform.position + Vector3.up * heightOffset;

        // Вычисляем позицию камеры: отступаем от точки фокуса строго назад по направлению взгляда камеры
        Vector3 desiredPosition = focusPoint - transform.forward * distance;

        // Плавно перемещаем камеру
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
