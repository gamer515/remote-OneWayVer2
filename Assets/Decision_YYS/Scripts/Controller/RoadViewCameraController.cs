using UnityEngine;

public class RoadViewCameraController : MonoBehaviour
{
    private Transform playerTransform;

    private float maxDistance = 20f;
    private float smoothTime = 2f;

    private float followVelocity;

    public void BindPlayerTransform(Transform playerTransform)
    {
        if(playerTransform == null)
        {
            Debug.LogError("Player Transform is null. Cannot bind.");
            return;
        }

        this.playerTransform = playerTransform;
    }

    private void LateUpdate()
    {
        if(playerTransform == null)
        {
            Debug.LogWarning("Player Transform is not bound. Skipping camera follow.");
            return;
        }

        float distanceZ = Vector3.Distance(playerTransform.position, Camera.main.transform.position);
        
        if (distanceZ < maxDistance)
        {
            return;
        }

        float targetZ = playerTransform.position.z - Mathf.Sign(distanceZ) * maxDistance;
        
        float nextZ = Mathf.SmoothDamp(
            transform.position.z, 
            targetZ, 
            ref followVelocity,
            smoothTime);
    
        transform.position = new Vector3(transform.position.x, transform.position.y, nextZ);
    }
}
