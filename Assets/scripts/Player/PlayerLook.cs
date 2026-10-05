using PurrNet;
using System.Globalization;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    [SerializeField]
    public Camera _cam;

    private float _xRotation = 0f;

    private float _xSensitivity = 30f;
    private float _ySensitivity = 30f;

    public void Start() 
    {

    }

    protected override void OnSpawned()
    {
        if (!isOwner)
            _cam.gameObject.SetActive(false);



        base.OnSpawned();
    }

    public void ProcessLook(Vector2 input)
    {
        if (!isOwner)
            return;

        float mouseX = input.x;
        float mouseY = input.y;

        //looking up and down
        _xRotation -= (mouseY * Time.deltaTime) * _ySensitivity;
        _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);
        //apply this to our cmaera transform
        _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0, 0);
        
        //look left and right
        transform.Rotate(Vector3.up * (mouseX * Time.deltaTime) * _xSensitivity);
    }
}
