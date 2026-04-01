using System.Collections;
using LAS;
using UnityEngine;

public class IkLegBehaviour : MonoBehaviour
{
    [SerializeField] private Animator animator;
    
    [Space(10)]
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform leftTarget;
    [SerializeField] private AnimationCurve footVerticalMovement;
    
    [Space(10)]
    [SerializeField] private Transform rightFoot;
    [SerializeField] private Transform rightTarget;

    [Space(10)] 
    [SerializeField] private Transform twoBoneLeft;
    [SerializeField] private Transform twoBoneRight;

    [Space(10)] 
    [SerializeField] private Transform feetPivot;
    // minimum torso/pivot rotation before moving feet
    [SerializeField] private float minimumRotation;
    // maxminum torso/pivot rotation before planting feet on ground.
    [SerializeField] private float maximumRotation;

    [SerializeField] private float rotationSpeed;
    
    // < 0 == left foot
    // > 0 == right foot

    private Foot foot;
    private Foot foot2;
    
    private Vector3 leftPosition;
    private Vector3 rightPosition;

    private bool isMovingFoot = false;
    private bool moveOtherFoot = true;

    private float currentRotationBounds;

    private float startRotation;
    private float rotationDelta;

    private float deltaValue;

    private int rotationDirection = 1;
    private float lastRotationValue;

    private float lerpDuration = 0.5f;

    private bool isRotatingBody;

    private void Awake()
    {
        foot = new Foot(leftFoot, leftTarget, twoBoneLeft);
        foot2 = new Foot(rightFoot, rightTarget, twoBoneRight);

        currentRotationBounds = minimumRotation;
    }

    private void Update()
    {
        deltaValue = 0;
        float currentRotationValue = Mathf.Abs(feetPivot.eulerAngles.y);
        
        if(lastRotationValue >= 360 - rotationSpeed && currentRotationValue <= rotationSpeed)
            deltaValue = 360 - lastRotationValue + currentRotationValue;
        else if (lastRotationValue < 90 && currentRotationValue > 270)
            deltaValue = (360 - currentRotationValue) * -1 - lastRotationValue;
        else
            deltaValue = currentRotationValue - lastRotationValue;
                
        rotationDelta += deltaValue;

        if (feetPivot.eulerAngles.y < lastRotationValue)
            rotationDirection = -1;
        else
            rotationDirection = 1;
        
        lastRotationValue =  feetPivot.eulerAngles.y;

        if (!isMovingFoot && rotationDelta > 30)
        {
            // Right foot first
            if(rotationDirection > 0)
                StartCoroutine(LerpPositionCo(foot2));
            // Left foot first
            else if (rotationDirection < 0)
                StartCoroutine(LerpPositionCo(foot));
        }
    }

    public void RotateTowards(Vector3 angleToTarget)
    {
        if (angleToTarget.y < 0)
            MoveLeftFoot();
        else
            MoveRightFoot();
        
        
    }

    private void MoveRightFoot()
    {
    }

    public void MoveLeftFoot()
    {
    }
    
    private IEnumerator LerpPositionCo(Foot foot)
    {
        isMovingFoot = true;
        Debug.Log("Started foot position lerp: " + foot.transform.name);

        float rotatedAmount = rotationDelta;

        float target = feetPivot.rotation.eulerAngles.y;
        
        float timeElapsed = 0;

        foot.constraint.position += new Vector3(0, 0.05f, 0);

        while (timeElapsed < lerpDuration)
        {
            foot.constraint.eulerAngles = new Vector3 (0, Mathf.Lerp(foot.constraint.eulerAngles.y,  target, timeElapsed / lerpDuration), 0);

            if (Mathf.Abs(target - foot.constraint.eulerAngles.y) <= 0.01f)
                break;
            
            //foot.ikPosition = Vector3.Lerp(startPosition, targetPosition, timeElapsed / 0.2f);
            //foot.SetIKRotation(feetPivot.rotation);
            
            yield return null;
            timeElapsed += Time.deltaTime;
        }

        //foot.SetIKRotation(feetPivot.rotation);
        //foot.ikPosition = foot.target.position;

        while (rotatedAmount <= rotationDelta && rotatedAmount < 170 && deltaValue != 0)
        {
            //Debug.Log(deltaValue);
            foot.constraint.eulerAngles = new Vector3(0, feetPivot.eulerAngles.y, 0);
            //foot.SetIKRotation(feetPivot.rotation);
            //foot.ikPosition = foot.target.position;
            rotatedAmount = rotationDelta;
            yield return null;
        }
        
        foot.constraint.position -= new Vector3(0, 0.05f, 0);
        
        if (moveOtherFoot)
        {
            moveOtherFoot = false;
            
            if(foot.Equals(foot2))
                StartCoroutine(LerpPositionCo(this.foot));
            else
                StartCoroutine(LerpPositionCo(foot2));
        }
        else
        {
            rotationDelta -= rotatedAmount;
            startRotation = Mathf.Abs(feetPivot.eulerAngles.y);
            
            moveOtherFoot = true;
            isMovingFoot = false;
        }
        
        Debug.Log("Final foot movement angle: " + rotatedAmount + " | " + foot.transform.name);
    }
}

