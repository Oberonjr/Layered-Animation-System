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
    [SerializeField] private float headPitchLimits = 40;
    [SerializeField] private float headYawLimits = 70;
    [SerializeField] private float headRollLimits = 20;
    [SerializeField] private AnimationCurve smoothRotationCurve;

    [Header("Torso")] 
    [SerializeField] private Transform torsoBone;
    [SerializeField] private float torsoPitchLimits = 15;
    [SerializeField] private float torsoYawLimits = 30;
    [SerializeField] private float torsoRollLimits = 15;

    [Header("Legs")]
    [SerializeField] private IkLegTurning legBehaviour;
    
    [Header("Rotation parameters")]
    [SerializeField] private float rotationSpeed = 360;
    [SerializeField] private float returnRotationSpeed = 45;
    [SerializeField] private float accelerationTime;
    
    [SerializeField][Range(0, 1)] private float enableLegsThreshold = 0.5f;

    [Space(10)]
    [SerializeField] private Transform testTarget;
    
    [HideInInspector] public bool canStartAnim;
    [HideInInspector] public bool isLookingAtTarget;

    private Transform lookAtTarget;
    
    private Vector3 targetRotation;

    private Vector3 headStart;
    private Vector3 torsoStart;

    private float headCoefficient;
    private float torsoCoefficient;

    private bool isReturning;

    private bool willLegsTurn;
    private bool areLegsTurning;

    private bool hasAccelerated;

    private float actualRotationSpeed;

    private int rotateDirection;

    private void Start()
    {
        float totalClamp = headYawLimits + torsoYawLimits;

        // Calculate how much each body part needs to rotate to achieve a combined target rotation
        headCoefficient = headYawLimits / totalClamp;
        torsoCoefficient = torsoYawLimits / totalClamp;
        
        actualRotationSpeed = 0;
    }

    private void Update()
    {
        /*if(testTarget)
            LookAtContinuous(testTarget);*/
        
            
        LookAtContinuous();
    }

    public void LookAt(Transform lookAtTarget)
    {
        actualRotationSpeed = 0;
        areLegsTurning = false;
        hasAccelerated = false;
        isReturning = false;
        isLookingAtTarget = false;

        // Calculate how much each body part needs to rotate to achieve a combined target rotation
        float totalClamp = headYawLimits + torsoYawLimits;
        headCoefficient = headYawLimits / totalClamp;
        torsoCoefficient = torsoYawLimits / totalClamp;
        
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        Vector3 targetAngle = angle.eulerAngles - (root.eulerAngles + Min(headBone.localEulerAngles) + Min(torsoBone.localEulerAngles));
        
        //Debug.Log("Angle: " + targetRotation + " | Head: " + root.eulerAngles + headBone.localEulerAngles + torsoBone.localEulerAngles + " | Target: " + targetAngle);

        if (!IsClamped(headBone.localEulerAngles.y + targetAngle.y * headCoefficient, headYawLimits)
            || !IsClamped(torsoBone.localEulerAngles.y + targetAngle.y * torsoCoefficient, torsoYawLimits))
        {
            Debug.LogWarning($"headClamp: {headBone.localEulerAngles.y + targetAngle.y * headCoefficient}");
            Debug.LogWarning($"torsoClamp: {torsoBone.localEulerAngles.y + targetAngle.y * torsoCoefficient}");
        }
        
        rotateDirection = targetAngle.y < 0 ? -1 : 1;
        
        if (IsClamped(headBone.localEulerAngles.y + Mathf.Abs(targetAngle.y) * headCoefficient, headYawLimits)
            || IsClamped(torsoBone.localEulerAngles.y +  Mathf.Abs(targetAngle.y) * torsoCoefficient, torsoYawLimits))
        {
            willLegsTurn = true;
        }
        else
        {
            willLegsTurn = false;
        }
        
        //Debug.Log("Legs turn: " + willLegsTurn);
        
        StartCoroutine(AccelerateRotationCo());
        
        this.lookAtTarget = lookAtTarget;
    }

    Vector3 Min(Vector3 a)
    {
        if (a.y > 180)
            return a - new Vector3(0, 360, 0);
        
        return a;
    }

    float Min(float a)
    {
        if (a > 180)
            return a - 360;

        return a;
    }

    public void ClearTarget()
    {
        /*if (testTarget || isDebugging)
            return;*/

        lookAtTarget = null;
        
        //isReturning = true;
        //targetRotation = Quaternion.LookRotation(transform.forward).eulerAngles;
    }

    public void LookAtContinuous()
    {
        Quaternion angle = new Quaternion();
        
        if (lookAtTarget)
            angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);
        
        if(!lookAtTarget || isReturning)
            angle = Quaternion.LookRotation(transform.forward);            
        
        Vector3 lookAngle = root.eulerAngles + Min(headBone.localEulerAngles) + Min(torsoBone.localEulerAngles);
        
        Vector3 targetAngle = angle.eulerAngles - lookAngle;
        
        //Debug.Log("Angle: " + targetRotation + " | Head: " + root.eulerAngles + headBone.localEulerAngles + torsoBone.localEulerAngles + " | Target: " + targetAngle);
        
        //Debug.Log($"headBone: {headBone.localEulerAngles.y}, torsoBone: {torsoBone.localEulerAngles.y}");
        
        if ((IsClamped(headBone.localEulerAngles.y, headYawLimits * enableLegsThreshold)
            || IsClamped(torsoBone.localEulerAngles.y, torsoYawLimits * enableLegsThreshold))
            && willLegsTurn && !areLegsTurning)
        {
            legBehaviour.RotateTowards(angle.eulerAngles, rotateDirection);
            areLegsTurning = true;
            isReturning = true;
            actualRotationSpeed = returnRotationSpeed;
        }


        if (hasAccelerated)
        {
            float accelerationValue;
            
            if (isReturning)
            {
                accelerationValue = Mathf.Abs(targetAngle.y) / (returnRotationSpeed * Time.deltaTime / accelerationTime);
                actualRotationSpeed = returnRotationSpeed * smoothRotationCurve.Evaluate(Mathf.Clamp(accelerationValue, 0, 1));
            }
            else
            {
                accelerationValue = Mathf.Abs(targetAngle.y) / (rotationSpeed * Time.deltaTime / accelerationTime);
                actualRotationSpeed = rotationSpeed * smoothRotationCurve.Evaluate(Mathf.Clamp(accelerationValue, 0, 1));
            }
            
            
            //Debug.Log(accelerationValue + " | " + actualRotationSpeed + " | " + isReturning);
        }
        
        //Debug.Log(accelerationValue);
        
        Vector3 maxRotation = GetDirectionalNormalized(targetAngle) * (actualRotationSpeed * Time.deltaTime);

        if (Mathf.Abs(targetAngle.y) > Mathf.Abs(maxRotation.y))
            targetRotation = maxRotation;
        else
            targetRotation = targetAngle;            
        
        //Debug.Log("Target rotation: " + targetRotation);
        
        RotateHead(targetRotation);
        RotateTorso(targetRotation);
        
        if (Mathf.Abs(angle.eulerAngles.y - lookAngle.y) < rotationSpeed * Time.deltaTime)
        {
            if (!legBehaviour.isRotating && !isLookingAtTarget)
            {
                isLookingAtTarget = true;
                
                ClearTarget();
            }
        }
    }
    
    /*public bool LookAtContinuous(Transform lookAtTarget)
    {
        Quaternion angle = Quaternion.LookRotation(lookAtTarget.position - headBone.position);

        /*if (isNewLookatCommand)
        {
            willLegsTurn = IsClamped(headBone.localEulerAngles.y + angle.eulerAngles.y, headYawLimits);
            isNewLookatCommand = false;
            isReturning = false;
            
            if(isLookingAtTarget)
                isLookingAtTarget = false;
        }#1#
        
        if (isReturning || (!testTarget))
            angle = Quaternion.LookRotation(transform.forward);
        
        canStartAnim = legBehaviour.canStartAnim;
        
        float lookAngle = root.eulerAngles.y + Min(headBone.localEulerAngles.y) + Min(torsoBone.localEulerAngles.y);
        
        Debug.Log("Target angle: " + angle.eulerAngles.y);
        Debug.Log("Current look angle: " + lookAngle);
        
        if (Mathf.Abs(angle.eulerAngles.y - lookAngle) < rotationSpeed * Time.deltaTime)
        {
            if (!legBehaviour.isRotating && !isLookingAtTarget)
            {
                isLookingAtTarget = true;
                actualRotationSpeed = rotationSpeed;
                //isNewLookatCommand = true;
                areLegsTurning = false;
                isReturning = false;
                
                Destroy(testTarget.gameObject);
                
                //Debug.Log("Reset");
                
                return isLookingAtTarget;
            }
        }
        
        if ((IsClamped(headBone.localEulerAngles.y, headYawLimits * enableLegsThreshold) || IsClamped(torsoBone.localEulerAngles.y, torsoYawLimits * enableLegsThreshold)) 
            && willLegsTurn && !areLegsTurning)
        {
            int direction;
            
            if (targetRotation.y < 0)
                direction = -1;
            else
                direction = 1;
            
            Debug.Log("Direction: " + direction);
            
            legBehaviour.RotateTowards(angle.eulerAngles, direction);
            isReturning = true;
            actualRotationSpeed = returnRotationSpeed;

            areLegsTurning = true;
        }
        
        Debug.Log(angle.eulerAngles);

        angle.eulerAngles -= root.eulerAngles + Min(headBone.localEulerAngles) + Min(torsoBone.localEulerAngles);
        
        Vector3 maxRotation = GetDirectionalNormalized(angle.eulerAngles) * (actualRotationSpeed * Time.deltaTime);

        targetRotation = angle.eulerAngles.y < maxRotation.y ? angle.eulerAngles : maxRotation;
        
        RotateHead(targetRotation);
        RotateTorso(targetRotation);
        
        return false;
    }*/

    private void RotateHead(Vector3 targetAngle)
    {
        Vector3 angle = targetAngle * headCoefficient;

        //isHeadClamped = IsClamped(headBone.localEulerAngles.y + angle.y, headYawLimits);

        /*if (isHeadClamped)
            return;*/

        /*Debug.Log( "Head angle: " + angle);
        
        Debug.Log("Clamped angle: " + headBone.localEulerAngles + angle);*/
        
        headBone.localEulerAngles = ClampRotation(headBone.localEulerAngles + angle,
            new Vector3(headPitchLimits, headYawLimits, headRollLimits));
    }
    
    private void RotateTorso(Vector3 targetAngle)
    {
        Vector3 angle = targetAngle * torsoCoefficient;
        
        angle = new Vector3(0, angle.y, 0);
        
        //isTorsoClamped = IsClamped(torsoBone.localEulerAngles.y + angle.y, torsoYawLimits);
        
        torsoBone.localEulerAngles = ClampRotation(torsoBone.localEulerAngles + angle, new Vector3(torsoPitchLimits, torsoYawLimits, torsoRollLimits));
    }

    private IEnumerator AccelerateRotationCo()
    {
        float timeElapsed = 0;
        
        // Keep looping until the end of the headRotationCurve is reached
        while (timeElapsed < smoothRotationCurve[smoothRotationCurve.length - 1].time)
        {
            actualRotationSpeed = rotationSpeed * smoothRotationCurve.Evaluate(timeElapsed);
            
            timeElapsed += Time.deltaTime * (1 / accelerationTime);
            
            yield return null;
        }

        actualRotationSpeed = rotationSpeed;
        hasAccelerated = true;
        
        Debug.Log("Finished look at: " + headBone.eulerAngles);
    }

    // Called by IKSetup during runtime, not to be used anywhere else.
    public void AutoSetup(Transform head, Transform torso)
    {
        root = transform;
        headBone = transform.Find("BodyPivot").GetChild(0);
        torsoBone = transform.Find("BodyPivot").GetChild(1);

        legBehaviour = GetComponent<IkLegTurning>();

        smoothRotationCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
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
        while (angleToCheck < 0)
            angleToCheck += 360;

        while (angleToCheck > 360)
            angleToCheck -= 360;
        
        return angleToCheck >= angleClamp && angleToCheck <= 360 - angleClamp;
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

    public void TestTurnAround()
    {
        //isDebugging = true;
        
        if (!testTarget)
        {
            testTarget = new GameObject("LookAtTestTarget").transform;
            testTarget.position = transform.position + Vector3.up + transform.forward * -2;
            
            LookAt(testTarget);
            return;
        }
        
        Debug.Log("TEST ==================================================================");

        testTarget.position = transform.position + Vector3.up + transform.forward * -2;
        
        LookAt(testTarget);
    }
}