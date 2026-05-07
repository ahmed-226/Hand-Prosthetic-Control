using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Motion : MonoBehaviour
{
    [Header("Motion Settings")]
    public float speed = 2.0f;       // How fast the hand opens/closes
    public float maxAngle = 45.0f;   // How far the joints bend

    private ArticulationBody[] allJoints;

    void Start()
    {
        // This automatically finds EVERY joint inside the hand so you don't have to link them manually!
        allJoints = GetComponentsInChildren<ArticulationBody>();
    }

    void Update()
    {
        // Mathf.Sin creates a smooth wave that goes back and forth over time
        float currentAngle = Mathf.Sin(Time.time * speed) * maxAngle;

        // Loop through every joint we found
        foreach (ArticulationBody joint in allJoints)
        {
            // We skip the "root" (the base of the hand) so the whole hand doesn't spin around
            if (joint.isRoot) continue;

            // Get the current drive, update the target angle, and apply it back
            ArticulationDrive drive = joint.xDrive;
            drive.target = currentAngle;
            joint.xDrive = drive;
        }
    }
}