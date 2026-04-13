using System;
using UnityEngine;
using UnityEngine.UIElements;

public class IKLookAt : MonoBehaviour
{
    [Header("Head")]
    [SerializeField] private Transform headBone;
    [SerializeField] private float headPitchLimits;
    [SerializeField] private float headYawLimits;
    [SerializeField] private float headRollLimits;
    private bool isHeadClamped;

    [Header("Torso")] 
    [SerializeField] private Transform torsoBone;
    [SerializeField] private float torsoPitchLimits;
    [SerializeField] private float torsoYawLimits;
    [SerializeField] private float torsoRollLimits;
    
    [Space(10)]
    [SerializeField] private float rotationSpeed;

    private bool isTorsoClamped;
    private bool isTorsoCentered = true;

    private Vector3 targetRotation;
    
    private void LateUpdate()
    {
        RotateHead(targetRotation);
        
        if(isHeadClamped || !isTorsoCentered)
            RotateTorso(targetRotation - headBone.localEulerAngles);
    }

    public void LookAt(Transform lookAtTarget)
    {
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position + Vector3.up - headBone.position);

        targetRotation = angle.eulerAngles - Quaternion.LookRotation(transform.forward).eulerAngles;
    }

    /// <summary>Returns the head and torso to their neutral (forward-facing) rotation.</summary>
    public void ClearTarget()
    {
        targetRotation = Vector3.zero;
    }

    private void RotateHead(Vector3 angle)
    {
        isHeadClamped = IsClamped(angle.y, headYawLimits);

        headBone.localEulerAngles = ClampRotation(angle, new Vector3(headPitchLimits, headYawLimits, headRollLimits));
    }

    private void RotateTorso(Vector3 angle)
    {
        torsoBone.localEulerAngles = ClampRotation(angle, new Vector3(torsoPitchLimits, torsoYawLimits, torsoRollLimits));
        
        isTorsoCentered = torsoBone.localEulerAngles == Vector3.zero;
    }

    private Vector3 ClampRotation(Vector3 vectorToClamp, Vector3 clampLimits)
    {
        return new Vector3(
            ClampRotationAxis(vectorToClamp.x, clampLimits.x),
            ClampRotationAxis(vectorToClamp.y, clampLimits.y),
            ClampRotationAxis(vectorToClamp.z, clampLimits.z));
    }

    //Clamps rotation in a 360 degree system (e.g. between 270 and 90 degrees / -90 and 90 degrees)
    private float ClampRotationAxis(float angleToClamp, float clampLimit)
    {
        if(angleToClamp < 0)
            angleToClamp += 360;
        
        float outputAngle;
        if(angleToClamp <= 360 - clampLimit && angleToClamp > 180)
            outputAngle = 360 - clampLimit;
        else if (angleToClamp >= clampLimit && angleToClamp < 180)
            outputAngle = clampLimit;
        else
            outputAngle = angleToClamp;
        
        return outputAngle;
    }

    private bool IsClamped(float angleToCheck, float angleClamp)
    {
        return angleToCheck > angleClamp && angleToCheck < 360 - angleClamp;
    }
}
