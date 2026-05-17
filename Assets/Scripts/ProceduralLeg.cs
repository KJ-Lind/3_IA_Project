using System.Collections;
using UnityEngine;
using static ProceduralLeg;

public class ProceduralLeg : MonoBehaviour
{
    [System.Serializable]
    public class Leg
    {
        public Transform ikTarget;
        public Transform idealFootPos;
        public int team;

        [HideInInspector] public bool isStepping = false;
        [HideInInspector] public Vector3 currentGroundTarget;

        [HideInInspector] public float stepProgress = 0f;
        [HideInInspector] public Vector3 stepStartPos;
        [HideInInspector] public Vector3 stepEndPos;

        [HideInInspector] public float currentStepDuration;
        [HideInInspector] public float currentStepHeight;
    }

    public Leg[] legs;


    [Header("Walking Settings")]
    public float walkStepDuration = 0.25f;
    public float walkStepHeight = 0.5f;
    public float walkStepThreshold = 1.0f;
    public AnimationCurve stepEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Turning Settings")]
    public float slowTurnDuration = 0.2f;       // Step time when turning gently
    public float fastTurnDuration = 0.08f;      // Step time when spinning violently
    public float turnStepHeight = 0.2f;
    public float wideTurnThreshold = 0.6f;      // Step distance when turning gently
    public float tightTurnThreshold = 0.2f;     // Step distance when spinning violently
    public float maxTurnVelocity = 120f;

    [Header("Idle Settings")]
    public float idleStepDuration = 0.5f;  // Slower, relaxed step for adjusting
    public float idleStepHeight = 0.3f;
    public float idleThreshold = 0.2f;
    public float timeBeforeIdleAdjust = 0.5f;
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
    private float currentBodySpeed;

    [Header("WallWalking")]
    public float gravityAlignSpeed = 5.0f;
    private Vector3 surfaceNormal = Vector3.up;

    [Header("Turning Juice")]
    public float bankMultiplier = 0.2f;
    public float maxBankAngle = 25f;
    public float bankSmoothSpeed = 8f;

    private float currentBank = 0f;
    private float currentYawVelocity;
    private Quaternion lastBodyRot;


    private int lastSteppingTeam = -1;

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


    private void LateUpdate()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            if (!legs[i].isStepping) continue;

            // Advance progress based on the CACHED duration for this specific step
            legs[i].stepProgress += Time.deltaTime / legs[i].currentStepDuration;

