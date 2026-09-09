using UnityEngine;

public class MiniGame1 : Interactable
{
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
        
        Debug.Log($"Interacted with {gameObject.name}");
        // TODO: OPen a minin game and perform a task.
        base.Interact();
    }
}
