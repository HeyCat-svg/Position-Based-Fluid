using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMove : MonoBehaviour {

    public float m_RotationSpeed = 60.0f;
    public float m_MoveSpeed = 12.0f;
    public CharacterController m_Controller;
    public bool isStatic = false;

    float m_XRotation = 0.0f;
    float m_YRotation = 0.0f;

    void Start() {
        if (m_Controller == null) {
            m_Controller = GetComponent<CharacterController>();
        }
    }

    void Update() {
        if (isStatic) {
            return;
        }

        float dt = Time.deltaTime;
        float mouseX = Input.GetAxis("Mouse X") * m_RotationSpeed * dt;
        float mouseY = Input.GetAxis("Mouse Y") * m_RotationSpeed * dt;

        m_XRotation -= mouseY;
        m_XRotation = Mathf.Clamp(m_XRotation, -89.9f, 89.9f);
        m_YRotation += mouseX;
        this.transform.localRotation = Quaternion.Euler(m_XRotation, m_YRotation, 0.0f);

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) {
            move += transform.forward * m_MoveSpeed * dt;
        }
        if (Input.GetKey(KeyCode.S)) {
            move -= transform.forward * m_MoveSpeed * dt;
        }
        if (Input.GetKey(KeyCode.A)) {
            move -= transform.right * m_MoveSpeed * dt;
        }
        if (Input.GetKey(KeyCode.D)) {
            move += transform.right * m_MoveSpeed * dt;
        }
        if (Input.GetKey(KeyCode.Q)) {
            move -= transform.up * m_MoveSpeed * dt;
        }
        if (Input.GetKey(KeyCode.E)) {
            move += transform.up * m_MoveSpeed * dt;
        }
        m_Controller.Move(move);
    }
}
