using UnityEngine;
// 1. Add this namespace at the top
using UnityEngine.InputSystem;

public class SpiderController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float turnSpeed = 150f;

    private Rigidbody rb;
    private Vector2 inputDirection; // Stores WASD data

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
    }

    void Update()
    {
        // 2. Use the New Input System way to read keys
        if (Keyboard.current != null)
        {
            float moveZ = 0;
            if (Keyboard.current.wKey.isPressed) moveZ = 1;
            if (Keyboard.current.sKey.isPressed) moveZ = -1;

            float rotateX = 0;
            if (Keyboard.current.dKey.isPressed) rotateX = 1;
            if (Keyboard.current.aKey.isPressed) rotateX = -1;

            // Apply movement
            Vector3 moveVector = transform.forward * moveZ * moveSpeed * Time.deltaTime;
            transform.Translate(moveVector, Space.World);

            // Apply rotation
            transform.Rotate(Vector3.up, rotateX * turnSpeed * Time.deltaTime);
        }
    }
}