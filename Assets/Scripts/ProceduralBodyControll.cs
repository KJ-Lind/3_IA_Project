using UnityEngine;

public class ProceduralBodyController : MonoBehaviour
{
    [Header("Legs")]
    public Transform[] legIKTargets;    // Drag all your IK foot targets here

    [Header("Body Settings")]
    public float targetHeightOffset = 1.0f;  // How high the body should hover above the feet
    public float smoothSpeed = 8f;           // How quickly the body adapts to new heights

    void Update()
    {
        if (legIKTargets.Length == 0) return;

        float averageY = 0f;

        // 1. Sum up the vertical heights of every foot
        foreach (Transform target in legIKTargets)
        {
            averageY += target.position.y;
        }

        // 2. Divide by total legs to find the average ground level
        averageY /= legIKTargets.Length;

        // 3. Set the new body target, keeping X and Z the same but altering Y
        Vector3 targetBodyPos = transform.position;
        targetBodyPos.y = averageY + targetHeightOffset;

        // 4. Smoothly interpolate the body to match the terrain beneath it
        transform.position = Vector3.Lerp(transform.position, targetBodyPos, Time.deltaTime * smoothSpeed);
    }
}