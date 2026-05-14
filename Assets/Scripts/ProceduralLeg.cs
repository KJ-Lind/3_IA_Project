using System.Collections;
using UnityEngine;

public class ProceduralLeg : MonoBehaviour
{
    [System.Serializable]
    public class Leg
    {
        public Transform ikTarget;
        public Transform idealFootPos;
        public int team;
        [HideInInspector] public bool isStepping = false;
        [HideInInspector] public Vector3 currentGroundTarget; // Store the raycast hit here
    }

    public Leg[] legs;

    [Header("Movement Thresholds")]
    public float stepThreshold = 1.0f;
    public float turnThreshold = 15f; // New: Degrees of rotation before stepping
    public float idleThreshold = 0.2f;

    [Header("Walking Settings")]
    public float stepHeight = 0.5f;
    public float baseStepDuration = 0.25f;
    public AnimationCurve stepEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Idle Settings")]
    public float timeBeforeIdleAdjust = 0.5f; // How long to wait before settling
    private float idleTimer = 0f;

    [Header("Terrain")]
    public LayerMask groundLayer;
    public float raycastHeightOffset = 2f;
    public float raycastDistance = 10f;
    public float sphereRadius = 2.0f;

    [Header("Body")]
    public Transform bodyMesh;
    public float bodyHeight = 1.0f;
    public float tiltSpeed = 8.0f;
    public float lookaheadDistance = 1.0f;
    public float bodyRadius = 0.5f;

    private Vector3 lastBodyPos;
    private Quaternion lastBodyRot; // Track rotation
    private float currentBodySpeed;

    [Header("WallWalking")]
    public float gravityAlignSpeed = 5.0f;
    private Vector3 surfaceNormal = Vector3.up;

    void Start()
    {
        lastBodyRot = transform.rotation;
        for (int i = 0; i < legs.Length; i++)
        {
            legs[i].ikTarget.position = legs[i].idealFootPos.position;
            legs[i].currentGroundTarget = legs[i].idealFootPos.position;
        }
        lastBodyPos = transform.position;
    }

