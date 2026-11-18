using UnityEngine;

public class AutoRotate : MonoBehaviour
{
    public float rotationSpeed = 10f; 
    private bool isRotating = true;

    void Update()
    {
        if (isRotating)
        {
            transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
        }

        if (Input.GetMouseButtonDown(0))
        {
            isRotating = false;
        }

        if (Input.GetMouseButton(0))
        {
            float mouseX = Input.GetAxis("Mouse X");
            transform.Rotate(Vector3.up * mouseX * rotationSpeed * Time.deltaTime * 10);
        }

        if (Input.GetMouseButtonUp(0))
        {
            isRotating = true;
        }
        if(Input.GetKey(KeyCode.Equals))
        {
            transform.localScale *= 1.005f;
        }
        if (Input.GetKey(KeyCode.Minus))
        {
            transform.localScale *= 0.995f;
        }
    }
}
