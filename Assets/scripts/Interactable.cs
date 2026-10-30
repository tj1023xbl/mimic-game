using PurrNet;
using UnityEngine;

public abstract class Interactable : NetworkBehaviour
{
    public string promptMessage;

    public void BaseInteract() 
    {
        Interact();
    }

    protected virtual void Interact() 
    {
        
    }
}
