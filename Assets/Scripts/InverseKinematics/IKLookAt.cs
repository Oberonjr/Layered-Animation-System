using System;
using System.Collections;
using LAS;
using UnityEngine;
using UnityEngine.UIElements;
using DG.Tweening;

public class IKLookAt : MonoBehaviour
{
    [SerializeField] private Transform root;
    
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

    [HideInInspector] public bool canStartAnim;
    
    private Vector3 targetRotation;
    
    private bool isTorsoClamped;
    private bool isLookingAtTarget;

    private float headCoefficient;
    private float torsoCoefficient;

    public void LookAt(Transform lookAtTarget)
    {
        float totalClamp = headYawLimits + torsoYawLimits;

        // Calculate how much each body part needs to rotate to achieve a combined target rotation
        headCoefficient = headYawLimits / totalClamp;
        torsoCoefficient = torsoYawLimits / totalClamp;
        
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        targetRotation = angle.eulerAngles;
        
        Vector3 targetAngle = targetRotation - (root.eulerAngles + Min(headBone.localEulerAngles) + Min(torsoBone.localEulerAngles));
        
        Debug.Log("Angle: " + targetRotation + " | Head: " + root.eulerAngles + headBone.localEulerAngles + torsoBone.localEulerAngles + " | Target: " + targetAngle);
        
        if (IsClamped(headBone.localEulerAngles.y + targetAngle.y * headCoefficient, headYawLimits)
            && IsClamped(torsoBone.localEulerAngles.y + targetAngle.y * torsoCoefficient, torsoYawLimits))
        {
            legBehaviour.RotateTowards(targetRotation);
        }
        else
        {
            StartCoroutine(RotateUpperBodyCo(targetAngle));
        }
    }

    Vector3 Min(Vector3 a)
    {
        if (a.y > 180)
            return a - new Vector3(0, 360, 0);
        
        return a;
    }

    public void ClearTarget()
    {
        targetRotation = Quaternion.LookRotation(transform.forward).eulerAngles;
    }
    
    public bool LookAtContinuous(Transform lookAtTarget)
    {
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        canStartAnim = legBehaviour.canStartAnim;
        
        if (!legBehaviour.isRotating)
        {
            if (Mathf.Abs(angle.eulerAngles.y - Quaternion.LookRotation(headBone.forward).eulerAngles.y) < 5)
            {
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

    private void RotateTorso(Vector3 targetAngle)
    {
        Vector3 angle = targetAngle * 0.3f;
        
        isTorsoClamped = IsClamped(torsoBone.localEulerAngles.y + angle.y, torsoYawLimits);

        if (isTorsoClamped)
            return;
        
        torsoBone.localEulerAngles = ClampRotation(torsoBone.localEulerAngles + angle, new Vector3(torsoPitchLimits, torsoYawLimits, torsoRollLimits));
    }

    private IEnumerator RotateUpperBodyCo(Vector3 targetAngle)
    {
        float timeElapsed = 0;
        Vector3 headStartRotation = headBone.localEulerAngles;
        Vector3 torsoStartRotation = torsoBone.localEulerAngles;
        
        // Keep looping until the end of the headRotationCurve is reached
        while (timeElapsed < 1)
        {
            Vector3 angle = targetAngle * headRotationCurve.Evaluate(timeElapsed);

            headBone.localEulerAngles = headStartRotation + angle * headCoefficient;
            torsoBone.localEulerAngles = torsoStartRotation + angle * torsoCoefficient;
            
            timeElapsed += Time.deltaTime * (rotationSpeed / 360);
            
            yield return null;
        }
        
        headBone.localEulerAngles = headStartRotation + targetAngle * headCoefficient;
        torsoBone.localEulerAngles = torsoStartRotation + targetAngle * torsoCoefficient;
        
        Debug.Log("Finished look at: " + headBone.eulerAngles);
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