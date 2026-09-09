using UnityEngine;

public class DoorButton : Interactable
{
    [SerializeField]
    private GameObject door;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Not Implemented
    }

    // Update is called once per frame
    void Update()
    {
        // Not Implemented
    }

    protected override void Interact() 
    {
        bool isOpen = door.GetComponent<Animator>().GetBool("isOpen");
        isOpen = !isOpen;
        door.GetComponent<Animator>().SetBool("isOpen", isOpen);
        Debug.Log($"Interacted with {gameObject.name}: {isOpen}");
        base.Interact();
    }
}