            if (legs[i].stepProgress >= 1f)
            {
                legs[i].isStepping = false;
                legs[i].ikTarget.position = legs[i].stepEndPos;

                Vector3 finalProjectedForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal).normalized;
                if (finalProjectedForward != Vector3.zero)
                {
                    legs[i].ikTarget.rotation = Quaternion.LookRotation(finalProjectedForward, surfaceNormal);
                }
                continue;
            }

            float easedT = stepEasing.Evaluate(legs[i].stepProgress);
            Vector3 currentPos = Vector3.Lerp(legs[i].stepStartPos, legs[i].stepEndPos, easedT);

            // Apply the CACHED step height for this specific step
            currentPos += surfaceNormal * (Mathf.Sin(easedT * Mathf.PI) * legs[i].currentStepHeight);

            legs[i].ikTarget.position = currentPos;

            Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal).normalized;
            if (projectedForward != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(projectedForward, surfaceNormal);
                legs[i].ikTarget.rotation = Quaternion.Slerp(legs[i].ikTarget.rotation, targetRot, easedT);
            }
        }
    }

    void Update()
    {
        UpdateSurfaceNormal();

        currentBodySpeed = Vector3.Distance(transform.position, lastBodyPos) / Time.deltaTime;
        float deltaYaw = Mathf.DeltaAngle(lastBodyRot.eulerAngles.y, transform.rotation.eulerAngles.y);
        currentYawVelocity = deltaYaw / Time.deltaTime;

        // Determine the core state of the crawler this frame
        bool isWalking = currentBodySpeed > 0.05f;
        bool isTurningInPlace = !isWalking && Mathf.Abs(currentYawVelocity) > 2f;
        bool isIdle = !isWalking && !isTurningInPlace;

        float turnIntensity = Mathf.Clamp01(Mathf.Abs(currentYawVelocity) / maxTurnVelocity);
        float dynamicTurnThreshold = Mathf.Lerp(wideTurnThreshold, tightTurnThreshold, turnIntensity);

        if (isIdle) idleTimer += Time.deltaTime;
        else idleTimer = 0f;

        bool isLongIdle = idleTimer > timeBeforeIdleAdjust;
        bool[] teamWantsStep = new bool[2];

        // 1. EVALUATE DISTANCE THRESHOLDS BASED ON STATE
        for (int i = 0; i < legs.Length; i++)
        {
            Vector3 rayOrigin = legs[i].idealFootPos.position + (surfaceNormal * raycastHeightOffset);
            if (Physics.SphereCast(rayOrigin, sphereRadius, -surfaceNormal, out RaycastHit hit, raycastDistance, groundLayer))
            {
                legs[i].currentGroundTarget = hit.point;
            }

            float distance = Vector3.Distance(legs[i].ikTarget.position, legs[i].currentGroundTarget);

            bool wantsStep = false;

            // Strict threshold gating based on current state
            if (isWalking && distance > walkStepThreshold) wantsStep = true;
            else if (isTurningInPlace && distance > dynamicTurnThreshold) wantsStep = true;
            else if (isIdle && idleTimer > timeBeforeIdleAdjust && distance > idleThreshold) wantsStep = true;

            if (wantsStep)
            {
                teamWantsStep[legs[i].team] = true;
            }
        }
        bool isAnyLegStepping = false;
        foreach (var leg in legs)
        {
            if (leg.isStepping) isAnyLegStepping = true;
        }

        // Only allow a new team to step if NO legs are currently mid-air
        if (!isAnyLegStepping)
        {
            int teamToStep = -1;

            // Anti-Starvation: Force alternation if both teams are queued to step
            if (teamWantsStep[0] && teamWantsStep[1])
            {
                teamToStep = lastSteppingTeam == 0 ? 1 : 0;
            }
            else if (teamWantsStep[0]) teamToStep = 0;
            else if (teamWantsStep[1]) teamToStep = 1;

            // Trigger the winning team
            if (teamToStep != -1)
            {
                for (int i = 0; i < legs.Length; i++)
                {
                    if (legs[i].team == teamToStep)
                    {
                        legs[i].isStepping = true;
                        legs[i].stepProgress = 0f;
                        legs[i].stepStartPos = legs[i].ikTarget.position;
                        legs[i].stepEndPos = legs[i].currentGroundTarget;

                        // Apply specific cached settings based on state
                        if (isTurningInPlace)
                        {
                            legs[i].currentStepDuration = Mathf.Lerp(slowTurnDuration, fastTurnDuration, turnIntensity);
                            legs[i].currentStepHeight = turnStepHeight;
                        }
                        else if (isIdle) // <-- NEW IDLE LOGIC
                        {
                            legs[i].currentStepDuration = idleStepDuration;
                            legs[i].currentStepHeight = idleStepHeight;
                        }
                        else
                        {
                            // If walking or idle, use standard walking parameters
                            float speedMultiplier = isWalking ? Mathf.Clamp(currentBodySpeed, 1.0f, 10.0f) : 1.0f;
                            legs[i].currentStepDuration = walkStepDuration / speedMultiplier;
                            legs[i].currentStepHeight = walkStepHeight;
                        }
                    }
                }

                lastSteppingTeam = teamToStep; // Remember who stepped
                if (isIdle) idleTimer = 0f;    // Reset the idle timer ONLY if we successfully settled our feet
            }
        }

        CalculateBodyOrientation();

        lastBodyPos = transform.position;
        lastBodyRot = transform.rotation;
    }

    void CalculateBodyOrientation()
    {
        if (bodyMesh == null || legs.Length == 0) return;

        float speedMultiplier = currentBodySpeed > 0.1f ? Mathf.Clamp(currentBodySpeed, 1.0f, 5.0f) : 1.0f;
        float dynamicTiltSpeed = tiltSpeed * speedMultiplier;

        Vector3 avgFootPos = Vector3.zero;
        foreach (Leg leg in legs)
        {
            avgFootPos += leg.ikTarget.position;
        }
        avgFootPos /= legs.Length;

        Vector3 targetPosition = avgFootPos + surfaceNormal * bodyHeight;
        bodyMesh.position = Vector3.Lerp(bodyMesh.position, targetPosition, Time.deltaTime * dynamicTiltSpeed);

        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal).normalized;

        if (projectedForward.sqrMagnitude < 0.01f)
        {
            projectedForward = Vector3.ProjectOnPlane(transform.up, surfaceNormal).normalized;
        }

        if (projectedForward != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(projectedForward, surfaceNormal);

            float targetBank = 0f;
            if (currentBodySpeed > 0.5f)
            {
                targetBank = Mathf.Clamp(-currentYawVelocity * bankMultiplier, -maxBankAngle, maxBankAngle);
            }
            currentBank = Mathf.Lerp(currentBank, targetBank, Time.deltaTime * bankSmoothSpeed);

            Quaternion rollOffset = Quaternion.AngleAxis(currentBank, projectedForward);
            targetRotation = rollOffset * targetRotation;

            bodyMesh.rotation = Quaternion.Slerp(bodyMesh.rotation, targetRotation, Time.deltaTime * tiltSpeed);
        }
    }
    //bool IsOppositeTeamStepping(int myTeam)
    //{
    //    foreach (Leg leg in legs)
    //    {
    //        if (leg.team != myTeam && leg.isStepping)
    //        {
    //            return true;
    //        }
    //    }
    //    return false;
    //}

    //IEnumerator PerformStep(Leg leg, bool isMoving)
    //{
    //    leg.isStepping = true;
    //    Vector3 startPos = leg.ikTarget.position;

    //    // We always target the latest ground hit
    //    Vector3 endPos = leg.currentGroundTarget;

    //    float timeElapsed = 0;
    //    float speedMultiplier = isMoving ? Mathf.Clamp(currentBodySpeed, 1f, 10f) : 1f;
    //    float dynamicStepDuration = baseStepDuration / speedMultiplier;

    //    while (timeElapsed < dynamicStepDuration)
    //    {
    //        float t = timeElapsed / dynamicStepDuration;
    //        float easedT = stepEasing.Evaluate(t);

    //        // Note: During rotation, 'leg.currentGroundTarget' is moving every frame. 
    //        // To prevent the foot from "sliding" while in the air, we Lerp to the target 
    //        // detected at the START of the step, or update it slightly.
    //        Vector3 currentPos = Vector3.Lerp(startPos, endPos, easedT);
    //        currentPos += surfaceNormal * (Mathf.Sin(easedT * Mathf.PI) * stepHeight);

    //        leg.ikTarget.position = currentPos;
    //        timeElapsed += Time.deltaTime;
    //        yield return null;
    //    }

    //    leg.ikTarget.position = leg.currentGroundTarget;
    //    leg.isStepping = false;
    //}
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
                Vector3 forwardOrigin = transform.position + (surfaceNormal * 0.5f);

                Gizmos.DrawLine(rayCastOrigin, rayCastOrigin + (-normal * raycastDistance));
                Gizmos.DrawWireSphere(forwardOrigin, bodyRadius);
                Gizmos.DrawWireSphere(SphereOrigin, sphereRadius);
            }
        }
    }

    void UpdateSurfaceNormal()
    {
        Vector3 moveDirection = currentBodySpeed > 0.1f ? (transform.position - lastBodyPos).normalized : transform.forward;

        // Down ray cast for floor detection
        Vector3 downOrigin = transform.position + (surfaceNormal * raycastHeightOffset);
        bool hitDown = Physics.SphereCast(downOrigin, bodyRadius, -surfaceNormal, out RaycastHit downHit, raycastDistance, groundLayer);

        //Forward ray cast for 90º wall detection
        Vector3 forwardOrigin = transform.position + (surfaceNormal * 0.5f);
        bool hitForward = Physics.SphereCast(forwardOrigin, bodyRadius, moveDirection, out RaycastHit forwardHit, lookaheadDistance + 0.5f, groundLayer);

        Vector3 targetNormal = surfaceNormal;

        if (hitForward)
        {
            targetNormal = forwardHit.normal;
        }
        else if (hitDown)
        {
            targetNormal = downHit.normal;
        }

        surfaceNormal = Vector3.Lerp(surfaceNormal, targetNormal, Time.deltaTime * gravityAlignSpeed).normalized;
    }
}