using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class HandMotionManager : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5005;
    private Thread receiveThread;
    private UdpClient client;
    private string lastReceivedCommand = "no_motion";
    private string currentCommand = "";

    [Header("Wrist (Physics Driven)")]
    public ArticulationBody wristJoint;
    [Range(-90f, 90f)] public float wristPitch = 0f; // Pronation / Supination (X)
    [Range(-90f, 90f)] public float wristRoll = 0f;  // Radial / Ulnar Deviation (Y)
    [Range(-90f, 90f)] public float wristYaw = 0f;   // Flexion / Extension (Z)

    [Header("Palm & Thumb Base")]
    public ArticulationBody palmJoint;
    [Range(-90f, 90f)] public float palmCupAngle = 0f;
    public ArticulationBody thumbBaseJoint;
    [Range(-90f, 90f)] public float thumbBaseAngle = 0f;

    [Header("Fingers (Flexion / Curling)")]
    public ArticulationBody[] thumbFlexion;
    [Range(-180f, 180f)] public float thumbAngle = 0f;

    public ArticulationBody[] indexFlexion;
    [Range(-180f, 180f)] public float indexAngle = 0f;

    public ArticulationBody[] middleFlexion;
    [Range(-180f, 180f)] public float middleAngle = 0f;

    public ArticulationBody[] ringFlexion;
    [Range(-180f, 180f)] public float ringAngle = 0f;

    public ArticulationBody[] pinkyFlexion;
    [Range(-180f, 180f)] public float pinkyAngle = 0f;

    [Header("Fingers (Spread / Splay - INERTIA FIX)")]
    public ArticulationBody[] spreadJoints;
    [Range(-45f, 45f)] public float fingerSpreadAngle = 0f;

    [Header("Motor Settings")]
    public float motorStiffness = 10000000f;
    public float motorDamping = 100000f;
    private float safeForceLimit = 100000f;

    void Start()
    {
        InitializeSingleJoint(wristJoint, true);
        InitializeSingleJoint(palmJoint, false);
        InitializeSingleJoint(thumbBaseJoint, false);

        InitializeChain(thumbFlexion);
        InitializeChain(indexFlexion);
        InitializeChain(middleFlexion);
        InitializeChain(ringFlexion);
        InitializeChain(pinkyFlexion);

        // Lock the virtual spread joints
        InitializeChain(spreadJoints);

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void ReceiveData()
    {
        client = new UdpClient(port);
        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                lastReceivedCommand = Encoding.UTF8.GetString(data);
            }
            catch (Exception) { }
        }
    }

    void Update()
    {
        if (lastReceivedCommand != currentCommand)
        {
            currentCommand = lastReceivedCommand;
            ApplyCommand(currentCommand);
        }

        // Manual controls (1 through 0)
        if (Input.GetKeyDown(KeyCode.Alpha1)) lastReceivedCommand = "radial_deviation";
        if (Input.GetKeyDown(KeyCode.Alpha2)) lastReceivedCommand = "ulnar_deviation";
        if (Input.GetKeyDown(KeyCode.Alpha3)) lastReceivedCommand = "wrist_flexion";
        if (Input.GetKeyDown(KeyCode.Alpha4)) lastReceivedCommand = "wrist_extension";
        if (Input.GetKeyDown(KeyCode.Alpha5)) lastReceivedCommand = "supination";
        if (Input.GetKeyDown(KeyCode.Alpha6)) lastReceivedCommand = "pronation";
        if (Input.GetKeyDown(KeyCode.Alpha7)) lastReceivedCommand = "power_grip";
        if (Input.GetKeyDown(KeyCode.Alpha8)) lastReceivedCommand = "chuck_grip";
        if (Input.GetKeyDown(KeyCode.Alpha9)) lastReceivedCommand = "pinch_grip";
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Space)) lastReceivedCommand = "open_hand";
    }

    void ApplyCommand(string command)
    {
        wristPitch = 0f;
        wristRoll = 0f;
        wristYaw = 0f;
        SetFingers(0f, 0f, 0f, 0f, 0f, 0f);
        palmCupAngle = 0f;
        thumbBaseAngle = 0f;

        switch (command)
        {
            case "radial_deviation": wristRoll = 30f; break;
            case "ulnar_deviation": wristRoll = -30f; break;
            case "wrist_flexion": wristYaw = 45f; break;
            case "wrist_extension": wristYaw = -45f; break;
            case "supination": wristPitch = -90f; break;
            case "pronation": wristPitch = 90f; break;
            case "power_grip":
                SetFingers(75f, 75f, 75f, 75f, 75f, 0f);
                palmCupAngle = 0f;
                thumbBaseAngle = 45f;
                break;
            case "chuck_grip":
                // FIXED: Spread is now 0f so the middle finger stays glued to the index finger.
                // Thumb reaches up (40f) and swings across (65f) to meet both fingers.
                SetFingers(40f, 45f, 55f, 10f, 10f, 0f);
                thumbBaseAngle = 60f;
                break;

            case "pinch_grip":
                // FIXED: Thumb swings almost entirely across the hand (80f) to face the index finger.
                // Both index and thumb curl equally (45f/50f) to meet tip-to-tip.
                SetFingers(25f, 50f, 10f, 10f, 10f, 0f);
                thumbBaseAngle = 80f;
                break;
            case "open_hand":
            case "no_motion": break;
        }
    }

    void SetFingers(float t, float i, float m, float r, float p, float splay)
    {
        thumbAngle = t;
        indexAngle = i;
        middleAngle = m;
        ringAngle = r;
        pinkyAngle = p;
        fingerSpreadAngle = splay;
    }

    void FixedUpdate()
    {
        if (wristJoint != null)
        {
            ArticulationDrive xDrive = wristJoint.xDrive; xDrive.target = wristPitch; wristJoint.xDrive = xDrive;
            ArticulationDrive yDrive = wristJoint.yDrive; yDrive.target = wristRoll; wristJoint.yDrive = yDrive;
            ArticulationDrive zDrive = wristJoint.zDrive; zDrive.target = wristYaw; wristJoint.zDrive = zDrive;
        }

        BendSingleJoint(palmJoint, palmCupAngle);
        BendSingleJoint(thumbBaseJoint, thumbBaseAngle);

        BendChain(thumbFlexion, thumbAngle);
        BendChain(indexFlexion, indexAngle);
        BendChain(middleFlexion, middleAngle);
        BendChain(ringFlexion, ringAngle);
        BendChain(pinkyFlexion, pinkyAngle);

        BendChain(spreadJoints, fingerSpreadAngle);
    }

    private void InitializeSingleJoint(ArticulationBody joint, bool isSpherical)
    {
        if (joint == null) return;
        ArticulationDrive drive = joint.xDrive;
        drive.stiffness = motorStiffness;
        drive.damping = motorDamping;
        drive.forceLimit = safeForceLimit;
        drive.target = 0f;
        joint.xDrive = drive;

        if (isSpherical)
        {
            ArticulationDrive yDrive = joint.yDrive;
            yDrive.stiffness = motorStiffness;
            yDrive.damping = motorDamping;
            yDrive.forceLimit = safeForceLimit;
            yDrive.target = 0f;
            joint.yDrive = yDrive;

            ArticulationDrive zDrive = joint.zDrive;
            zDrive.stiffness = motorStiffness;
            zDrive.damping = motorDamping;
            zDrive.forceLimit = safeForceLimit;
            zDrive.target = 0f;
            joint.zDrive = zDrive;
        }
    }

    private void InitializeChain(ArticulationBody[] joints)
    {
        if (joints == null) return;
        foreach (ArticulationBody joint in joints) InitializeSingleJoint(joint, false);
    }

    private void BendSingleJoint(ArticulationBody joint, float targetAngle)
    {
        if (joint == null) return;
        ArticulationDrive drive = joint.xDrive;
        drive.target = targetAngle;
        joint.xDrive = drive;
    }

    private void BendChain(ArticulationBody[] joints, float targetAngle)
    {
        if (joints == null) return;
        foreach (ArticulationBody joint in joints) BendSingleJoint(joint, targetAngle);
    }

    void OnApplicationQuit()
    {
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (client != null) client.Close();
    }
}