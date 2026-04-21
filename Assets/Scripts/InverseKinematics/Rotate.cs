using UnityEngine;

namespace LAS
{
    public class Rotate : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed;
        
        void Update()
        {
            transform.Rotate(0, Time.deltaTime * rotationSpeed, 0);
        }
    }
}
