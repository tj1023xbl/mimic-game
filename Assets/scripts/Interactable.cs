using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public string promptMessage;

    public void BaseInteract() 
    {
        Interact();
    }

    public virtual void Interact() 
    {
        
    }
}
