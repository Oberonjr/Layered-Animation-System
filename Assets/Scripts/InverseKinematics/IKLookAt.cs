using System;
using LAS;
using UnityEngine;
using UnityEngine.UIElements;
using DG.Tweening;

public class IKLookAt : MonoBehaviour
{
    [SerializeField] private Transform bodyTransform;
    
    [Header("Head")]
    [SerializeField] private Transform headBone;
    [SerializeField] private float headPitchLimits;
    [SerializeField] private float headYawLimits;
    [SerializeField] private float headRollLimits;
    [SerializeField] private AnimationCurve headRotationCurve;
    private bool isHeadClamped;

    [Header("Torso")] 
    [SerializeField] private Transform torsoBone;
    [SerializeField] private float torsoPitchLimits;
    [SerializeField] private float torsoYawLimits;
    [SerializeField] private float torsoRollLimits;

    [Header("Legs")]
    [SerializeField] private IkLegTurning legBehaviour;
    
    [Space(10)]
    [SerializeField] private float rotationSpeed;

    [SerializeField] private Transform testTarget;

    private bool isTorsoClamped;

    private Vector3 targetRotation;
    
    private bool isLookingAtTarget;

    private bool isRotating;

    private void Update()
    {
        //LookAtContinuous(testTarget);

        //transform.position = Vector3.MoveTowards(transform.position, torsoBone.forward * 100, Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (isLookingAtTarget)
            return;
        
        RotateHead(targetRotation);
        RotateTorso(targetRotation);
    }

    public bool LookAt(Transform lookAtTarget)
    {
        if (isRotating)
            return false;
        
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        if (Mathf.Abs(angle.eulerAngles.y - Quaternion.LookRotation(headBone.forward).eulerAngles.y) < 5)
        {
            //Debug.Log("Look at returns true");
            isLookingAtTarget = true;
            return isLookingAtTarget;
        }

        if (isHeadClamped)
            return true;
        
        // Reset isLookingAtTarget if the above check fails
        if(isLookingAtTarget)
            isLookingAtTarget = false;
        
        Vector3 targetAngle = angle.eulerAngles - Quaternion.LookRotation(headBone.forward).eulerAngles;
        
        //targetRotation = angle.eulerAngles - Quaternion.LookRotation(headBone.forward).eulerAngles;
        
        DORotateHead(targetAngle);

        return false;
    }

    public void ClearTarget()
    {
        //targetRotation = Quaternion.LookRotation(bodyTransform.forward).eulerAngles;
    }
    
    public bool LookAtContinuous(Transform lookAtTarget)
    {
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        if (!legBehaviour.isRotating)
        {
            if (Mathf.Abs(angle.eulerAngles.y - Quaternion.LookRotation(headBone.forward).eulerAngles.y) < 5)
            {
                //Debug.Log("Look at returns true");
                isLookingAtTarget = true;
                return isLookingAtTarget;
            }
        }
        
        if(isLookingAtTarget)
            isLookingAtTarget = false;

        if (isHeadClamped && isTorsoClamped)
            legBehaviour.RotateTowards(angle.eulerAngles);
        
        angle.eulerAngles -= Quaternion.LookRotation(headBone.forward).eulerAngles;

        Vector3 maxRotation = GetDirectionalNormalized(angle.eulerAngles) * (rotationSpeed * Time.deltaTime);

        if(angle.eulerAngles.magnitude < maxRotation.magnitude)
            targetRotation = angle.eulerAngles;
        else
            targetRotation = maxRotation;

        return false;
    }

    private void RotateHead(Vector3 targetAngle)
    {
        Vector3 angle = targetAngle * 0.7f;
        
        isHeadClamped = IsClamped(headBone.localEulerAngles.y + angle.y, headYawLimits);

        if (isHeadClamped)
            return;
        
        headBone.localEulerAngles = ClampRotation(headBone.localEulerAngles + angle, new Vector3(headPitchLimits, headYawLimits, headRollLimits));
    }

    private void DORotateHead(Vector3 targetAngle)
    {
        isRotating = true;
        Vector3 angle = targetAngle * 0.7f;

        if (isHeadClamped)
            return;
        
        Debug.Log(angle);
        
        isHeadClamped = IsClamped(headBone.localEulerAngles.y + angle.y, headYawLimits);
        
        angle = ClampRotation(headBone.localEulerAngles + angle,
            new Vector3(headPitchLimits, headYawLimits, headRollLimits));

        Debug.Log(angle);
        
        headBone.DORotate(angle, rotationSpeed, RotateMode.FastBeyond360).SetSpeedBased(true).OnComplete(() =>
        {
            Debug.Log(headBone.localEulerAngles);
            isRotating = false;
        });
    }

    private void RotateTorso(Vector3 targetAngle)
    {
        Vector3 angle = targetAngle * 0.3f;
        
        isTorsoClamped = IsClamped(torsoBone.localEulerAngles.y + angle.y, torsoYawLimits);

        if (isTorsoClamped)
            return;
        
        torsoBone.localEulerAngles = ClampRotation(torsoBone.localEulerAngles + angle, new Vector3(torsoPitchLimits, torsoYawLimits, torsoRollLimits));
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

    private Vector3 GetDirectionalNormalized(Vector3 vectorToNormalize)
    {
        Vector3 normalized = vectorToNormalize.normalized;

        float x, y, z;

        // if the rotation values are between 180 and 360 degrees, target is on the left
        x = vectorToNormalize.x > 180 ? -1 : 1;
        y = vectorToNormalize.y > 180 ? -1 : 1;
        z = vectorToNormalize.z > 180 ? -1 : 1;
        
        return new Vector3(normalized.x * x, normalized.y * y, normalized.z * z);
    }

    public void EnableLegIK(bool value)
    {
        legBehaviour.EnableLegIk(value);
    }
}