    void Update()
    {
        UpdateSurfaceNormal();

        // 1. Calculate Linear and Angular movement
        currentBodySpeed = Vector3.Distance(transform.position, lastBodyPos) / Time.deltaTime;
        float angleDelta = Quaternion.Angle(transform.rotation, lastBodyRot); // Difference in degrees

        bool isMoving = currentBodySpeed > 0.1f || angleDelta > 0.1f;

        if (!isMoving) idleTimer += Time.deltaTime;
        else idleTimer = 0f;

        bool isIdle = idleTimer > timeBeforeIdleAdjust;

        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i].isStepping) continue;
            if (IsOppositeTeamStepping(legs[i].team)) continue;

            // 2. Find the ground point based on the CURRENT position/rotation of the idealFootPos
            Vector3 rayOrigin = legs[i].idealFootPos.position + (surfaceNormal * raycastHeightOffset);
 
            if (Physics.SphereCast(rayOrigin, 0.1f, -surfaceNormal, out RaycastHit hit, raycastDistance, groundLayer))
            {
                legs[i].currentGroundTarget = hit.point;
            }

            // 3. Check distance to current target
            float distance = Vector3.Distance(legs[i].ikTarget.position, legs[i].currentGroundTarget);

            // Step if moving too far, OR if we rotated past the threshold
            bool needsWalkingStep = isMoving && (distance > stepThreshold || angleDelta > turnThreshold);
            bool needsIdleStep = isIdle && distance > idleThreshold;

            if (needsWalkingStep || needsIdleStep)
            {
                StartCoroutine(PerformStep(legs[i], isMoving));
            }
        }
        CalculateBodyOrientation();

        lastBodyPos = transform.position;
        lastBodyRot = transform.rotation; // Update rotation for next frame
    }

    void CalculateBodyOrientation()
    {
        if (bodyMesh == null || legs.Length == 0) return;

        Vector3 avgFootPos = Vector3.zero;
        foreach (Leg leg in legs)
        {
            avgFootPos += leg.ikTarget.position;
        }
        avgFootPos /= legs.Length;

        Vector3 targetPosition = avgFootPos + surfaceNormal * bodyHeight;
        bodyMesh.position = Vector3.Lerp(bodyMesh.position, targetPosition, Time.deltaTime * tiltSpeed);

        Vector3 moveDirection = (transform.position - lastBodyPos).normalized;
        if (currentBodySpeed < 0.1f)
        {
            moveDirection = transform.forward;
        }
        Vector3 rayOrigin = transform.position + (moveDirection * lookaheadDistance) + (surfaceNormal * raycastHeightOffset);

        if (Physics.SphereCast(rayOrigin, bodyRadius, -surfaceNormal, out RaycastHit hit, raycastDistance, groundLayer))
        {
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
            bodyMesh.rotation = Quaternion.Slerp(bodyMesh.rotation, targetRotation, Time.deltaTime * tiltSpeed);
        }
    }
    bool IsOppositeTeamStepping(int myTeam)
    {
        foreach (Leg leg in legs)
        {
            if (leg.team != myTeam && leg.isStepping)
            {
                return true; // Someone on the other team is moving! Freeze!
            }
        }
        return false;
    }

    IEnumerator PerformStep(Leg leg, bool isMoving)
    {
        leg.isStepping = true;
        Vector3 startPos = leg.ikTarget.position;

        // We always target the latest ground hit
        Vector3 endPos = leg.currentGroundTarget;

        float timeElapsed = 0;
        float speedMultiplier = isMoving ? Mathf.Clamp(currentBodySpeed, 1f, 10f) : 1f;
        float dynamicStepDuration = baseStepDuration / speedMultiplier;

        while (timeElapsed < dynamicStepDuration)
        {
            float t = timeElapsed / dynamicStepDuration;
            float easedT = stepEasing.Evaluate(t);

            // Note: During rotation, 'leg.currentGroundTarget' is moving every frame. 
            // To prevent the foot from "sliding" while in the air, we Lerp to the target 
            // detected at the START of the step, or update it slightly.
            Vector3 currentPos = Vector3.Lerp(startPos, leg.currentGroundTarget, easedT);
            currentPos += surfaceNormal * (Mathf.Sin(easedT * Mathf.PI) * stepHeight);

            leg.ikTarget.position = currentPos;
            timeElapsed += Time.deltaTime;
            yield return null;
        }

        leg.ikTarget.position = leg.currentGroundTarget;
        leg.isStepping = false;
    }
    private void OnDrawGizmos()
    {
        if (legs == null) return;
        Gizmos.color = Color.green;
        foreach (Leg leg in legs)
        {
            if (leg.idealFootPos != null)
            {
                Vector3 normal = Application.isPlaying ? surfaceNormal : Vector3.up;
                Vector3 rayCastOrigin = leg.idealFootPos.position + (Vector3.up * raycastHeightOffset);
                Vector3 SphereOrigin = leg.idealFootPos.position + (surfaceNormal * raycastHeightOffset);
                Gizmos.DrawLine(rayCastOrigin, rayCastOrigin + (-normal * raycastDistance));
                Gizmos.DrawWireSphere(SphereOrigin, sphereRadius);
            }
        }
    }

    void UpdateSurfaceNormal()
    {
        Vector3 castOrigin = transform.position + surfaceNormal * raycastHeightOffset;

        if (Physics.SphereCast(castOrigin, bodyRadius, -surfaceNormal, out RaycastHit hit,
            raycastDistance, groundLayer))
        {
            surfaceNormal = Vector3.Lerp(surfaceNormal, hit.normal, Time.deltaTime * gravityAlignSpeed);
            surfaceNormal.Normalize();
        }
    }
}