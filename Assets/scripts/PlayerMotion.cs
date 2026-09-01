using UnityEngine;

public class PlayerMotion : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;

    // Gravity + jump
    private bool isGrounded;
    public float gravity = -9.8f;
    public float jumpHeight = 0.5f;

    // Crouching
    public bool crouching = false;
    public bool lerpCrouch = false;
    public float crouchTimer = 0f;

    // Movement speed
    public bool sprinting = false;
    public float speed = 5f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    // Update is called once per frame
    void Update()
    {
        isGrounded = controller.isGrounded;
        
        crouchTimer += Time.deltaTime;
        float p = crouchTimer / 1;
        p *= p;

        if(crouching)
            controller.height = Mathf.Lerp(controller.height, 1, p);
        else
            controller.height = Mathf.Lerp(controller.height, 2, p);

        if(p > 1)
        {
            lerpCrouch = false;
            crouchTimer = 0f;
        }

    }

    public void ProcessMove(Vector3 input)
    {
        Vector3 moveDirection = Vector3.zero;
        moveDirection.x = input.x;
        moveDirection.z = input.y;
        controller.Move(transform.TransformDirection(moveDirection) * speed * Time.deltaTime);
        playerVelocity.y += gravity * Time.deltaTime;

        if(isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;
        controller.Move(playerVelocity * Time.deltaTime);
        Debug.Log(playerVelocity.y);
    }

    public void Jump()
    {
        if(isGrounded)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -0.5f * gravity);
        }
    }

    public void Crouch()
    {
        crouching = !crouching;
        crouchTimer = 0f;
        lerpCrouch = true;
    }

    public void Sprint()
    {
        sprinting = !sprinting;
        if(sprinting)
            speed = 8f;
        else
            speed = 5f;
    }
}
