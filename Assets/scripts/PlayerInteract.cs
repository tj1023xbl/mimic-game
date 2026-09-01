using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    private Camera cam;

    // Set as serialize so it's visible in the inspector?
    [SerializeField]
    private float interactRange = 3f;

    [SerializeField]
    private LayerMask mask;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = GetComponent<PlayerLook>().cam;
    }

    // Update is called once per frame
    void Update()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * interactRange);
        RaycastHit hitInfo;

        if (Physics.Raycast(ray, out hitInfo, interactRange, mask)) 
        {
            if (hitInfo.collider.GetComponent<Interactable>() is not null)
                Debug.Log(hitInfo.collider.GetComponent<Interactable>().promptMessage);
        }
    }
}
