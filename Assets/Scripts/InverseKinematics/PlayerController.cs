using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LAS
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerInput map;
        [SerializeField] private Camera cam;

        [SerializeField] private float moveSpeed;

        [SerializeField] private float sensitivity;

        private Rigidbody rigidbody;

        private Vector3 inputDirection;

        private float xCam, yCam;

        private void Start()
        {
            rigidbody = GetComponent<Rigidbody>();

            map.actions.Enable();
            map.actions["Move"].performed += Move;
            map.actions["Move"].canceled += StopMove;
            
            Cursor.lockState = CursorLockMode.Locked;

        }

        private void LateUpdate()
        {
            MoveCamera(map.actions["Look"].ReadValue<Vector2>());
        }

        private void Move(InputAction.CallbackContext ctx)
        {
            inputDirection = ctx.ReadValue<Vector2>();
            inputDirection = transform.right * inputDirection.x + transform.forward * inputDirection.y;
        }
        
        private void StopMove(InputAction.CallbackContext ctx)
        {
            inputDirection = Vector3.zero;
        }

        private void MoveCamera(Vector2 delta)
        {
            //xCam = Mouse.current.position.ReadValue().y * -1;
            xCam += delta.y * Time.deltaTime * -sensitivity;

            xCam = Mathf.Clamp(xCam, -90, 90);
            
            //yCam = Mouse.current.position.ReadValue().x;
            yCam += delta.x * Time.deltaTime * sensitivity;
            
            cam.transform.localEulerAngles = new Vector3(xCam, 0, 0);
            transform.eulerAngles = new Vector3(0,  yCam, 0);
        }
        
        private void FixedUpdate()
        {
            rigidbody.AddForce(inputDirection * moveSpeed);
        }
    }
}
