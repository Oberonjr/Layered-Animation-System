using System;
using UnityEngine;

public class IKLookAt : MonoBehaviour
{
    [SerializeField] private Transform headBone;
    [SerializeField] private float headPitchLimits;
    [SerializeField] private float headYawLimits;
    [SerializeField] private float headRollLimits;

    [SerializeField] private Transform target;
    
    private Quaternion angle;


    private float baseYaw = 90;
    private float baseRoll = 270;
    
    private void Update()
    {
        LookAt(target);
    }

    public void LookAt(Transform target)
    {
        angle = Quaternion.LookRotation(target.position - headBone.position);

        //Debug.Log(angle.eulerAngles);
    }

    private void LateUpdate()
    {
        headBone.rotation = ToModelCoordinates(angle);
    }


    private Quaternion ToModelCoordinates(Quaternion rotation)
    {
        Quaternion output = new Quaternion();
        Quaternion modifier = new Quaternion();
        modifier.eulerAngles = new Vector3(0, 90, -90);
        
        modifier.eulerAngles += new Vector3(0, rotation.eulerAngles.y, rotation.eulerAngles.x);

        output.eulerAngles = ClampHeadRotation(modifier.eulerAngles);

        return output;
    }

    private Vector3 ClampHeadRotation(Vector3 vectorToClamp)
    {
        Debug.Log(vectorToClamp);
        Debug.Log(vectorToClamp.y - baseYaw + "Yaw");
        Debug.Log(vectorToClamp.z - baseRoll + "Roll");
        
        return new Vector3(
            Mathf.Clamp(vectorToClamp.x, -headPitchLimits, headPitchLimits),
            Mathf.Clamp(vectorToClamp.y - baseYaw, headYawLimits, headYawLimits) + baseYaw,
            Mathf.Clamp(vectorToClamp.z - baseRoll, headRollLimits, headRollLimits) + baseRoll);
    }
}
