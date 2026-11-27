using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Project.LarpCore;
using Project.LinuxOS;

[RequireComponent(typeof(NavMeshAgent))]
public class Move : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayer;

    private NavMeshAgent agent;
    private Camera mainCamera;
    private Renderer[] cachedRenderers;

    private Action _pendingArrivalCallback;
    private Vector3 _pendingTargetPoint;
    private float _pendingArrivalDistance = 1.6f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        mainCamera = Camera.main;
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Update()
    {
        if (GameManager.Instance != null && (GameManager.Instance.IsModalOpen || GameManager.Instance.IsGameOver))
            return;

        if (PlayerStateController.Instance != null &&
            (PlayerStateController.Instance.IsAtComputer || PlayerStateController.Instance.IsFlying))
            return;

        if (_pendingArrivalCallback != null)
        {
            Vector2 flatDelta = new Vector2(
                transform.position.x - _pendingTargetPoint.x,
                transform.position.z - _pendingTargetPoint.z);

            bool arrived = flatDelta.magnitude <= _pendingArrivalDistance ||
                           (agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= _pendingArrivalDistance);

            if (arrived)
            {
                Action callback = _pendingArrivalCallback;
                _pendingArrivalCallback = null;
                StopMovement();
                callback?.Invoke();
                return;
            }
        }

        // Проверяем, подключена ли мышь и зажата ли правая кнопка (rightButton)
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            _pendingArrivalCallback = null;
            MoveToCursor();
        }
    }

    public void MoveToPoint(Vector3 worldPoint, Action onArrived = null, float arrivalDistance = 1.6f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            onArrived?.Invoke();
            return;
        }

        Vector3 target = worldPoint;
        if (NavMesh.SamplePosition(worldPoint, out NavMeshHit navHit, 3.5f, NavMesh.AllAreas))
        {
            target = navHit.position;
        }

        _pendingTargetPoint = target;
        _pendingArrivalDistance = Mathf.Max(0.8f, arrivalDistance);
        _pendingArrivalCallback = onArrived;
        agent.isStopped = false;
        agent.SetDestination(target);
    }

    public void StopMovement()
    {
        _pendingArrivalCallback = null;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    public void SetVisualsVisible(bool visible)
    {
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = visible;
        }
    }

    private void MoveToCursor()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null || agent == null || !agent.isOnNavMesh)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.point);
        }
    }
}
