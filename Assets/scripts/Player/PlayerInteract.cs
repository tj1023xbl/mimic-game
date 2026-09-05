using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    private Camera cam;
    private PlayerUI playerUI;
    private InputManager inputManager;

    // Set as serialize so it's visible in the inspector?
    [SerializeField]
    private float interactRange = 3f;

    [SerializeField]
    private LayerMask mask;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = GetComponent<PlayerLook>().cam;
        playerUI = GetComponent<PlayerUI>();
        inputManager = GetComponent<InputManager>();
    }

    // Update is called once per frame
    void Update()
    {
        playerUI.UpdateText(string.Empty);

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * interactRange);
        RaycastHit hitInfo;

        if (Physics.Raycast(ray, out hitInfo, interactRange, mask)) 
        {
            if (hitInfo.collider.GetComponent<Interactable>() is not null) 
            {
                Interactable interactable = hitInfo.collider.GetComponent<Interactable>();
                playerUI.UpdateText(interactable.promptMessage);

                if (inputManager.onFoot.Interact.triggered) 
                {
                    interactable.BaseInteract();
                }
            }    
        }
    }
}
