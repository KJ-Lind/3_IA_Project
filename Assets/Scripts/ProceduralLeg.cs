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
            Vector3 rayOrigin = legs[i].idealFootPos.position + (-surfaceNormal * -raycastHeightOffset);
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

        // 1. Calculate Body Height based on average foot height
        float averageFootY = 0f;
        foreach (Leg leg in legs)
        {
            avgFootPos += leg.ikTarget.position;
        }
        avgFootPos /= legs.Length;

        Vector3 targetPosition = avgFootPos + surfaceNormal * bodyHeight;
        bodyMesh.position = Vector3.Lerp(bodyMesh.position, targetPosition, Time.deltaTime * tiltSpeed);

        // 2. Calculate Body Tilt (Pitch and Roll) based on actual foot placements
        Vector3 frontFeetAvg = Vector3.zero; int frontCount = 0;
        Vector3 backFeetAvg = Vector3.zero; int backCount = 0;
        Vector3 leftFeetAvg = Vector3.zero; int leftCount = 0;
        Vector3 rightFeetAvg = Vector3.zero; int rightCount = 0;

        foreach (Leg leg in legs)
        {
            // Determine where the leg is located relative to the crawler's center
            Vector3 localHomePos = transform.InverseTransformPoint(leg.idealFootPos.position);

            // Group feet by Z axis (Front/Back) and X axis (Left/Right)
            if (localHomePos.z > 0) { frontFeetAvg += leg.ikTarget.position; frontCount++; }
            else { backFeetAvg += leg.ikTarget.position; backCount++; }

            if (localHomePos.x > 0) { rightFeetAvg += leg.ikTarget.position; rightCount++; }
            else { leftFeetAvg += leg.ikTarget.position; leftCount++; }
        }
        Vector3 rayOrigin = transform.position + (moveDirection * lookaheadDistance) + (surfaceNormal * raycastHeightOffset);

        // Average the grouped positions
        if (frontCount > 0) frontFeetAvg /= frontCount;
        if (backCount > 0) backFeetAvg /= backCount;
        if (leftCount > 0) leftFeetAvg /= leftCount;
        if (rightCount > 0) rightFeetAvg /= rightCount;

        // Get the vectors connecting opposite sides of the crawler
        Vector3 bodyForwardSlope = frontFeetAvg - backFeetAvg;
        Vector3 bodyRightSlope = rightFeetAvg - leftFeetAvg;

        // The Cross Product of these two slopes gives us the exact upward angle of the terrain under the feet
        Vector3 terrainUp = Vector3.Cross(bodyForwardSlope, bodyRightSlope).normalized;

        // Safety check to ensure the normal is always pointing up, not upside down
        if (terrainUp.y < 0) terrainUp = -terrainUp;

        // 3. Apply the rotation smoothly
        // Project our forward direction onto the new terrain plane so the crawler doesn't pitch weirdly on steep slopes
        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, terrainUp);

        Quaternion targetRotation = Quaternion.LookRotation(projectedForward, terrainUp);
        bodyMesh.rotation = Quaternion.Slerp(bodyMesh.rotation, targetRotation, Time.deltaTime * tiltSpeed);
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
                Vector3 normal = Application.isPlaying ? surfaceNormal : Vector3.up ;
                Vector3 rayCastOrigin = leg.idealFootPos.position + (Vector3.up * raycastHeightOffset);
                Vector3 SphereOrigin = leg.idealFootPos.position;
                Gizmos.DrawLine(rayCastOrigin, rayCastOrigin + (-normal * raycastDistance)); // He cambiado donde se printea la sphere. TODO: ver si tengo que cambiar de donde sale el sphereCast
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
