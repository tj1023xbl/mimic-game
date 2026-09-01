using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{

    private PlayerInput playerInput;
    private PlayerInput.OnFootActions onFoot;

    private PlayerMotion motion;
    private PlayerLook look;

    // Runs on startup? Like a constructor?
    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;

        // Get base components
        motion = GetComponent<PlayerMotion>();
        look = GetComponent<PlayerLook>();

        // Subscribe to keyboard events to trigger player actions
        onFoot.Jump.performed += ctx => motion.Jump();
        onFoot.Crouch.performed += ctx => motion.Crouch();
        onFoot.Sprint.performed += ctx => motion.Sprint();
    }

    void start()
    {
        
    }

    // Update is called on a predictable interval. (defaults 0.02 seconds)
    void FixedUpdate()
    {
        motion.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
    }

    void LateUpdate()
    {
        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnEnable()
    {
        onFoot.Enable();
    }

    private void OnDisable()
    {
        onFoot.Disable();
    }
}
