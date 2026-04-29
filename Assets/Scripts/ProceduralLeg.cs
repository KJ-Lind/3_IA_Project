using System.Collections;
using UnityEngine;

public class ProceduralLeg : MonoBehaviour
{
    [System.Serializable]
    public class Leg
    {
        public Transform ikTarget;       // The stationary target on the floor
        public Transform idealFootPos;   // The moving target attached to the body
        public int team;                 // Team 0 or Team 1 (for alternating steps)
        [HideInInspector] public bool isStepping = false;
    }

    [Header("Leg Setup")]
    // 2. An array holding all our legs in one place
    public Leg[] legs;

    [Header("Idle Settling Settings")]
    public float idleThreshold = 0.2f;       // How strict the resting pose should be
    public float timeBeforeIdleAdjust = 0.5f;// How long to stand still before adjusting
    private float idleTimer = 0f;

    [Header("Walking Settings")]
    public float stepThreshold = 1.0f;
    public float stepHeight = 0.5f;
    public float baseStepDuration = 0.25f;

    [Header("Animation Polish")]
    // This creates a visual graph in the Unity Inspector!
    public AnimationCurve stepEasing = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Terrain Detection")]
    public LayerMask groundLayer;          // Tells the raycast what is safe to step on
    public float raycastHeightOffset = 2f; // How high above the ideal spot to start the laser
    public float raycastDistance = 4f;

    private Vector3 lastBodyPos;
    private float currentBodySpeed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        for (int i = 0; i < legs.Length; i++)
            legs[i].ikTarget.position = legs[i].idealFootPos.position;
        lastBodyPos = transform.position;
    }

    // Update is called once per frame
    void Update()
    {

        currentBodySpeed = Vector3.Distance(transform.position, lastBodyPos) / Time.deltaTime;
        lastBodyPos = transform.position;

        bool isMoving = currentBodySpeed > 0.1f;

        if (!isMoving)
        {
            idleTimer += Time.deltaTime; // Start counting up when stopped
        }
        else
        {
            idleTimer = 0f; // Reset the timer immediately if we start moving
        }

        bool isIdle = idleTimer > timeBeforeIdleAdjust;

        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i].isStepping) return;

            if (IsOppositeTeamStepping(legs[i].team)) continue;

            Vector3 rayOrigin = legs[i].idealFootPos.position + (Vector3.up * raycastHeightOffset);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastDistance, groundLayer))
            {
                legs[i].idealFootPos.position = hit.point;
            }

            float distance = Vector3.Distance(legs[i].ikTarget.position, legs[i].idealFootPos.position);

            bool needsWalkingStep = isMoving && distance > stepThreshold;
            bool needsIdleStep = isIdle && distance > idleThreshold;

            if (needsWalkingStep || needsIdleStep)
            {
                StartCoroutine(PerformStep(legs[i], isMoving));
            }
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
        Vector3 endPos = leg.idealFootPos.position;

        float timeElapsed = 0;

        float speedMultiplier = isMoving ? Mathf.Clamp(currentBodySpeed, 1f, 10f) : 1f;
        float dynamicStepDuration = baseStepDuration / speedMultiplier;
        dynamicStepDuration = Mathf.Max(dynamicStepDuration, 0.05f);

        while (timeElapsed < dynamicStepDuration)
        {

            float t = timeElapsed / dynamicStepDuration;

            float easedT = stepEasing.Evaluate(t);

            Vector3 currentPos = Vector3.Lerp(startPos, endPos, easedT);

            currentPos.y += Mathf.Sin(easedT * Mathf.PI) * stepHeight;

            leg.ikTarget.position = currentPos;

            timeElapsed += Time.deltaTime;
            yield return null;
        }
        leg.ikTarget.position = endPos;
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
                Vector3 rayOrigin = leg.idealFootPos.position + (Vector3.up * raycastHeightOffset);
                Gizmos.DrawLine(rayOrigin, rayOrigin + (Vector3.down * raycastDistance));
            }
        }
    }
}